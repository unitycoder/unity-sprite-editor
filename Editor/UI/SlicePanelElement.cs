using System;
using System.Collections.Generic;
using BetterSpriteEditor.DataAccess;
using BetterSpriteEditor.Model;
using BetterSpriteEditor.Operations;
using BetterSpriteEditor.Operations.Slicing;
using BetterSpriteEditor.UI.Viewport;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterSpriteEditor.UI
{
    /// <summary>Preview only until Slice commits one undoable in-memory operation.</summary>
    public class SlicePanelElement : VisualElement
    {
        private SpriteDocument document;
        private HistoryStack history;
        private SelectionModel selection;
        private SpriteViewportElement viewport;
        private TextureAlphaMap alpha;
        private string alphaError;
        private SlicePlan plan;
        private readonly SliceRecipeFields settings;
        private readonly Label summary;
        private readonly ListView candidates;
        private readonly Button commit;
        private Func<SpriteDocument, TextureAlphaMap> loadAlpha = SpriteDocumentRepository.LoadAlphaMap;
        public event Action Closed;

        public SlicePanelElement()
        {
            style.position = Position.Absolute;
            style.top = style.left = 8;
            style.width = 360;
            style.maxHeight = Length.Percent(95);
            style.backgroundColor = new Color(0.22f, 0.22f, 0.22f, 0.98f);
            style.paddingLeft = style.paddingRight = 8;
            style.paddingTop = style.paddingBottom = 8;
            style.display = DisplayStyle.None;
            var body = new ScrollView { style = { flexShrink = 1 } };
            Add(body);
            body.Add(new Label("Slice") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            settings = new SliceRecipeFields();
            body.Add(settings);
            settings.Changed += () => { settings.Remember(); Recompute(); };
            summary = new Label { name = "slice-summary", style = { whiteSpace = WhiteSpace.Normal, marginTop = 5 } };
            body.Add(summary);
            candidates = MakeCandidateList();
            body.Add(candidates);
            body.Add(new Button(() => BatchSliceWindow.Open(settings.Value)) { text = "Batch slice Project selection…" });
            var footer = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd } };
            footer.Add(new Button(Cancel) { text = "Cancel", name = "slice-cancel" });
            commit = new Button(Commit) { text = "Slice", name = "slice-commit" };
            footer.Add(commit);
            Add(footer);
            RegisterCallback<DetachFromPanelEvent>(_ => Hide());
        }

        internal static ListView MakeCandidateList()
        {
            var list = new ListView { fixedItemHeight = 22, selectionType = SelectionType.None, style = { height = 132 } };
            list.makeItem = () => new Label();
            list.bindItem = (element, index) =>
            {
                var candidate = (SliceCandidate)list.itemsSource[index];
                string text = $"{candidate.Name} · {candidate.Rect} · {candidate.Status}";
                ((Label)element).text = text;
                element.tooltip = text;
            };
            return list;
        }

        public void Show(SpriteDocument newDocument, HistoryStack newHistory, SelectionModel newSelection, SpriteViewportElement newViewport)
        {
            Hide();
            document = newDocument; history = newHistory; selection = newSelection; viewport = newViewport;
            document.Changed += Recompute;
            style.display = DisplayStyle.Flex;
            Recompute();
        }

        public void Hide()
        {
            if (document != null) document.Changed -= Recompute;
            viewport?.SetPreviewRects(null);
            document = null; history = null; selection = null; viewport = null; alpha = null; alphaError = null; plan = null;
            candidates.itemsSource = null;
            style.display = DisplayStyle.None;
        }

        internal void SetAlphaProvider(Func<SpriteDocument, TextureAlphaMap> provider) => loadAlpha = provider;

        private void Recompute()
        {
            if (document == null) return;
            var recipe = settings.Value;
            if (recipe.Validate() == null && document.CanChangeRegions && recipe.NeedsAlpha && alpha == null && alphaError == null)
            {
                try { alpha = loadAlpha(document); }
                catch (Exception exception) { alphaError = exception.Message; }
            }
            plan = SlicePlanner.Build(document, alpha, recipe);
            if (recipe.NeedsAlpha && alphaError != null) plan.Error = "Pixel data unavailable: " + alphaError;
            summary.text = plan.Error ?? $"Add {plan.AddedCount}, remove {plan.RemovedCount}; {plan.EmptyCount} empty, {plan.OverlapCount} existing overlaps."
                + (recipe.Existing == SliceExistingPolicy.ReplaceAll ? " Replace All creates new sprite IDs and can break existing references." : " Existing regions keep their IDs and properties.");
            candidates.itemsSource = plan.Candidates;
            candidates.Rebuild();
            viewport?.SetPreviewRects(plan.Error == null ? plan.PreviewRects : null);
            commit.SetEnabled(plan.CanApply);
        }

        private void Commit()
        {
            Recompute();
            var command = plan?.CreateCommand(document);
            if (command == null) return;
            var targetHistory = history;
            var targetSelection = selection;
            Hide();
            targetHistory.Do(command);
            targetSelection.Clear();
            Closed?.Invoke();
        }

        private void Cancel() { Hide(); Closed?.Invoke(); }
    }
}
