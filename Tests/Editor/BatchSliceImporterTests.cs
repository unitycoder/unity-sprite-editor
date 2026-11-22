using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterSpriteEditor.DataAccess;
using BetterSpriteEditor.Model;
using BetterSpriteEditor.Operations;
using BetterSpriteEditor.Operations.Slicing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BetterSpriteEditor.Tests
{
    internal sealed class BatchSliceImporterTests
    {
        private string directory, backupRoot;
        private readonly List<BatchSliceItem> items = new();
        private SliceRecipe recipe;
        private BatchSliceBackups backups;
        private BatchSliceProcessor processor;
        private int saves;

        [SetUp]
        public void SetUp()
        {
            directory = "Assets/BetterSpriteEditorBatchTests-" + GUID.Generate();
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(directory));
            backupRoot = Path.Combine(Path.GetTempPath(), "bse-batch-" + GUID.Generate());
            backups = new BatchSliceBackups(backupRoot);
            saves = 0;
            processor = new BatchSliceProcessor(backups: backups, save: document => { saves++; SpriteDocumentRepository.Save(document); });
            recipe = new SliceRecipe { CellWidth = 8, CellHeight = 8, SkipEmpty = false };
            items.Clear();
            foreach (string name in new[] { "cat", "dog" })
            {
                string path = directory + "/" + name + ".png";
                var pixels = new Texture2D(16, 16);
                try
                {
                    pixels.SetPixels32(Enumerable.Repeat(new Color32(255, 255, 255, 255), 256).ToArray());
                    pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG());
                }
                finally { Object.DestroyImmediate(pixels); }
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.SaveAndReimport();
                var item = new BatchSliceItem { Guid = AssetDatabase.AssetPathToGUID(path), Path = path };
                items.Add(item);
                var document = Load(item);
                document.Regions.Add(new SpriteRegion
                {
                    Id = GUID.Generate(), Name = "hand-named", Rect = new Rect(0, 0, 8, 8),
                    Alignment = SpriteAlignment.Custom, Pivot = new Vector2(0.25f, 0.75f), Border = Vector4.one,
                    CustomData = string.Empty,
                    Outlines = new List<Vector2[]> { new[] { new Vector2(-2, -2), new Vector2(2, -2), new Vector2(0, 2) } },
                    PhysicsOutlines = new List<Vector2[]> { new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(0, 1) } }
                });
                SpriteDocumentRepository.Save(document);
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var item in items)
            {
                RecoveryStore.Delete(item.Guid);
                string order = $"UserSettings/BetterSpriteEditor/order-{item.Guid}.json";
                if (File.Exists(order)) File.Delete(order);
            }
            AssetDatabase.DeleteAsset(directory);
            if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, true);
        }

        private static SpriteDocument Load(BatchSliceItem item) => SpriteDocumentRepository.Load(AssetDatabase.LoadAssetAtPath<Texture2D>(item.Path));
        private static Sprite Sprite(BatchSliceItem item, string name) => AssetDatabase.LoadAllAssetsAtPath(item.Path).OfType<Sprite>().Single(s => s.name == name);
        private static (string, long) Identity(Sprite sprite)
        {
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long id), Is.True);
            return (guid, id);
        }

        [Test]
        public void PreviewAndApply_UseTargetNamesPreservePrefabReferencesAndSaveOncePerAsset()
        {
            var before = items.Select(i => Load(i).Regions.Single().Clone()).ToArray();
            var identity = Identity(Sprite(items[0], "hand-named"));
            string prefabPath = directory + "/reference.prefab";
            var root = new GameObject("Reference", typeof(SpriteRenderer));
            try
            {
                root.GetComponent<SpriteRenderer>().sprite = Sprite(items[0], "hand-named");
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { Object.DestroyImmediate(root); }
            for (int i = 0; i < items.Count; i++)
            {
                var meta = File.ReadAllBytes(items[i].Path + ".meta");
                processor.Preview(items[i], recipe);
                Assert.That(items[i].Status, Is.EqualTo(BatchSliceStatus.Ready), items[i].Message);
                Assert.That(File.ReadAllBytes(items[i].Path + ".meta"), Is.EqualTo(meta));
            }
            Assert.That(saves, Is.Zero);
            backups.Begin();
            foreach (var item in items) processor.Apply(item, recipe);
            Assert.That(items.All(i => i.Status == BatchSliceStatus.Applied), Is.True, string.Join("; ", items.Select(i => i.Message)));
            Assert.That(saves, Is.EqualTo(2));
            for (int i = 0; i < items.Count; i++)
            {
                var after = Load(items[i]);
                Assert.That(after.Regions.Count, Is.EqualTo(4));
                Assert.That(after.FindRegion(before[i].Id).ContentEquals(before[i]), Is.True);
                string name = Path.GetFileNameWithoutExtension(items[i].Path);
                Assert.That(after.Regions.Select(r => r.Name), Does.Contain(name + "_0").And.Contain(name + "_3"));
            }
            var reference = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).GetComponent<SpriteRenderer>().sprite;
            Assert.That(reference, Is.Not.Null);
            Assert.That(Identity(reference), Is.EqualTo(identity));
            Assert.That(Load(items[0]).Regions.Select(r => r.Id).Intersect(Load(items[1]).Regions.Select(r => r.Id)), Is.Empty);
            foreach (var item in items) { processor.Preview(item, recipe); processor.Apply(item, recipe); }
            Assert.That(items.All(i => i.Status == BatchSliceStatus.Unchanged), Is.True);
            Assert.That(saves, Is.EqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompletedBatch_RestoresOriginalRegionsIdsAndShapesAfterReopening(bool replace)
        {
            recipe.Existing = replace ? SliceExistingPolicy.ReplaceAll : SliceExistingPolicy.KeepExisting;
            var before = Load(items[0]).Regions.Single().Clone();
            var identity = Identity(Sprite(items[0], "hand-named"));
            processor.Preview(items[0], recipe); backups.Begin(); processor.Apply(items[0], recipe);
            Assert.That(items[0].Status, Is.EqualTo(BatchSliceStatus.Applied), items[0].Message);
            var reopenedBackups = new BatchSliceBackups(backupRoot);
            var path = reopenedBackups.LastFiles().Single();
            Assert.That(BatchSliceBackups.Read(path).Status, Is.EqualTo("Applied"));
            var restore = new BatchSliceItem { Guid = items[0].Guid, BackupPath = path };
            new BatchSliceProcessor(backups: reopenedBackups).Restore(restore);
            Assert.That(restore.Status, Is.EqualTo(BatchSliceStatus.Restored), restore.Message);
            Assert.That(Load(items[0]).Regions.Single().ContentEquals(before), Is.True);
            Assert.That(Identity(Sprite(items[0], "hand-named")), Is.EqualTo(identity));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Apply_RejectsChangedRecipeOrChangedImporterAfterPreview(bool changeImporter)
        {
            processor.Preview(items[0], recipe); backups.Begin();
            if (changeImporter)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(items[0].Path);
                importer.spritePixelsPerUnit = 37; importer.SaveAndReimport();
            }
            else recipe.PaddingX++;
            string before = AssetState.Revision(items[0].Path);
            processor.Apply(items[0], recipe);
            Assert.That(items[0].Status, Is.EqualTo(BatchSliceStatus.Error));
            Assert.That(saves, Is.Zero);
            Assert.That(AssetState.Revision(items[0].Path), Is.EqualTo(before));
            Assert.That(backups.LastFiles(), Is.Empty);
        }

        [Test]
        public void Restore_RejectsLaterEdits()
        {
            processor.Preview(items[0], recipe); backups.Begin(); processor.Apply(items[0], recipe);
            var document = Load(items[0]); document.Regions[0].Name = "later-edit";
            SpriteDocumentRepository.Save(document);
            string before = AssetState.Revision(items[0].Path);
            processor.Restore(items[0]);
            Assert.That(items[0].Status, Is.EqualTo(BatchSliceStatus.Error));
            Assert.That(items[0].Message, Does.Contain("changed after"));
            Assert.That(AssetState.Revision(items[0].Path), Is.EqualTo(before));
        }

        [Test]
        public void Restore_RejectsLaterPresentationOrderEvenWhenMetaIsUnchanged()
        {
            processor.Preview(items[0], recipe); backups.Begin(); processor.Apply(items[0], recipe);
            var document = Load(items[0]);
            document.Regions.Reverse();
            RegionOrderStore.Save(document);
            string before = AssetState.Revision(items[0].Path);
            processor.Restore(items[0]);
            Assert.That(items[0].Status, Is.EqualTo(BatchSliceStatus.Error));
            Assert.That(items[0].Message, Does.Contain("changed after"));
            Assert.That(AssetState.Revision(items[0].Path), Is.EqualTo(before));
            Assert.That(Load(items[0]).Regions[0].Id, Is.EqualTo(document.Regions[0].Id));
        }

        [Test]
        public void InterruptedSave_RetainsBeforeSnapshotAndDoesNotStopOtherTextures()
        {
            var failing = new BatchSliceProcessor(backups: backups, save: document =>
            {
                Assert.That(backups.LastFiles().Any(file => BatchSliceBackups.Read(file).Before.TextureGuid == document.AssetGuid), Is.True);
                if (document.AssetGuid == items[0].Guid) throw new IOException("Injected save failure");
                SpriteDocumentRepository.Save(document);
            });
            foreach (var item in items) failing.Preview(item, recipe);
            backups.Begin();
            var queue = new BatchSliceQueue(items, item => failing.Apply(item, recipe));
            queue.Step(); queue.Step();
            Assert.That(items[0].Status, Is.EqualTo(BatchSliceStatus.Error));
            Assert.That(BatchSliceBackups.Read(items[0].BackupPath).Status, Is.EqualTo("Pending"));
            Assert.That(items[1].Status, Is.EqualTo(BatchSliceStatus.Applied), items[1].Message);
            Assert.That(Load(items[0]).Regions.Count, Is.EqualTo(1));
            failing.Restore(items[0]);
            Assert.That(items[0].Message, Does.Contain("Only completed"));
        }

        [Test]
        public void PendingEditsAndSingleMode_BlockPreview()
        {
            var blocked = new BatchSliceProcessor(pendingEdits: _ => "Unsaved editor edits");
            blocked.Preview(items[0], recipe);
            Assert.That(items[0].Message, Does.Contain("Unsaved"));
            var importer = (TextureImporter)AssetImporter.GetAtPath(items[1].Path);
            importer.spriteImportMode = SpriteImportMode.Single; importer.SaveAndReimport();
            string before = AssetState.Revision(items[1].Path);
            processor.Preview(items[1], recipe);
            Assert.That(items[1].Status, Is.EqualTo(BatchSliceStatus.Error));
            Assert.That(items[1].Message, Does.Contain("Multiple"));
            Assert.That(AssetState.Revision(items[1].Path), Is.EqualTo(before));
        }

        [Test]
        public void Retry_CannotOverwriteTheOriginalBackupInTheSameRun()
        {
            backups.Begin();
            var document = Load(items[0]);
            string path = backups.WriteBefore(document);
            string before = File.ReadAllText(path);
            document.Regions[0].Name = "retry-state";
            Assert.Throws<InvalidOperationException>(() => backups.WriteBefore(document));
            Assert.That(File.ReadAllText(path), Is.EqualTo(before));
            backups.Begin();
            Assert.That(backups.WriteBefore(document), Is.Not.EqualTo(path));
            Assert.That(File.ReadAllText(path), Is.EqualTo(before));
        }
    }
}
