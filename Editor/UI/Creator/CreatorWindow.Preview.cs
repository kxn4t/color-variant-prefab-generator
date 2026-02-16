using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.ColorVariantGenerator
{
    /// <summary>
    /// Controls how prefab property overrides are handled when clearing the preview.
    /// </summary>
    internal enum PreviewRevertMode
    {
        /// <summary>Restore materials visually without touching prefab override state.</summary>
        VisualOnly = 0,
        /// <summary>Revert only overrides introduced by this tool (preserve user-made overrides).</summary>
        SelectiveRevert = 1,
        /// <summary>Revert all prefab overrides on affected renderers.</summary>
        FullRevert = 2
    }

    internal partial class CreatorWindow
    {
        // ────────────────────────────────────────────────
        // Scene Preview
        // ────────────────────────────────────────────────

        private Renderer FindRenderer(MaterialSlotIdentifier slot)
        {
            if (_baseInstance == null) return null;
            var target = string.IsNullOrEmpty(slot.rendererPath)
                ? _baseInstance.transform
                : _baseInstance.transform.Find(slot.rendererPath);
            return target != null ? target.GetComponent<Renderer>() : null;
        }

        private void ApplyPreviewMaterial(MaterialSlotIdentifier slot, Material material)
        {
            var renderer = FindRenderer(slot);
            if (renderer == null) return;

            // Group all preview changes under a single Undo entry
            if (!_previewActive)
            {
                Undo.SetCurrentGroupName("Color Variant Preview");
                _previewActive = true;
            }

            Undo.RecordObject(renderer, "Preview Material Change");

            var materials = renderer.sharedMaterials;
            if (slot.slotIndex >= 0 && slot.slotIndex < materials.Length)
            {
                // Apply override or revert to original
                if (material != null)
                {
                    materials[slot.slotIndex] = material;
                }
                else if (_originalMaterials.TryGetValue(slot, out var originalMat))
                {
                    materials[slot.slotIndex] = originalMat;
                }
                renderer.sharedMaterials = materials;
            }

            // Force Scene view repaint
            SceneView.RepaintAll();
        }

        private void ResetPreview()
        {
            if (!_previewActive || _baseInstance == null) return;

            // Restore materials visually
            foreach (var kvp in _originalMaterials)
            {
                var slot = kvp.Key;
                var originalMaterial = kvp.Value;

                var renderer = FindRenderer(slot);
                if (renderer == null) continue;

                var materials = renderer.sharedMaterials;
                if (slot.slotIndex >= 0 && slot.slotIndex < materials.Length)
                {
                    materials[slot.slotIndex] = originalMaterial;
                    renderer.sharedMaterials = materials;
                }
            }

            // Revert prefab overrides based on user setting
            if (PrefabUtility.IsPartOfPrefabInstance(_baseInstance))
            {
                var mode = (PreviewRevertMode)EditorPrefs.GetInt(PreviewRevertModeKey, 0);

                switch (mode)
                {
                    case PreviewRevertMode.SelectiveRevert:
                        RevertToolOverrides();
                        break;
                    case PreviewRevertMode.FullRevert:
                        RevertAllRendererOverrides();
                        break;
                }
            }

            _previewActive = false;
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Reverts only material overrides introduced by this tool, preserving
        /// overrides that existed before the tool was used (tracked by _preExistingOverrides).
        /// </summary>
        private void RevertToolOverrides()
        {
            foreach (var slot in _originalMaterials.Keys)
            {
                // Skip slots that already had overrides before this tool touched them
                if (_preExistingOverrides.Contains(slot)) continue;

                var renderer = FindRenderer(slot);
                if (renderer == null) continue;

                var so = new SerializedObject(renderer);
                var matProp = so.FindProperty($"m_Materials.Array.data[{slot.slotIndex}]");
                if (matProp != null && matProp.prefabOverride)
                {
                    PrefabUtility.RevertPropertyOverride(matProp, InteractionMode.AutomatedAction);
                }
            }
        }

        /// <summary>
        /// Reverts all prefab overrides on every renderer that was touched, regardless of origin.
        /// Uses a HashSet to avoid reverting the same Renderer component twice.
        /// </summary>
        private void RevertAllRendererOverrides()
        {
            var reverted = new HashSet<Renderer>();
            foreach (var slot in _originalMaterials.Keys)
            {
                var renderer = FindRenderer(slot);
                if (renderer == null || !reverted.Add(renderer)) continue;

                PrefabUtility.RevertObjectOverride(renderer, InteractionMode.AutomatedAction);
            }
        }

        private void OnClearOverrides()
        {
            ResetPreview();
            _overrides.Clear();
            _variantNameField.value = "";
            RefreshAllUI();
        }
    }
}
