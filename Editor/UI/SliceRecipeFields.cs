using System;
using System.IO;
using BetterSpriteEditor.DataAccess;
using BetterSpriteEditor.Operations.Slicing;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterSpriteEditor.UI
{
    internal sealed class SliceRecipeFields : VisualElement
    {
        internal event Action Changed;
        private readonly EnumField method, existing, alignment;
        private readonly Vector2IntField size, count, offset, padding;
        private readonly IntegerField minimum, threshold;
        private readonly Toggle skip, alternate;
        private readonly Vector2Field pivot;
        private readonly TextField template;
        private readonly Label fileStatus;

        internal SliceRecipeFields()
        {
            method = MakeEnum("Method", "slice-method", SliceMethod.GridByCellSize);
            size = MakePair("Cell size", "slice-cell-size", new Vector2Int(64, 64));
            count = MakePair("Columns / rows", "slice-cell-count", new Vector2Int(4, 4));
            offset = MakePair("Offset (top left)", "slice-offset", Vector2Int.zero);
            padding = MakePair("Padding", "slice-padding", Vector2Int.zero);
            minimum = MakeInteger("Minimum size", "slice-min-size", 2);
            alternate = MakeToggle("Alternate rows", "slice-alternate", false);
            skip = MakeToggle("Skip empty cells", "slice-skip-empty", true);
            threshold = MakeInteger("Alpha threshold (0–254)", "slice-alpha-threshold", 0);
            threshold.tooltip = "Pixels with alpha greater than this value count as content.";
            existing = MakeEnum("Existing regions", "slice-existing", SliceExistingPolicy.KeepExisting);
            alignment = MakeEnum("Pivot", "slice-alignment", SpriteAlignment.Center);
            pivot = new Vector2Field("Custom pivot") { name = "slice-pivot" };
            pivot.RegisterValueChangedCallback(_ => Notify());
            Add(pivot);
            template = new TextField("Names") { name = "slice-name-template", isDelayed = true };
            template.RegisterValueChangedCallback(_ => Notify());
            Add(template);
            Add(new Label("{texture}, {index}, {row}, {col}; e.g. {texture}_{row:00}_{col:00}. Grid numbers start at 0 before empty cells are skipped.")
                { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4, marginBottom = 4 } });
            var files = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            files.Add(new Button(SaveFile) { text = "Save settings…" });
            files.Add(new Button(LoadFile) { text = "Load settings…" });
            Add(files);
            fileStatus = new Label { style = { whiteSpace = WhiteSpace.Normal } };
            Add(fileStatus);
            Value = SliceRecipeStore.LoadLast(out var issue);
            fileStatus.text = issue;
        }

        internal SliceRecipe Value
        {
            get => new()
            {
                Method = (SliceMethod)method.value, Existing = (SliceExistingPolicy)existing.value,
                CellWidth = size.value.x, CellHeight = size.value.y, Columns = count.value.x, Rows = count.value.y,
                OffsetX = offset.value.x, OffsetY = offset.value.y, PaddingX = padding.value.x, PaddingY = padding.value.y,
                MinSize = minimum.value, AlphaThreshold = threshold.value, SkipEmpty = skip.value,
                AlternateRows = alternate.value, Alignment = (SpriteAlignment)alignment.value,
                Pivot = pivot.value, NameTemplate = template.value
            };
            set
            {
                method.SetValueWithoutNotify(value.Method);
                existing.SetValueWithoutNotify(value.Existing);
                size.SetValueWithoutNotify(new Vector2Int(value.CellWidth, value.CellHeight));
                count.SetValueWithoutNotify(new Vector2Int(value.Columns, value.Rows));
                offset.SetValueWithoutNotify(new Vector2Int(value.OffsetX, value.OffsetY));
                padding.SetValueWithoutNotify(new Vector2Int(value.PaddingX, value.PaddingY));
                minimum.SetValueWithoutNotify(value.MinSize);
                threshold.SetValueWithoutNotify(value.AlphaThreshold);
                skip.SetValueWithoutNotify(value.SkipEmpty);
                alternate.SetValueWithoutNotify(value.AlternateRows);
                alignment.SetValueWithoutNotify(value.Alignment);
                pivot.SetValueWithoutNotify(value.Pivot);
                template.SetValueWithoutNotify(value.NameTemplate);
                Notify();
            }
        }

        internal void Remember()
        {
            if (Value.Validate() != null) return;
            try { SliceRecipeStore.Save(SliceRecipeStore.LastPath, Value); fileStatus.text = string.Empty; }
            catch (Exception exception) { fileStatus.text = "Could not remember settings: " + exception.Message; }
        }

        private void Notify()
        {
            var kind = (SliceMethod)method.value;
            bool automatic = kind == SliceMethod.Automatic, iso = kind == SliceMethod.Isometric;
            size.style.display = kind == SliceMethod.GridByCellSize || iso ? DisplayStyle.Flex : DisplayStyle.None;
            count.style.display = kind == SliceMethod.GridByCellCount ? DisplayStyle.Flex : DisplayStyle.None;
            offset.style.display = !automatic ? DisplayStyle.Flex : DisplayStyle.None;
            padding.style.display = !automatic && !iso ? DisplayStyle.Flex : DisplayStyle.None;
            minimum.style.display = automatic ? DisplayStyle.Flex : DisplayStyle.None;
            alternate.style.display = iso ? DisplayStyle.Flex : DisplayStyle.None;
            skip.style.display = !automatic ? DisplayStyle.Flex : DisplayStyle.None;
            threshold.style.display = automatic || skip.value ? DisplayStyle.Flex : DisplayStyle.None;
            pivot.SetEnabled((SpriteAlignment)alignment.value == SpriteAlignment.Custom);
            Changed?.Invoke();
        }

        private void SaveFile()
        {
            string error = Value.Validate();
            if (error != null) { fileStatus.text = error; return; }
            string path = EditorUtility.SaveFilePanel("Save slicing settings", Path.GetDirectoryName(SliceRecipeStore.LastPath), "slicing-settings", "json");
            if (string.IsNullOrEmpty(path)) return;
            try { SliceRecipeStore.Save(path, Value); fileStatus.text = "Saved settings: " + path; }
            catch (Exception exception) { fileStatus.text = exception.Message; }
        }

        private void LoadFile()
        {
            string path = EditorUtility.OpenFilePanel("Load slicing settings", Path.GetDirectoryName(SliceRecipeStore.LastPath), "json");
            if (string.IsNullOrEmpty(path)) return;
            try { Value = SliceRecipeStore.Load(path); fileStatus.text = "Loaded settings: " + path; }
            catch (Exception exception) { fileStatus.text = exception.Message; }
        }

        private EnumField MakeEnum(string label, string id, Enum initial)
        {
            var field = new EnumField(label, initial) { name = id };
            field.RegisterValueChangedCallback(_ => Notify()); Add(field); return field;
        }
        private Vector2IntField MakePair(string label, string id, Vector2Int initial)
        {
            var field = new Vector2IntField(label) { name = id, value = initial };
            foreach (var child in field.Query<IntegerField>().ToList()) child.isDelayed = true;
            field.RegisterValueChangedCallback(_ => Notify()); Add(field); return field;
        }
        private IntegerField MakeInteger(string label, string id, int initial)
        {
            var field = new IntegerField(label) { name = id, value = initial, isDelayed = true };
            field.RegisterValueChangedCallback(_ => Notify()); Add(field); return field;
        }
        private Toggle MakeToggle(string label, string id, bool initial)
        {
            var field = new Toggle(label) { name = id, value = initial };
            field.RegisterValueChangedCallback(_ => Notify()); Add(field); return field;
        }
    }
}
