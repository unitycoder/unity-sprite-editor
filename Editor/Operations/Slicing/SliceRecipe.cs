using System;
using UnityEditor;
using UnityEngine;

namespace BetterSpriteEditor.Operations.Slicing
{
    internal enum SliceMethod { Automatic, GridByCellSize, GridByCellCount, Isometric }
    internal enum SliceExistingPolicy { KeepExisting, ReplaceAll }

    [Serializable]
    internal sealed class SliceRecipe
    {
        public int Version = 1;
        public SliceMethod Method = SliceMethod.GridByCellSize;
        public int CellWidth = 64;
        public int CellHeight = 64;
        public int Columns = 4;
        public int Rows = 4;
        public int OffsetX;
        public int OffsetY;
        public int PaddingX;
        public int PaddingY;
        public bool SkipEmpty = true;
        public bool AlternateRows;
        public int MinSize = 2;
        public int AlphaThreshold;
        public SpriteAlignment Alignment = SpriteAlignment.Center;
        public Vector2 Pivot = new(0.5f, 0.5f);
        public SliceExistingPolicy Existing = SliceExistingPolicy.KeepExisting;
        public string NameTemplate = "{texture}_{index}";

        internal bool NeedsAlpha => Method == SliceMethod.Automatic || SkipEmpty;
        internal SliceRecipe Clone() => (SliceRecipe)MemberwiseClone();

        internal string Validate()
        {
            if (Version != 1) return "Unsupported slicing settings version.";
            if (!Enum.IsDefined(typeof(SliceMethod), Method) || !Enum.IsDefined(typeof(SliceExistingPolicy), Existing)
                || !Enum.IsDefined(typeof(SpriteAlignment), Alignment)) return "Invalid slicing method, policy, or pivot alignment.";
            if (CellWidth < 1 || CellHeight < 1 || Columns < 1 || Rows < 1 || MinSize < 1)
                return "Cell size, cell count, and minimum size must be positive.";
            if (OffsetX < 0 || OffsetY < 0 || PaddingX < 0 || PaddingY < 0)
                return "Offset and padding must be nonnegative.";
            if (AlphaThreshold < 0 || AlphaThreshold > 254) return "Alpha threshold must be between 0 and 254.";
            if (float.IsNaN(Pivot.x) || float.IsInfinity(Pivot.x) || float.IsNaN(Pivot.y) || float.IsInfinity(Pivot.y))
                return "The custom pivot must be finite.";
            return SlicePlanner.ValidateNameTemplate(NameTemplate, Method);
        }
    }
}
