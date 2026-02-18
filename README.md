# Color Variant Prefab Generator

[日本語](README.ja.md)

A Unity Editor extension that streamlines material assignment to Prefabs and efficiently creates color variation **Prefab Variants**.  
With a thumbnail-equipped material browser and scene preview, setting up materials on models is also made easy. It can be used in any situation where you need color variant Prefabs — from VRChat avatar outfits to accessories, world assets, and more.

Have you been duplicating Prefabs for each color and saving them as Original Prefabs without really thinking about it?  
With the full-duplication approach, every time the base is modified you must manually apply the same changes to all color variants. And when deploying outfits to multiple avatars, you need to recreate color variant Prefabs from scratch for each avatar, causing your workload to balloon.

This tool solves these problems by automatically generating **Prefab Variants** that only store material differences.

## Features

- **CV Creator** — Create color variants one at a time while viewing a scene preview; bulk-replace materials across all slots sharing the same material
- **Batch Generator** — Bulk-deploy existing color variants to a different base Prefab
- **Material Browser** — Browse materials in a folder with thumbnails; assign via drag-and-drop
- **Real-time Preview** — Material changes are instantly reflected in the Scene view (with Undo/Redo support)
- **Smart Matching** — Automatically matches material slots even between Prefabs with different hierarchy structures
- **Import from Existing Prefab** — Import material configurations from manually created color variants

Changes to the base Prefab are automatically propagated to all Variants, making color variant maintenance significantly easier.

## Requirements

- Unity 2022.3.22f1 or later
- Optional: NDMF (Non-Destructive Modular Framework) 1.11.0+ — Used for Japanese UI display (English UI is used when NDMF is not present)

## Installation

### Via VRChat Creator Companion (Recommended)

1. Visit [https://kxn4t.github.io/vpm-repos/](https://kxn4t.github.io/vpm-repos/)
2. Click the "Add to VCC" button to add the "Kanameliser VPM Packages" repository to VCC or ALCOM
3. Add "Color Variant Prefab Generator" to your project from the Manage Project package list

### Manual Installation

1. Download the latest release from [GitHub Releases](https://github.com/kxn4t/color-variant-prefab-generator/releases)
2. Import the package into your Unity project

## Usage

Both tools are accessible from the menu bar: **Tools > Color Variant Prefab Generator**.

### Expected Workflow (Example: Deploying outfits to multiple avatars)

```
1. First, build out color variants on one avatar (CV Creator)
   Starting from a base Prefab (e.g., Shinano_HonmeiKnit),
   create Prefab Variants for each color: Black, Blue, Brown, etc.
   Finalize the material setup while checking the scene preview.

2. Bulk-deploy the completed color variants to another avatar (Batch Generator)
   Specify a base Prefab with MA, PB, and other setup already completed for the new avatar (e.g., Airi_HonmeiKnit),
   add the color variant Prefabs from step 1 as sources in bulk.
   Material differences are auto-detected and Prefab Variants for all colors are generated at once.
```

The intended flow is to carefully build out the material configuration on the first avatar, then use it as a template to deploy to other base Prefabs. Of course, using CV Creator alone to create color variants is also perfectly useful.

### CV Creator

Create color variants one at a time with care, using the material browser and scene preview.  
It is also convenient for the task of assigning materials to models.

1. Place the base Prefab in the Scene Hierarchy
2. Open **Tools > Color Variant Prefab Generator > Creator**
3. Drag the Hierarchy instance into the **Base Prefab** field
4. The left panel lists material slots for each Renderer — switch to **Bulk** mode via the toggle to group by material and replace across all matching slots at once
5. Set a **Material Folder** in the right panel to browse materials
6. Swap materials via drag-and-drop or the picker — changes are instantly reflected in the Scene view
7. Enter a **Variant Name** (e.g., "Black"), set the output path, and click **Generate Prefab Variant**
8. After generation, a dialog appears to choose the next action:
   - **Keep Current Overrides** — Keep the current settings and change only some materials to create another color
   - **Clear Overrides** — Clear overrides and variant name to start creating the next color

> **Tip:** Drag a **Hierarchy instance** into the Base Prefab field (not an asset from the Project window). Scene preview requires an instance in the scene.

> **Variant Parent:** When the base Prefab is a nested Variant (e.g., Base → Black), a **Variant Parent** dropdown appears in Output Settings. You can choose which ancestor to use as the parent for the generated Variant. This is useful for creating sibling Variants (e.g., making "Red" a direct child of "Base" instead of "Black").

> **Import from Prefab:** You can also load material configurations from an existing Prefab via the options menu (▼) on the Base Prefab field. Material differences are automatically detected and populated into the override slots.

> **Folder Assignment:** You can also assign Output Path and Material Folder by dragging and dropping a folder from the Project window.

#### Clear Overrides and Revert Modes

To reset material changes, use the **Clear Overrides** button. The dropdown (▾) offers three revert modes:

| Mode | Behavior |
|---|---|
| **Visual Only** | Restores appearance only. Prefab override marks remain |
| **Selective Revert** | Reverts only overrides added by this tool. Pre-existing overrides are preserved |
| **Full Revert** | Reverts all overrides on the target Renderers |

> **Note:** Closing the window does not automatically reset material changes made during preview. Use **Ctrl+Z** (Undo) to revert.

### Batch Generator

Generate Variants for a different base Prefab in bulk, using existing color variant Prefabs as sources. For example, you can bulk-deploy color variants created for one avatar to another avatar. Both Prefab Variants and regular Prefabs can be used as sources.

1. Open **Tools > Color Variant Prefab Generator > Batch Generator**
2. Set the **New Base Prefab** — the Prefab you want to generate Variants for
3. Add source Prefabs (existing color variants) to the **Color Variants** list — you can drag-and-drop multiple Prefabs at once
4. Variant names are assigned automatically — derived from the difference with the parent Prefab name for Prefab Variants, or from the file name for regular Prefabs. You can manually edit them with the pencil button (✎)
5. Each source is automatically compared against the base, and material differences are detected
6. Review the **Matching Results** — unmatched slots are highlighted with warnings and can be manually reassigned via dropdown
7. Set the output path and naming template
8. (Optional) Enable **"Create variants without material differences"** to generate Variants even when the source Prefab and base have identical materials — useful for managing the base Prefab in a separate folder
9. Click **Generate All Variants**

> **Tip:** If files with the same name already exist at the output destination, a confirmation dialog will appear asking whether to overwrite all or cancel. You can also assign the Output Path by dragging and dropping a folder.

### Naming Template

Both tools support a customizable naming template with the following placeholders:

| Placeholder | Description |
|---|---|
| `{BaseName}` | The base Prefab's file name (without extension; trailing `_Base` is automatically removed) |
| `{VariantName}` | The variant name you specify |

Default template: `{BaseName}_{VariantName}`

Example: Base `Airi_HonmeiKnit` + Variant `Black` → `Airi_HonmeiKnit_Black.prefab`

> **Tip:** In the Batch Generator, if multiple variants share the same name, a `_1`, `_2`, `_3` … suffix is automatically appended.  
> Example: `Black`, `Black`, `Black` → `Airi_HonmeiKnit_Black_1.prefab`, `Airi_HonmeiKnit_Black_2.prefab`, `Airi_HonmeiKnit_Black_3.prefab`

## How It Works

### Renderer Matching Algorithm

When comparing two Prefabs, the tool uses a 4-tier priority system to match material slots:

| Priority | Strategy | Description |
|---|---|---|
| P1 | Exact path | Same hierarchy path, object name, and slot index |
| P2 | Same depth | Same hierarchy depth, object name, and slot index |
| P3 | Name match | Same object name and slot index at any depth |
| P4 | Fuzzy match | Case-insensitive name match with slot index |

When multiple candidates exist at the same priority, tiebreakers are applied in order: base material name match, closest hierarchy depth, then path similarity (Levenshtein distance).

### What Gets Stored

Generated Prefab Variants **only** contain material overrides. No transforms, component changes, or other properties are stored. This means:

- Base Prefab changes (bone adjustments, component settings, etc.) are automatically propagated to all Variants
- No risk of unintended property drift between Variants

## License

[MIT](LICENSE) — Copyright (c) 2026 @kxn4t
