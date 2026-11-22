using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterSpriteEditor.DataAccess;
using BetterSpriteEditor.Model;
using BetterSpriteEditor.Operations;
using BetterSpriteEditor.Operations.Slicing;
using BetterSpriteEditor.UI;
using BetterSpriteEditor.UI.Viewport;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace BetterSpriteEditor.Tests
{
    internal sealed class SlicingWorkflowTests
    {
        private SpriteDocument document;
        private SliceRecipe recipe;
        private Color32[] pixels;
        private string temporary;
        private byte[] previousSettings;
        private GameObject host;
        private PanelSettings panelSettings;

        [SetUp]
        public void SetUp()
        {
            document = new SpriteDocument("Assets/cat.png", null, 16, 16, 100, SpriteImportMode.Multiple)
                { AssetGuid = GUID.Generate().ToString(), BaselineRevision = "slicing-test" };
            recipe = new SliceRecipe { CellWidth = 8, CellHeight = 8, SkipEmpty = false };
            pixels = new Color32[256];
            temporary = Path.Combine(Path.GetTempPath(), "bse-slicing-" + GUID.Generate());
            previousSettings = File.Exists(SliceRecipeStore.LastPath) ? File.ReadAllBytes(SliceRecipeStore.LastPath) : null;
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null) Object.DestroyImmediate(host);
            if (panelSettings != null) Object.DestroyImmediate(panelSettings);
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
            if (previousSettings != null) File.WriteAllBytes(SliceRecipeStore.LastPath, previousSettings);
            else if (File.Exists(SliceRecipeStore.LastPath)) File.Delete(SliceRecipeStore.LastPath);
        }

        private TextureAlphaMap Alpha() => new(pixels, 16, 16, 16, 16);
        private SlicePlan Plan() => SlicePlanner.Build(document, recipe.NeedsAlpha ? Alpha() : null, recipe);
        private SpriteRegion Existing()
        {
            var region = new SpriteRegion
            {
                Id = GUID.Generate(), Name = "hand-named", Rect = new Rect(0, 8, 8, 8), Alignment = SpriteAlignment.Custom,
                Pivot = new Vector2(0.25f, 0.75f), Border = Vector4.one, CustomData = "metadata",
                Outlines = new List<Vector2[]> { new[] { new Vector2(-2, -2), new Vector2(2, -2), new Vector2(0, 2) } },
                PhysicsOutlines = new List<Vector2[]> { new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(0, 1) } },
                OutlineTessellation = 0.2f, PhysicsTessellation = 0.7f
            };
            document.Regions.Add(region);
            return region;
        }

        [Test]
        public void EmptyCells_KeepTheirIndexWhenArtworkIsAdded()
        {
            recipe.SkipEmpty = true;
            pixels[9 * 16 + 1].a = 255; // cell 0
            pixels[1 * 16 + 1].a = 255; // cell 2
            var before = Plan();
            Assert.That(before.Candidates.Where(c => c.Status == SliceCandidateStatus.Create).Select(c => c.Name), Is.EqualTo(new[] { "cat_0", "cat_2" }));
            pixels[9 * 16 + 9].a = 255; // fill formerly empty cell 1
            var after = Plan();
            Assert.That(after.Candidates[2].Name, Is.EqualTo(before.Candidates[2].Name));
            Assert.That(after.AddedCount, Is.EqualTo(3));
        }

        [Test]
        public void Names_UseEachTextureAndZeroBasedTopLeftCoordinates()
        {
            recipe.NameTemplate = "{texture}_{row:00}_{col:000}_{index:00}";
            Assert.That(Plan().Candidates[2].Name, Is.EqualTo("cat_01_000_02"));
            document.AssetPath = "Assets/dog.png";
            Assert.That(Plan().Candidates[2].Name, Is.EqualTo("dog_01_000_02"));
        }

        [Test]
        public void KeepExisting_PreservesAllMetadataAndUndoesOnlyNewRegions()
        {
            var existing = Existing();
            var before = existing.Clone();
            var plan = Plan();
            Assert.That(plan.AddedCount, Is.EqualTo(3));
            Assert.That(plan.OverlapCount, Is.EqualTo(1));
            Assert.That(document.IsDirty, Is.False);
            var history = new HistoryStack(document);
            history.Do(plan.CreateCommand(document));
            Assert.That(existing.ContentEquals(before), Is.True);
            Assert.That(document.Regions.Select(r => r.Name), Is.EqualTo(new[] { "hand-named", "cat_1", "cat_2", "cat_3" }));
            Assert.That(document.Regions.Select(r => r.Id).Distinct().Count(), Is.EqualTo(4));
            history.Undo();
            Assert.That(document.Regions.Count, Is.EqualTo(1));
            Assert.That(document.Regions[0].ContentEquals(before), Is.True);
            Assert.That(document.IsDirty, Is.False);
        }

        [Test]
        public void ReplaceAll_IsExplicitAndUndoRestoresIdentity()
        {
            var original = Existing().Clone();
            recipe.Existing = SliceExistingPolicy.ReplaceAll;
            var plan = Plan();
            Assert.That(plan.RemovedCount, Is.EqualTo(1));
            var history = new HistoryStack(document);
            history.Do(plan.CreateCommand(document));
            Assert.That(document.FindRegion(original.Id), Is.Null);
            Assert.That(document.Regions.Count, Is.EqualTo(4));
            history.Undo();
            Assert.That(document.Regions.Single().ContentEquals(original), Is.True);
            recipe.SkipEmpty = true;
            Assert.That(Plan().RemovedCount, Is.Zero);
            Assert.That(Plan().CreateCommand(document), Is.Null, "An empty result must not clear the sheet.");
        }

        [Test]
        public void CollisionsAndStalePlans_BlockWithoutRenumbering()
        {
            Existing().Name = "cat_1";
            Assert.That(Plan().Error, Does.Contain("Duplicate"));
            document.Regions.Clear();
            var plan = Plan();
            Existing();
            Assert.That(plan.CreateCommand(document), Is.Null);
            plan = Plan();
            document.Regions[0].Name = "changed";
            Assert.That(plan.CreateCommand(document), Is.Null);
            recipe.NameTemplate = "same";
            Assert.That(Plan().Error, Does.Contain("Duplicate"));
        }

        [TestCase("{unknown}")]
        [TestCase("{index:00")]
        [TestCase("{texture:00}")]
        [TestCase("")]
        public void InvalidTemplates_AreRejected(string template)
        {
            recipe.NameTemplate = template;
            Assert.That(recipe.Validate(), Is.Not.Null);
            Assert.That(Plan().CanApply, Is.False);
        }

        [Test]
        public void Automatic_ThresholdAndMinimumSizeControlConnectedComponents()
        {
            recipe.Method = SliceMethod.Automatic;
            recipe.MinSize = 1;
            recipe.AlphaThreshold = 100;
            pixels[0].a = 100;
            pixels[9 * 16 + 9].a = 101;
            var plan = Plan();
            Assert.That(plan.Candidates.Single().Rect, Is.EqualTo(new Rect(9, 9, 1, 1)));
            recipe.MinSize = 2;
            Assert.That(Plan().AddedCount, Is.Zero);
            recipe.NameTemplate = "{row}";
            Assert.That(recipe.Validate(), Does.Contain("no grid"));
        }

        [Test]
        public void Grid_ThresholdIsStrictAndMissingOrReducedPixelsBlock()
        {
            recipe.SkipEmpty = true; recipe.AlphaThreshold = 100;
            pixels[0].a = 100;
            Assert.That(Plan().AddedCount, Is.Zero);
            pixels[0].a = 101;
            Assert.That(Plan().AddedCount, Is.EqualTo(1));
            Assert.That(SlicePlanner.Build(document, null, recipe).CanApply, Is.False);
            var reduced = new TextureAlphaMap(new Color32[64], 8, 8, 16, 16);
            Assert.That(SlicePlanner.Build(document, reduced, recipe).Error, Does.Contain("Full-resolution"));
            recipe.SkipEmpty = false;
            Assert.That(SlicePlanner.Build(document, null, recipe).AddedCount, Is.EqualTo(4));
        }

        [Test]
        public void GridCountAndIsometric_KeepTheirCoordinateConventions()
        {
            recipe.Method = SliceMethod.GridByCellCount; recipe.Columns = 2; recipe.Rows = 2;
            recipe.OffsetX = 1; recipe.OffsetY = 2; recipe.PaddingX = 1; recipe.PaddingY = 2;
            var plan = Plan();
            Assert.That(plan.Candidates[3].Rect, Is.EqualTo(new Rect(9, 0, 7, 6)));
            Assert.That(plan.Candidates[3].Row, Is.EqualTo(1));
            recipe = new SliceRecipe { Method = SliceMethod.Isometric, CellWidth = 8, CellHeight = 8, SkipEmpty = false };
            plan = Plan();
            Assert.That(plan.Candidates.Count, Is.EqualTo(5));
            Assert.That(plan.Candidates[2].Rect, Is.EqualTo(new Rect(4, 4, 8, 8)));
            Assert.That(plan.Candidates[2].Row, Is.EqualTo(1));
            Assert.That(plan.Candidates[2].Column, Is.Zero);
        }

        [Test]
        public void InvalidOrUnsupportedRequests_DoNotProduceCommands()
        {
            recipe.CellWidth = 0;
            Assert.That(Plan().Error, Is.Not.Null);
            recipe.CellWidth = 8;
            var single = new SpriteDocument("Assets/single.png", null, 16, 16, 100, SpriteImportMode.Single);
            Assert.That(SlicePlanner.Build(single, null, recipe).CanApply, Is.False);
            var huge = new SpriteDocument("Assets/huge.png", null, 8192, 8192, 100, SpriteImportMode.Multiple);
            recipe.CellWidth = recipe.CellHeight = 1;
            Assert.That(SlicePlanner.Build(huge, null, recipe).Error, Does.Contain("limit"));
            recipe.Version = 2;
            Assert.That(recipe.Validate(), Does.Contain("version"));
        }

        [Test]
        public void Settings_RoundTripParametersWithoutTextureIdentityOrRegions()
        {
            recipe.NameTemplate = "{texture}_{row:00}_{col:00}";
            recipe.Pivot = new Vector2(0.1f, 0.9f); recipe.Alignment = SpriteAlignment.Custom;
            string path = Path.Combine(temporary, "recipe.json");
            SliceRecipeStore.Save(path, recipe);
            Assert.That(JsonUtility.ToJson(SliceRecipeStore.Load(path)), Is.EqualTo(JsonUtility.ToJson(recipe)));
            string text = File.ReadAllText(path);
            Assert.That(text, Does.Not.Contain("AssetGuid").And.Not.Contain("Regions").And.Not.Contain("cat.png"));
            File.WriteAllText(path, text.Replace("\"Version\": 1", "\"Version\": 999"));
            Assert.Throws<InvalidDataException>(() => SliceRecipeStore.Load(path));
        }

        [Test]
        public void Queue_CancellationKeepsCompletedWorkAndFailuresDoNotStopLaterAssets()
        {
            var items = Enumerable.Range(0, 3).Select(i => new BatchSliceItem { Guid = i.ToString() }).ToList();
            var processed = new List<string>();
            var queue = new BatchSliceQueue(items, item => { processed.Add(item.Guid); if (item.Guid == "0") throw new IOException("failure"); });
            queue.Step(); queue.Step(); queue.Cancel(); queue.Step();
            Assert.That(processed, Is.EqualTo(new[] { "0", "1" }));
            Assert.That(items[0].Status, Is.EqualTo(BatchSliceStatus.Error));
            Assert.That(items[2].Status, Is.EqualTo(BatchSliceStatus.NotReviewed));
        }

        [Test]
        public void Panel_PreviewsCancelsAndCommitsOneUndoStepWithLiveSettings()
        {
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            host = new GameObject("Slicing UI test", typeof(UIDocument));
            var ui = host.GetComponent<UIDocument>(); ui.panelSettings = panelSettings;
            var panel = new SlicePanelElement(); ui.rootVisualElement.Add(panel);
            panel.Q<SliceRecipeFields>().Value = recipe;
            var history = new HistoryStack(document);
            var selection = new SelectionModel();
            panel.Show(document, history, selection, new SpriteViewportElement());
            Assert.That(document.IsDirty, Is.False);
            Click(panel.Q<Button>("slice-cancel"));
            Assert.That(history.CanUndo, Is.False);
            panel.Show(document, history, selection, new SpriteViewportElement());
            panel.Q<TextField>("slice-name-template").value = "{texture}_{row}_{col}";
            Click(panel.Q<Button>("slice-commit"));
            Assert.That(history.Position, Is.EqualTo(1));
            Assert.That(document.Regions[3].Name, Is.EqualTo("cat_1_1"));
            history.Undo();
            Assert.That(document.Regions, Is.Empty);
            Assert.That(document.IsDirty, Is.False);
        }

        private static void Click(Button button)
        {
            using var evt = NavigationSubmitEvent.GetPooled(); evt.target = button; button.SendEvent(evt);
        }
    }
}
