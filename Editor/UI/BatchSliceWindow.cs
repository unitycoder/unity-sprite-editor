using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterSpriteEditor.DataAccess;
using BetterSpriteEditor.Model;
using BetterSpriteEditor.Operations.Slicing;
using BetterSpriteEditor.UI.Viewport;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterSpriteEditor.UI
{
    internal sealed class BatchSliceWindow : EditorWindow
    {
        private readonly List<BatchSliceItem> items = new();
        private BatchSliceProcessor processor;
        private BatchSliceQueue queue;
        private SliceRecipeFields settings;
        private VisualElement setup;
        private ListView assets, candidates;
        private SpriteViewportElement viewport;
        private Label progress, details;
        private Button preview, apply, cancel, restore;
        private bool restoreMode;
        private bool newRunRequired = true;
        private string operation;

        [MenuItem("Window/2D/Better Sprite Editor Batch Slice")]
        private static void OpenMenu() => Open();

        [MenuItem("Assets/Batch Slice in Better Sprite Editor", false, 2101)]
        private static void OpenSelection() => Open();

        [MenuItem("Assets/Batch Slice in Better Sprite Editor", true)]
        private static bool CanOpenSelection() => Selection.assetGUIDs.Length > 0;

        internal static void Open(SliceRecipe recipe = null)
        {
            var window = GetWindow<BatchSliceWindow>();
            window.titleContent = new GUIContent("Batch Slice");
            window.minSize = new Vector2(1000, 580);
            window.Show();
            if (window.settings == null) window.CreateGUI();
            if (window.queue?.IsRunning == true) return;
            if (window.restoreMode) { window.items.Clear(); window.restoreMode = false; }
            if (recipe != null) window.settings.Value = recipe;
            window.AddSelection();
        }

        private void CreateGUI()
        {
            rootVisualElement.Clear();
            processor = new BatchSliceProcessor(BetterSpriteEditorWindow.PendingEditsForAsset);
            var body = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            rootVisualElement.Add(body);
            setup = new ScrollView { style = { width = 340, flexShrink = 0, paddingLeft = 6, paddingRight = 6 } };
            body.Add(setup);
            setup.Add(new Label("Slicing settings") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            settings = new SliceRecipeFields();
            settings.Changed += SettingsChanged;
            setup.Add(settings);
            setup.Add(new Button(AddSelection) { text = "Add Project selection / folders" });
            setup.Add(new Button(() => { items.Clear(); restoreMode = false; Refresh(); ClearPreview(); }) { text = "Clear list" });
            setup.Add(new Label("Only Sprite textures in Multiple mode can be sliced. Preview makes no asset changes. Apply saves each included texture separately.")
                { style = { whiteSpace = WhiteSpace.Normal, marginTop = 8 } });
            setup.Add(new Label("Keep Existing preserves regions that overlap a cell. Replace All removes all existing regions, creates new sprite IDs, and can break references.")
                { style = { whiteSpace = WhiteSpace.Normal, marginTop = 8 } });
            var assetColumn = new VisualElement { style = { width = 280, flexShrink = 0 } };
            body.Add(assetColumn);
            assetColumn.Add(new Label("Included textures · select to inspect"));
            assets = new ListView { itemsSource = items, fixedItemHeight = 58, style = { flexGrow = 1 }, selectionType = SelectionType.Single };
            assets.makeItem = () =>
            {
                var row = new VisualElement();
                var include = new Toggle { name = "include" };
                include.RegisterValueChangedCallback(evt =>
                {
                    if (include.userData is BatchSliceItem item) item.Included = evt.newValue;
                    UpdateActions();
                });
                row.Add(include);
                row.Add(new Label { name = "status" });
                return row;
            };
            assets.bindItem = (element, index) =>
            {
                var item = items[index];
                var include = element.Q<Toggle>();
                include.userData = item;
                include.label = Path.GetFileName(item.Path);
                include.SetValueWithoutNotify(item.Included);
                include.SetEnabled(queue?.IsRunning != true);
                element.Q<Label>("status").text = $"{item.Status} · +{item.Added}, −{item.Removed}";
                element.tooltip = item.Path + "\n" + item.Message;
            };
            assets.selectionChanged += _ => InspectSelected();
            assetColumn.Add(assets);
            var inspection = new VisualElement { style = { flexGrow = 1, minWidth = 300 } };
            body.Add(inspection);
            viewport = new SpriteViewportElement { style = { flexGrow = 1, minHeight = 160 } };
            inspection.Add(viewport);
            details = new Label { style = { whiteSpace = WhiteSpace.Normal, marginLeft = 6, marginRight = 6 } };
            inspection.Add(details);
            candidates = SlicePanelElement.MakeCandidateList();
            inspection.Add(candidates);
            progress = new Label { text = "Add textures or folders, then preview the included textures.", style = { whiteSpace = WhiteSpace.Normal, marginLeft = 6 } };
            rootVisualElement.Add(progress);
            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            preview = new Button(PreviewAll) { text = "Preview included" };
            apply = new Button(ApplyReviewed) { text = "Apply reviewed textures" };
            cancel = new Button(CancelRun) { text = "Cancel remaining" };
            restore = new Button(LoadLastBatch) { text = "Load last batch for restore" };
            actions.Add(preview); actions.Add(apply); actions.Add(cancel); actions.Add(restore);
            rootVisualElement.Add(actions);
            UpdateActions();
        }

        private void AddSelection()
        {
            if (restoreMode) { items.Clear(); restoreMode = false; }
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (string guid in Selection.assetGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path))
                    foreach (string child in AssetDatabase.FindAssets("t:Texture2D", new[] { path })) paths.Add(AssetDatabase.GUIDToAssetPath(child));
                else if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null) paths.Add(path);
            }
            foreach (string path in paths.OrderBy(p => p, StringComparer.Ordinal))
            {
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (items.All(item => item.Guid != guid)) items.Add(new BatchSliceItem { Guid = guid, Path = path });
            }
            Refresh();
        }

        private void SettingsChanged()
        {
            settings.Remember();
            foreach (var item in items)
            {
                item.Status = BatchSliceStatus.NotReviewed; item.Added = item.Removed = 0;
                item.Message = "Settings changed. Preview again.";
            }
            Refresh(); ClearPreview();
        }

        private void PreviewAll()
        {
            var recipe = settings.Value;
            if (recipe.Validate() is string error) { progress.text = error; return; }
            newRunRequired = true;
            StartRun("Preview", items.Where(item => item.Included), item =>
            {
                var result = processor.Preview(item, recipe);
                ShowPreview(item, result.document, result.plan);
            });
        }

        private void ApplyReviewed()
        {
            if (restoreMode)
            {
                StartRun("Restore", items.Where(item => item.Included && item.Status == BatchSliceStatus.Applied), processor.Restore);
                return;
            }
            var recipe = settings.Value;
            var included = items.Where(item => item.Included).ToList();
            if (!CanApply(included)) return;
            try
            {
                // Continue a cancelled run without losing access to its completed backups.
                if (newRunRequired || processor.Backups.RunDirectory == null)
                    processor.Backups.Begin();
                newRunRequired = false;
            }
            catch (Exception exception) { progress.text = "Cannot create batch backup: " + exception.Message; return; }
            StartRun("Apply", included.Where(item => item.Status == BatchSliceStatus.Ready), item => processor.Apply(item, recipe));
        }

        private void StartRun(string label, IEnumerable<BatchSliceItem> targets, Action<BatchSliceItem> action)
        {
            operation = label;
            queue = new BatchSliceQueue(targets, action);
            EditorApplication.update -= Step;
            if (queue.IsRunning) EditorApplication.update += Step;
            Refresh();
        }

        private void Step()
        {
            queue.Step();
            progress.text = $"{operation}: {queue.Completed}/{queue.Total}. {items.Count(i => i.Status == BatchSliceStatus.Error)} errors."
                + (operation == "Apply" ? " Completed textures are saved; cancellation stops only the remaining textures." : string.Empty);
            if (!queue.IsRunning) EditorApplication.update -= Step;
            Refresh();
        }

        private void CancelRun()
        {
            queue?.Cancel(); EditorApplication.update -= Step;
            progress.text = $"Stopped after {queue?.Completed ?? 0} textures. Completed saves remain; use Load last batch for restore if needed.";
            Refresh();
        }

        private void LoadLastBatch()
        {
            try
            {
                var records = processor.Backups.LastFiles().Select(path => (path, record: BatchSliceBackups.Read(path))).ToList();
                if (records.Count == 0) { progress.text = "No batch backup found."; return; }
                items.Clear(); restoreMode = true;
                foreach (var (path, record) in records)
                {
                    bool applied = record.Status == "Applied";
                    items.Add(new BatchSliceItem
                    {
                        Guid = record.Before.TextureGuid, Path = AssetDatabase.GUIDToAssetPath(record.Before.TextureGuid), BackupPath = path,
                        Included = applied, Status = applied ? BatchSliceStatus.Applied : record.Status == "Restored" ? BatchSliceStatus.Restored : BatchSliceStatus.Error,
                        Message = applied ? "Ready to restore original metadata if the texture has not changed since this batch."
                            : "Backup status: " + record.Status + ". Interrupted saves require inspection: " + path
                    });
                }
                progress.text = "Review the list, then Restore. Textures changed after the batch will be refused. Clear the list to return to slicing.";
                ClearPreview(); Refresh();
            }
            catch (Exception exception) { progress.text = exception.Message; }
        }

        private void InspectSelected()
        {
            if (queue?.IsRunning == true || assets.selectedItem is not BatchSliceItem item) return;
            if (restoreMode || item.Status is BatchSliceStatus.Applied or BatchSliceStatus.Restored)
            {
                ClearPreview(); details.text = item.Path + "\n" + item.Message; return;
            }
            var inspection = new BatchSliceItem { Guid = item.Guid, Path = item.Path };
            var result = processor.Preview(inspection, settings.Value);
            if (item.Status == BatchSliceStatus.Ready && (item.Revision != inspection.Revision || item.Fingerprint != inspection.Fingerprint))
            {
                item.Status = BatchSliceStatus.NotReviewed;
                item.Message = "The texture changed. Preview again before applying.";
                Refresh();
            }
            ShowPreview(inspection, result.document, result.plan);
        }

        private void ShowPreview(BatchSliceItem item, SpriteDocument document, SlicePlan plan)
        {
            viewport.SetSession(document, new SelectionModel());
            viewport.SetPreviewRects(plan?.PreviewRects);
            details.text = item.Path + "\n" + item.Message;
            candidates.itemsSource = plan?.Candidates;
            candidates.Rebuild();
        }

        private void ClearPreview()
        {
            viewport.SetSession(null, null); candidates.itemsSource = null; candidates.Rebuild(); details.text = string.Empty;
        }

        private static bool CanApply(List<BatchSliceItem> included) => included.Any(i => i.Status == BatchSliceStatus.Ready)
            && included.All(i => i.Status is BatchSliceStatus.Ready or BatchSliceStatus.Unchanged or BatchSliceStatus.Applied);

        private void Refresh() { assets.RefreshItems(); UpdateActions(); }

        private void UpdateActions()
        {
            bool busy = queue?.IsRunning == true;
            setup.SetEnabled(!busy);
            settings.SetEnabled(!busy && !restoreMode);
            preview.SetEnabled(!busy && !restoreMode && items.Any(item => item.Included));
            restore.SetEnabled(!busy);
            cancel.SetEnabled(busy);
            var included = items.Where(item => item.Included).ToList();
            int changes = included.Count(i => i.Status == (restoreMode ? BatchSliceStatus.Applied : BatchSliceStatus.Ready));
            apply.text = restoreMode ? $"Restore {changes} textures" : $"Apply {changes} reviewed textures";
            apply.SetEnabled(!busy && (restoreMode ? changes > 0 : CanApply(included)));
        }

        private void OnDisable() { queue?.Cancel(); EditorApplication.update -= Step; }
    }
}
