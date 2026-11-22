using System;
using System.IO;
using BetterSpriteEditor.Operations.Slicing;
using UnityEngine;

namespace BetterSpriteEditor.DataAccess
{
    internal static class SliceRecipeStore
    {
        internal const string LastPath = "UserSettings/BetterSpriteEditor/last-slicing-settings.json";

        internal static SliceRecipe Load(string path)
        {
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Slicing settings exceed the 64 KB limit.");
            var recipe = JsonUtility.FromJson<SliceRecipe>(File.ReadAllText(path));
            if (recipe == null) throw new InvalidDataException("The file does not contain slicing settings.");
            string issue = recipe.Validate();
            if (issue != null) throw new InvalidDataException(issue);
            return recipe;
        }

        internal static void Save(string path, SliceRecipe recipe)
        {
            string issue = recipe.Validate();
            if (issue != null) throw new InvalidDataException(issue);
            AssetState.WriteAtomic(Path.GetFullPath(path), JsonUtility.ToJson(recipe, true));
        }

        internal static SliceRecipe LoadLast(out string issue)
        {
            issue = null;
            if (!File.Exists(LastPath)) return new SliceRecipe();
            try { return Load(LastPath); }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is UnauthorizedAccessException)
            {
                issue = $"Could not load last slicing settings: {exception.Message}";
                return new SliceRecipe();
            }
        }
    }
}
