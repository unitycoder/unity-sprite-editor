# Better Sprite Editor

An editor-only sprite metadata tool targeting Unity 2023.1 and newer, using the public 2D Sprite Data Provider API. It opens in its own UI Toolkit window.

## Features

- Multi-select of sprite regions with Unity-style multi-edit inspector.
- Create, move, resize, combine, split, rename, pivot, and border tools.
- Automatic, grid, and isometric slicing with preview, reusable settings, stable grid naming, and multi-texture batch processing.
- Outline and physics-shape editing, generation, and shared shapes.
- Undo/redo history (Ctrl+Z / Ctrl+Y), including after Apply.
- Searchable region list and reference search in loaded scenes or project assets.
- Customizable hotkeys for every action and gesture.
- Drag applied sprites into the scene, hierarchy, or sprite fields.

## Installation

In Package Manager, choose **Install package from Git URL** and enter `https://github.com/Suvitruf/unity-sprite-editor.git`.

For a local UPM installation, place the package folder outside the project's Assets folder, then use Package Manager's **Install package from disk** action and select its `package.json`. The package depends on `com.unity.2d.sprite`. An Assets-based development copy can be used directly; do not install a second copy through Package Manager in the same project.

## Getting started

Open a texture in the editor via one of:

- `Window > 2D > Better Sprite Editor` (uses the currently selected texture),
- right-click a texture in the Project window → `Open in Better Sprite Editor`,
- the texture importer inspector's context menu (⋮) → `Open in Better Sprite Editor`.

## Repeatable slicing

Open **Slice** (Shift+S) for a Sprite texture in Multiple mode. Choose Automatic, Grid by Cell Size, Grid by Cell Count, or Isometric. Settings update the orange preview and candidate list without changing the document. **Slice** creates one undo step; the main **Apply** saves it. **Cancel** discards the preview.

**Keep Existing** is the default: every existing region keeps its name, ID, pivot, border, and shapes; candidates overlapping any existing region are skipped. This adds new regions without updating or matching existing rectangles. **Replace All** explicitly removes all existing regions and creates new IDs, which can break references and discard associated skinning data. The panel reports add/remove counts. A result with no new regions leaves the sheet unchanged.

Names support `{texture}`, `{index}`, `{row}`, and `{col}`, with optional numeric padding such as `{index:000}`. For example, `{texture}_{row:00}_{col:00}` produces `cat_01_03` for row 1, column 3 of `cat.png`. Grid coordinates start at zero at the top left; indexes are assigned before empty or overlapping cells are skipped. Filling an empty cell therefore does not renumber later cells in the same grid. Changing the grid can change indexes; stable names do not preserve IDs when using Replace All. Automatic slicing supports `{texture}` and `{index}` only. Name collisions block the operation instead of silently renumbering sprites.

**Save settings…** and **Load settings…** use a versioned JSON file containing slicing parameters, naming, pivot, and the existing-region policy. They contain no texture identity, sprite IDs, fixed rectangles, or texture importer settings. The same settings are recomputed for each texture, including its own filename. Save the JSON in a project-owned location to share it. Last-used settings are remembered locally in `UserSettings/BetterSpriteEditor/last-slicing-settings.json` and shared by the single-texture and batch panels.

**Alpha threshold** counts pixels with alpha strictly greater than the selected value (0–254). Grid Skip Empty and Automatic require full-resolution pixels; if the provider returns reduced pixels, increase the texture import Max Size or disable Skip Empty for grids. Isometric emptiness uses the existing diamond sampling rule. Preview is limited to 50,000 candidate regions; Automatic additionally limits input to 16 million pixels per texture.

## Slicing multiple textures

1. Select textures or folders in Project and choose **Assets > Batch Slice in Better Sprite Editor**, or open **Window > 2D > Better Sprite Editor Batch Slice**. Folder selection includes textures recursively; duplicate selections are deduplicated.
2. Set the shared slicing parameters and click **Preview included**. Select a texture row to inspect its artwork, generated names, rectangles, skipped cells, and add/remove counts. Uncheck textures you want to exclude. Only writable assets under `Assets/` with region creation/deletion support in Sprite Multiple mode are accepted; the tool does not change importer mode automatically.
3. Resolve errors or uncheck affected textures, then click **Apply N reviewed textures**. This saves each changed texture once. Unchanged textures are not reimported. The tool checks texture/importer revisions, presentation order, settings, and regenerated results against the preview before each save. Unapplied importer edits, unsaved Better Sprite Editor edits, and pending recovery data block that texture.
4. **Cancel remaining** stops between textures. Completed saves remain; the batch is not one global Undo operation. Apply can continue the remaining reviewed textures in the same run. Previewing again starts a new run on the next Apply, retaining earlier backups. Closing the window or reloading assemblies stops the queue; preview again before starting a new run.
5. **Load last batch for restore** lists saved before-state records. Review the included rows and click **Restore N textures** to restore completed saves. Restore refuses textures edited since the batch, including changes to presentation order. Clear the list to return to slicing.

Before each save, the tool writes the original region data under `UserSettings/BetterSpriteEditor/batch-slicing/`. Completed records survive closing the window and assembly reloads. Older run folders remain available, but the UI loads only the latest run. If a save is interrupted, its Pending record is retained for inspection; the tool does not guess whether a partly completed write is safe to restore. Backups cover this editor's region IDs, names, rectangles, pivots, borders, custom data, outlines, physics shapes, and presentation order; they are not image, full importer, bone, or mesh-weight backups. Keep Existing is the appropriate mode for adding slices while retaining existing edited sprites.

## Keeping pivots anchored

Select a region and enable **Keep Pivot Position** under **Rect** in the inspector. Moving or resizing the rectangle then keeps its pivot at the same point in the texture. This applies to viewport handles, group movement, arrow-key nudging, and the X/Y/W/H fields, including multi-selection. Each selected region keeps its own anchor.

The option is off by default and remembered as an editor preference. A preset alignment changes to Custom only when needed to retain the anchor. Pivots may end up outside the new rectangle; their position is preserved without rounding to whole pixels. Undo/redo restores the rectangle and pivot together. The option is available only when both rectangle and pivot editing are supported, in Multiple mode.

## Shifting selected pivots

Under **Pivot**, enter X and Y in **Offset (px)**, then click **Shift Pivots**. The offset is added to each selected pivot's current position: X = 2 and Y = -1 moves every pivot two pixels right and one pixel down. Different region sizes and starting pivots retain their differences. Positive Y moves up; fractional offsets and pivots outside the rectangle are supported.

The offset always uses texture pixels, independent of Unit Mode and Keep Pivot Position. Changing the offset fields does not edit the sprites; each click applies the offset again as one undo step. Shifted presets become Custom, and undo restores their original alignment and value. Rectangles, borders, and outlines stay unchanged. The action works in Single and Multiple mode when pivot editing is supported. Use the main **Apply** action to save.

## Trimming and standardizing frame bounds

Select regions in Multiple mode and click **Bounds** in the toolbar. Choose **Trim Transparency** or **Uniform Frame Bounds**. Orange rectangles preview the proposed bounds; the panel lists each result and any conflict. **Change Bounds** updates the working document as one undo step. Use the main **Apply** button to save, or **Cancel** to discard the preview.

**Trim Transparency** finds pixels whose alpha is greater than the threshold (0–254) and retains the requested padding, capped at the original rectangle. A threshold of 0 includes every nontransparent pixel. Regions with no pixels above the threshold stay unchanged. Trimming preserves fractional and outside-frame pivot anchors, so the artwork keeps its position relative to the pivot.

**Uniform Frame Bounds** calculates the smallest common bounds around the existing pivots that contain every original rectangle. Padding adds space on each side. Disable **Automatic size** to request a larger width and height; extra space is split between opposite sides, with an odd extra pixel on the right/top. Every frame receives the same size and the same pivot offset within its rectangle. Compatible fractional pivot positions are required to keep rectangle edges on whole pixels. This operation retains existing frame content; use Trim first to remove unnecessary margins.

Existing outline and collider vertices stay at their original artwork positions, and nonzero 9-slice border lines remain fixed. The panel blocks the entire batch if a proposed rectangle would clip a shape or border line, extend beyond the texture, or include any nontransparent pixel outside the original region. Resolve the listed conflicts by changing padding, size, selection, pivots, or existing shapes/borders. Empty frames are retained when standardizing sizes.

Bounds editing requires rectangle and pivot editing support; preserving border lines or translated shapes also requires their corresponding editing support/providers. Full-resolution alpha data is required. If the provider returns downsampled pixels, increase the texture import Max Size and reload. Source image pixels are unchanged, and the tool cannot create padding beyond the texture edges.

## Copying properties and shapes

Select one source sprite and click **Copy Properties**. Select one or more destinations, optionally in another texture, and click **Paste Properties…**. The panel uses the copied snapshot, including unapplied source edits. Choose Pivot, Border, Outline, Physics Shape, and/or either tessellation value. Outline and physics source selectors also support transfers between the two shape types. The choices remain selected when reopening the panel during the current window session.

**Pivot coordinates** can copy the normalized alignment/value (retaining a preset), or the pixel offset from the source frame's bottom-left corner. Pixel copying converts a preset to Custom only when necessary. Fractional and outside-frame pivots are supported. Borders copy their pixel insets without scaling; overlapping borders are rejected.

**Shape coordinates** defaults to **Pixels relative to pivot**, which preserves geometry size and its pixel offsets from the pivot. **Pixels from bottom-left** preserves local pixel coordinates relative to the frame origin. **Scale to frame size** explicitly scales X and Y independently with the destination dimensions. When also pasting Pivot, shape alignment uses the resulting destination pivot. Pixel modes do not preserve world size when textures have different Pixels Per Unit; the panel reports this difference.

The viewport previews pivots in blue, borders in yellow, outlines in orange, and physics shapes in green. Outline interiors are tinted so you can inspect artwork outside the proposed render shape, especially when copying a collider into an outline. Dense contours use strokes without a fill. This is a geometry preview; it does not simulate the imported render mesh. Empty copied shapes clear the chosen destination and are identified in the panel. An unavailable source provider is reported separately from an empty shape.

Each target row reports its result. If a contour would extend beyond a destination frame, a border would not fit, or a selected property cannot be edited, pasting is blocked. Uncheck incompatible targets or change the chosen properties/mapping. **Paste Properties** applies every included change together as one Undo step; **Cancel** leaves the document untouched. Each destination owns independent geometry. Names, rectangles, sprite IDs, and importer-specific custom data are retained. Use the main **Apply** button to save.

The package's clipboard survives texture changes, window reopening, and assembly reloads in the same Unity session. It is separate from the operating system clipboard and clears when Unity exits. The versioned payload has an 8 MB size limit. Source images and importer platform settings are not part of the clipboard.

## Saving and recovery

Apply (Ctrl+S) writes sprite metadata and reimports the texture. Revert reloads the asset and discards unapplied edits and history. Closing or switching textures offers Save/Discard/Cancel; opening the same texture keeps the current session. A failed save keeps the document open.

If the source image or importer metadata changes externally, a clean document reloads automatically. A dirty document is retained and Apply refuses to overwrite the external version. Use Revert when you are ready to discard local edits and reload. Asset moves are tracked by GUID.

Recovery snapshots are written before assembly reload and every 30 seconds while dirty, under `Library/BetterSpriteEditor/`. They include selection and up to 100 undo and 100 redo commands. Damaged, legacy, or conflicting snapshots are renamed with a `.retained-…` suffix; the Console reports their location for inspection. Recovery is not a substitute for source control or backups.

Dragging rows changes this editor's presentation order. Apply saves that order under `UserSettings/BetterSpriteEditor/`, per project and user. It does not reorder Unity's sprite picker or importer array. Keep the package's UserSettings data if you want to retain personal ordering.

## Supported operations and limits

- Single-mode textures support pivot, border, and available shape edits. Rect editing, renaming, adding, deleting, splitting, combining, and slicing require Multiple mode. Change import mode in Unity's texture inspector and reload before using those tools.
- Custom importers must expose the corresponding public providers and editing capabilities.
- Grid-by-count uses whole-pixel cells and exactly the requested count when it fits. Remainder pixels are left at the right and bottom edges; impossible grids generate no cells.
- Shared shapes retain fractional and outside-frame pivots. The resulting polygon must fit every selected frame without clipping; otherwise generation reports the incompatible frame and leaves existing shapes untouched. Use larger aligned frame bounds, reduce Outset, or disable Shared Shape.
- Point Target is a simplification request, not a hard cap: disconnected paths and preserved geometry can require more points. Preview reports the actual result.
- Project usage search covers supported text assets under Assets. “No references found” applies only to the selected scope. Cancelled scans, unreadable/binary assets, and unapplied new sprites have unknown status. Package assets and dynamic loading are outside this scan.
- Multi-texture batch operations currently support slicing. Secondary-texture editing and animation preview/export are not implemented. Sprite IDs are retained for existing sprites; deleting/replacing a region can invalidate references to that region.

## Version compatibility

The package targets Unity 2023.1 and newer. Automated compatibility checks pass on Unity 2022.3.61f1 (38 tests), 2023.1.22f1 (38 tests), 6000.0.71f1 (38 tests), 6000.1.4f1 (46 tests), and 6000.3.11f1 (46 tests). Inspection of the Sprite package bundled with 2023.1.22f1 confirms that it exposes neither `SpriteRect.customData` nor the frame edit capability API, so the older API branch applies to 2023.1. Unity 2023.2 is not installed in the validation environment, so direct tests on that editor remain pending; manual viewport checks have been done only on Unity 6000.3. The 2022 run exercises the older API branch; it does not extend the declared support range.

Standard TextureImporter sprites use a compatibility fallback on older editors, with the same Single/Multiple mode restrictions. Native importer capabilities are respected on Unity 6000.1 and newer; unknown custom importers without a capability provider remain restricted. Custom sprite metadata is read and written through the public API on Unity 6000.0 and newer. Older branches reject nonempty custom metadata carried in a document instead of silently discarding it.

## Tests

Install Unity Test Framework and run the package's EditMode tests in Test Runner. For UPM installs, add `"com.suvitruf.better-sprite-editor"` to the host manifest's `testables` array. Importer tests create temporary assets and remove them afterwards. The tests also cover recovery, grid sizing, shared pivots, reference scans, and save conflict detection.
