using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BetterSpriteEditor.Model;
using UnityEditor;
using UnityEngine;

namespace BetterSpriteEditor.Operations.Slicing
{
    internal enum SliceCandidateStatus { Create, Empty, ExistingOverlap }

    [Serializable]
    internal sealed class SliceCandidate
    {
        public Rect Rect;
        public int Index;
        public int Row;
        public int Column;
        public string Name;
        public SliceCandidateStatus Status;
    }

    internal sealed class SlicePlan
    {
        internal readonly List<SliceCandidate> Candidates = new();
        internal readonly List<SpriteRegion> Before = new();
        internal SliceRecipe Recipe;
        internal string Error;
        internal int AddedCount => Candidates.FindAll(c => c.Status == SliceCandidateStatus.Create).Count;
        internal int EmptyCount => Candidates.FindAll(c => c.Status == SliceCandidateStatus.Empty).Count;
        internal int OverlapCount => Candidates.FindAll(c => c.Status == SliceCandidateStatus.ExistingOverlap).Count;
        internal int RemovedCount => Recipe.Existing == SliceExistingPolicy.ReplaceAll && AddedCount > 0 ? Before.Count : 0;
        internal bool CanApply => Error == null && AddedCount > 0;
        internal List<Rect> PreviewRects => Candidates.FindAll(c => c.Status == SliceCandidateStatus.Create).ConvertAll(c => c.Rect);

        internal IEditorCommand CreateCommand(SpriteDocument document)
        {
            if (!CanApply || !document.CanChangeRegions || document.Regions.Count != Before.Count) return null;
            for (int i = 0; i < Before.Count; i++)
                if (!Before[i].ContentEquals(document.Regions[i])) return null;
            var commands = new List<IEditorCommand>();
            if (RemovedCount > 0) commands.Add(new DeleteRegionsCommand(document, document.Regions));
            foreach (var candidate in Candidates)
            {
                if (candidate.Status != SliceCandidateStatus.Create) continue;
                commands.Add(new CreateRegionCommand(document, new SpriteRegion
                {
                    Id = GUID.Generate(), Name = candidate.Name, Rect = candidate.Rect,
                    Alignment = Recipe.Alignment, Pivot = Recipe.Pivot, Border = Vector4.zero, CustomData = string.Empty
                }));
            }
            return new CompositeCommand($"Slice: add {AddedCount}, remove {RemovedCount}", commands);
        }

        internal string Fingerprint()
        {
            var builder = new StringBuilder(JsonUtility.ToJson(Recipe));
            // Presentation order lives outside the importer's revision hash.
            foreach (var region in Before) builder.Append('\n').Append(region.Id);
            foreach (var candidate in Candidates) builder.Append('\n').Append(JsonUtility.ToJson(candidate));
            using var hash = SHA256.Create();
            return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
        }
    }

    internal static class SlicePlanner
    {
        internal const int MaximumCandidates = 50000;
        private static readonly Regex token = new(@"\{(texture|index|row|col)(?::(0{1,8}))?\}", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        internal static string ValidateNameTemplate(string template, SliceMethod method)
        {
            if (string.IsNullOrWhiteSpace(template) || template.Length > 256) return "Use a nonempty name template of at most 256 characters.";
            var remaining = token.Replace(template, match => string.Empty);
            if (remaining.IndexOfAny(new[] { '{', '}', '\r', '\n', '\t' }) >= 0)
                return "Name tokens: {texture}, {index}, {row}, {col}; numbers may use zero padding, e.g. {index:000}.";
            foreach (Match match in token.Matches(template))
            {
                if (match.Groups[1].Value == "texture" && match.Groups[2].Success) return "Zero padding applies only to numeric tokens.";
                if (method == SliceMethod.Automatic && (match.Groups[1].Value is "row" or "col"))
                    return "Automatic slicing has no grid coordinates; use {index} or {texture}.";
            }
            return null;
        }

        internal static SlicePlan Build(SpriteDocument document, TextureAlphaMap alpha, SliceRecipe recipe)
        {
            var plan = new SlicePlan { Recipe = recipe.Clone(), Error = recipe.Validate() };
            if (plan.Error != null) return plan;
            if (document == null || !document.CanChangeRegions)
                return Fail(plan, "Slicing requires a Sprite texture in Multiple mode with region creation/deletion support.");
            if (recipe.NeedsAlpha && (alpha == null || alpha.ActualWidth != document.TextureWidth || alpha.ActualHeight != document.TextureHeight))
                return Fail(plan, "Matching texture pixels are required. Reload the preview.");
            if (recipe.NeedsAlpha && (alpha.Width != alpha.ActualWidth || alpha.Height != alpha.ActualHeight))
                return Fail(plan, "Full-resolution pixels are required. Increase Max Size, or disable Skip Empty for grid slicing.");
            if (document.TextureWidth <= 0 || document.TextureHeight <= 0) return Fail(plan, "Invalid texture dimensions.");
            plan.Before.AddRange(document.Regions.ConvertAll(r => r.Clone()));
            List<Rect> rects;
            try { rects = Generate(document, alpha, recipe); }
            catch (InvalidOperationException exception) { return Fail(plan, exception.Message); }
            string textureName = Path.GetFileNameWithoutExtension(document.AssetPath);
            if (string.IsNullOrEmpty(textureName)) textureName = document.Texture != null ? document.Texture.name : "sprite";
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (recipe.Existing == SliceExistingPolicy.KeepExisting)
                foreach (var region in document.Regions) names.Add(region.Name);
            for (int index = 0; index < rects.Count; index++)
            {
                var rect = rects[index];
                var candidate = new SliceCandidate { Rect = rect, Index = index };
                if (recipe.Method != SliceMethod.Automatic)
                {
                    float stepY = recipe.Method == SliceMethod.Isometric ? rect.height * 0.5f : rect.height + recipe.PaddingY;
                    candidate.Row = Mathf.RoundToInt((document.TextureHeight - recipe.OffsetY - rect.yMax) / stepY);
                    bool shifted = recipe.Method == SliceMethod.Isometric && (recipe.AlternateRows ^ ((candidate.Row & 1) != 0));
                    float stepX = rect.width + (recipe.Method == SliceMethod.Isometric ? 0 : recipe.PaddingX);
                    candidate.Column = Mathf.RoundToInt((rect.xMin - recipe.OffsetX - (shifted ? rect.width * 0.5f : 0)) / stepX);
                }
                candidate.Name = token.Replace(recipe.NameTemplate, match =>
                {
                    string key = match.Groups[1].Value;
                    if (key == "texture") return textureName;
                    int number = key == "index" ? candidate.Index : key == "row" ? candidate.Row : candidate.Column;
                    return number.ToString(match.Groups[2].Success ? match.Groups[2].Value : "0", CultureInfo.InvariantCulture);
                });
                if (recipe.Method != SliceMethod.Automatic && recipe.SkipEmpty
                    && !(recipe.Method == SliceMethod.Isometric
                        ? IsometricSlicer.DiamondHasContent(rect, new IsometricSliceSettings { CellWidth = recipe.CellWidth, CellHeight = recipe.CellHeight },
                            rect.width / rect.height, alpha, (byte)recipe.AlphaThreshold)
                        : alpha.AnyOpaqueInActualRect(rect, (byte)recipe.AlphaThreshold)))
                    candidate.Status = SliceCandidateStatus.Empty;
                else if (recipe.Existing == SliceExistingPolicy.KeepExisting && document.Regions.Exists(r => r.Rect.Overlaps(rect)))
                    candidate.Status = SliceCandidateStatus.ExistingOverlap;
                else if (!names.Add(candidate.Name))
                    plan.Error = $"Duplicate sprite name '{candidate.Name}'. Change the template or keep the overlapping existing region.";
                plan.Candidates.Add(candidate);
            }
            return plan;
        }

        private static List<Rect> Generate(SpriteDocument document, TextureAlphaMap alpha, SliceRecipe recipe)
        {
            if (recipe.Method == SliceMethod.Automatic)
            {
                if ((long)alpha.Width * alpha.Height > 16777216)
                    throw new InvalidOperationException("Automatic slicing is limited to 16M pixels per texture. Use a grid for larger sheets.");
                return AutoSlicer.Slice(alpha, recipe.MinSize, recipe.MinSize, (byte)recipe.AlphaThreshold, MaximumCandidates);
            }
            long availableWidth = (long)document.TextureWidth - recipe.OffsetX;
            long availableHeight = (long)document.TextureHeight - recipe.OffsetY;
            long width = recipe.CellWidth, height = recipe.CellHeight;
            bool count = recipe.Method == SliceMethod.GridByCellCount;
            if (count)
            {
                width = (availableWidth - (long)recipe.PaddingX * (recipe.Columns - 1)) / recipe.Columns;
                height = (availableHeight - (long)recipe.PaddingY * (recipe.Rows - 1)) / recipe.Rows;
            }
            if (width < 1 || height < 1 || availableWidth < width || availableHeight < height) return new List<Rect>();
            bool iso = recipe.Method == SliceMethod.Isometric;
            long columns = iso ? availableWidth / width : (availableWidth + recipe.PaddingX) / (width + recipe.PaddingX);
            long rows = iso ? (availableHeight - height) * 2 / height + 1 : (availableHeight + recipe.PaddingY) / (height + recipe.PaddingY);
            if (count) { columns = Math.Min(columns, recipe.Columns); rows = Math.Min(rows, recipe.Rows); }
            if (columns * rows > MaximumCandidates)
                throw new InvalidOperationException($"This grid exceeds the {MaximumCandidates:N0} cell preview limit. Increase the cell size.");
            if (iso)
                return IsometricSlicer.Slice(new IsometricSliceSettings
                {
                    CellWidth = recipe.CellWidth, CellHeight = recipe.CellHeight, OffsetX = recipe.OffsetX, OffsetY = recipe.OffsetY,
                    IsAlternate = recipe.AlternateRows, SkipEmptyCells = false
                }, document.TextureWidth, document.TextureHeight, null);
            return GridSlicer.Slice(new GridSliceSettings
            {
                ByCellCount = count, CellWidth = recipe.CellWidth, CellHeight = recipe.CellHeight, Columns = recipe.Columns, Rows = recipe.Rows,
                OffsetX = recipe.OffsetX, OffsetY = recipe.OffsetY, PaddingX = recipe.PaddingX, PaddingY = recipe.PaddingY, SkipEmptyCells = false
            }, document.TextureWidth, document.TextureHeight, null);
        }

        private static SlicePlan Fail(SlicePlan plan, string error) { plan.Error = error; return plan; }
    }
}
