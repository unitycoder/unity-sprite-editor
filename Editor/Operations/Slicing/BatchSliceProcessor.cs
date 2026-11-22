using System;
using System.IO;
using System.Linq;
using BetterSpriteEditor.DataAccess;
using BetterSpriteEditor.Model;
using UnityEditor;
using UnityEngine;

namespace BetterSpriteEditor.Operations.Slicing
{
    internal enum BatchSliceStatus { NotReviewed, Ready, Unchanged, Applied, Error, Restored }

    internal sealed class BatchSliceItem
    {
        internal string Guid;
        internal string Path;
        internal bool Included = true;
        internal BatchSliceStatus Status;
        internal string Message;
        internal string Revision;
        internal string RecipeJson;
        internal string Fingerprint;
        internal int Added;
        internal int Removed;
        internal string BackupPath;
    }

    /// <summary>Processes one asset at a time; the caller owns the cancellable editor update queue.</summary>
    internal sealed class BatchSliceProcessor
    {
        private readonly Func<string, string> pendingEdits;
        private readonly Func<SpriteDocument, TextureAlphaMap> loadAlpha;
        private readonly Action<SpriteDocument> save;
        internal readonly BatchSliceBackups Backups;

        internal BatchSliceProcessor(Func<string, string> pendingEdits = null, BatchSliceBackups backups = null,
            Func<SpriteDocument, TextureAlphaMap> loadAlpha = null, Action<SpriteDocument> save = null)
        {
            this.pendingEdits = pendingEdits;
            this.loadAlpha = loadAlpha ?? SpriteDocumentRepository.LoadAlphaMap;
            this.save = save ?? SpriteDocumentRepository.Save;
            Backups = backups ?? new BatchSliceBackups();
        }

        internal (SpriteDocument document, SlicePlan plan) Preview(BatchSliceItem item, SliceRecipe recipe)
        {
            item.BackupPath = null;
            item.Status = BatchSliceStatus.NotReviewed;
            item.Added = item.Removed = 0;
            item.Message = null;
            item.Revision = item.Fingerprint = item.RecipeJson = null;
            try
            {
                var document = Load(item.Guid);
                item.Path = document.AssetPath;
                var plan = SlicePlanner.Build(document, recipe.NeedsAlpha ? loadAlpha(document) : null, recipe);
                if (plan.Error != null) throw new InvalidOperationException(plan.Error);
                SpriteDocumentRepository.EnsureUnchanged(document);
                item.Revision = document.BaselineRevision;
                item.RecipeJson = JsonUtility.ToJson(recipe);
                item.Fingerprint = plan.Fingerprint();
                item.Added = plan.AddedCount;
                item.Removed = plan.RemovedCount;
                item.Status = plan.CanApply ? BatchSliceStatus.Ready : BatchSliceStatus.Unchanged;
                item.Message = $"+{item.Added}, −{item.Removed}; {plan.EmptyCount} empty, {plan.OverlapCount} existing overlaps.";
                return (document, plan);
            }
            catch (Exception exception) { Fail(item, exception.Message); return (null, null); }
        }

        internal void Apply(BatchSliceItem item, SliceRecipe recipe)
        {
            if (!item.Included || item.Status != BatchSliceStatus.Ready) return;
            try
            {
                if (JsonUtility.ToJson(recipe) != item.RecipeJson) throw new InvalidOperationException("Settings changed. Preview this texture again.");
                var document = Load(item.Guid);
                if (document.BaselineRevision != item.Revision) throw new InvalidOperationException("The texture or import settings changed after preview. Preview it again.");
                var plan = SlicePlanner.Build(document, recipe.NeedsAlpha ? loadAlpha(document) : null, recipe);
                if (!plan.CanApply || plan.Fingerprint() != item.Fingerprint)
                    throw new InvalidOperationException(plan.Error ?? "The slicing result changed after preview. Preview it again.");
                var command = plan.CreateCommand(document);
                if (command == null) throw new InvalidOperationException("The document changed after preview.");
                // Do not mutate the importer unless the original state has been durably saved.
                item.BackupPath = Backups.WriteBefore(document);
                var record = BatchSliceBackups.Read(item.BackupPath);
                new HistoryStack(document).Do(command);
                save(document);
                record.AfterRevision = AssetState.Revision(document.AssetPath);
                record.AfterRegionOrder = document.Regions.Select(region => region.Id.ToString()).ToArray();
                record.Status = "Applied";
                BatchSliceBackups.Write(item.BackupPath, record);
                item.Status = BatchSliceStatus.Applied;
                item.Message = $"Applied: +{item.Added}, −{item.Removed}.";
            }
            catch (Exception exception)
            {
                Fail(item, exception.Message + (item.BackupPath == null ? string.Empty
                    : $" Original metadata retained at {item.BackupPath}. An interrupted save may need inspection."));
            }
        }

        internal void Restore(BatchSliceItem item)
        {
            try
            {
                var record = BatchSliceBackups.Read(item.BackupPath);
                if (record.Status != "Applied" || string.IsNullOrEmpty(record.AfterRevision))
                    throw new InvalidOperationException("Only completed batch saves can be restored automatically. The original snapshot is retained for inspection.");
                if (record.Before.TextureGuid != item.Guid) throw new InvalidDataException("Backup texture identity does not match.");
                var current = Load(item.Guid);
                if (current.BaselineRevision != record.AfterRevision || record.AfterRegionOrder == null
                    || !current.Regions.Select(region => region.Id.ToString()).SequenceEqual(record.AfterRegionOrder))
                    throw new InvalidOperationException("This texture changed after the batch. Restore was blocked to preserve those edits.");
                var original = DocumentSnapshot.RestoreDocument(record.Before, current.Texture);
                original.BaselineRevision = current.BaselineRevision;
                save(original);
                record.Status = "Restored";
                BatchSliceBackups.Write(item.BackupPath, record);
                item.Status = BatchSliceStatus.Restored;
                item.Message = "Restored original sprite metadata.";
            }
            catch (Exception exception) { Fail(item, exception.Message); }
        }

        private SpriteDocument Load(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("The texture was moved out of the project or deleted.");
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) throw new InvalidOperationException("Batch slicing requires a writable texture under Assets.");
            string issue = pendingEdits?.Invoke(guid);
            if (issue != null) throw new InvalidOperationException(issue);
            if (RecoveryStore.TryLoad(guid) != null) throw new InvalidOperationException("This texture has pending recovery data. Open it in Better Sprite Editor and resolve the recovered edits first.");
            var importer = AssetImporter.GetAtPath(path);
            if (importer != null && EditorUtility.IsDirty(importer))
                throw new InvalidOperationException("The texture importer has unapplied edits. Apply or revert them first.");
            foreach (var editor in ActiveEditorTracker.sharedTracker.activeEditors)
                if (editor.target == importer && editor.serializedObject.hasModifiedProperties)
                    throw new InvalidOperationException("The Inspector has pending texture edits. Apply or revert them first.");
            return SpriteDocumentRepository.Load(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        }

        private static void Fail(BatchSliceItem item, string issue) { item.Status = BatchSliceStatus.Error; item.Message = issue; }
    }
}
