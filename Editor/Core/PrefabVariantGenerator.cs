using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Kanameliser.ColorVariantGenerator
{
    /// <summary>
    /// Creates Prefab Variants with material overrides.
    /// </summary>
    internal static class PrefabVariantGenerator
    {
        /// <summary>
        /// Generates a single Prefab Variant from a base prefab with material overrides only.
        /// The Hierarchy preview instance is NOT used for saving - a fresh instance is created
        /// from the base prefab to ensure only material changes are stored as overrides.
        /// </summary>
        public static GenerationResult GenerateVariant(
            GameObject basePrefabAsset,
            List<MaterialOverride> overrides,
            string variantName,
            string outputPath,
            string namingTemplate)
        {
            var result = new GenerationResult { variantName = variantName };

            try
            {
                string validationError = ValidateInputs(basePrefabAsset, variantName, outputPath);
                if (validationError != null)
                {
                    result.errorMessage = validationError;
                    return result;
                }

                string fullPath = ComputeOutputPath(basePrefabAsset.name, variantName, outputPath, namingTemplate);
                EnsureDirectoryExists(fullPath);

                var instance = PrefabUtility.InstantiatePrefab(basePrefabAsset) as GameObject;
                if (instance == null)
                {
                    result.errorMessage = "Failed to instantiate base prefab.";
                    return result;
                }

                try
                {
                    int appliedCount = ApplyMaterialOverrides(instance, overrides);
                    SaveAsVariant(instance, fullPath, appliedCount, result);
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
            catch (Exception ex)
            {
                result.errorMessage = ex.Message;
                Debug.LogError($"[Color Variant Generator] Error generating variant '{variantName}': {ex}");
            }

            return result;
        }

        /// <summary>
        /// Generates a Prefab Variant preserving structural changes from the hierarchy instance (Standard mode).
        /// Added GameObjects are transferred wholesale with all components.
        /// Removed GameObjects are destroyed. Property modifications are filtered based on options.
        /// Material overrides are applied on top via the existing mechanism.
        /// </summary>
        public static GenerationResult GenerateStandardVariant(StandardGenerationRequest request)
        {
            var result = new GenerationResult { variantName = request.variantName };

            try
            {
                string validationError = ValidateInputs(request.basePrefabAsset, request.variantName, request.outputPath);
                if (validationError != null)
                {
                    result.errorMessage = validationError;
                    return result;
                }

                if (request.hierarchyInstance == null)
                {
                    result.errorMessage = "Hierarchy instance is null.";
                    return result;
                }

                string fullPath = ComputeOutputPath(
                    request.basePrefabAsset.name, request.variantName, request.outputPath, request.namingTemplate);
                EnsureDirectoryExists(fullPath);

                var options = request.options ?? new StandardModeOptions();
                var materialOverrides = request.materialOverrides ?? new List<MaterialOverride>();

                if (options.includePropertyChanges)
                {
                    // Native path saves a duplicate of the hierarchy instance, which preserves
                    // the instance's existing prefab connection. That makes "retarget the parent
                    // to an arbitrary ancestor" impossible on this path — the chosen ancestor
                    // would be silently ignored. Reject the request instead of producing a
                    // Variant whose parent does not match the user's selection.
                    var hierarchySource = PrefabUtility.GetCorrespondingObjectFromSource(request.hierarchyInstance);
                    if (request.basePrefabAsset != null && hierarchySource != null
                        && request.basePrefabAsset != hierarchySource)
                    {
                        result.errorMessage =
                            "Standard mode with 'Include Transform/component changes' cannot retarget the Variant parent to an ancestor. " +
                            "Either turn the option OFF, or set the Variant Parent dropdown back to the direct parent.";
                        return result;
                    }

                    // Native path: let Unity decide what is an override.
                    // Save a duplicate of the hierarchy instance, then re-open the saved variant
                    // through PrefabUtility's contents API to layer material overrides on top
                    // without disturbing the user's scene state.
                    GenerateStandardVariantNative(
                        request.hierarchyInstance, materialOverrides, fullPath, result);
                }
                else
                {
                    // Filtered path: GameObject add/remove plus GameObject-level property
                    // overrides on existing objects (m_Name / m_IsActive / m_Layer / m_TagString
                    // / m_StaticEditorFlags etc.) are transferred. Transform & Component property
                    // changes and component add/remove are intentionally excluded.
                    GenerateStandardVariantFiltered(
                        request.basePrefabAsset, request.hierarchyInstance,
                        materialOverrides, fullPath, result);
                }
            }
            catch (Exception ex)
            {
                result.errorMessage = ex.Message;
                Debug.LogError(
                    $"[Color Variant Generator] Error generating variant '{request.variantName}': {ex}");
            }

            return result;
        }

        /// <summary>
        /// Native Standard mode path: duplicates the hierarchy instance while preserving its
        /// prefab connection, saves the duplicate via Unity's SaveAsPrefabAsset (which produces
        /// a Variant of the same base Prefab), then layers material overrides on the saved
        /// Variant via the PrefabUtility contents API.
        /// The user's hierarchy instance is never passed to SaveAsPrefabAsset, because that
        /// call rebinds the source GameObject's prefab connection to the newly written asset
        /// and would silently mutate the user's scene state.
        /// </summary>
        private static void GenerateStandardVariantNative(
            GameObject hierarchyInstance,
            List<MaterialOverride> materialOverrides,
            string fullPath,
            GenerationResult result)
        {
            // Object.Instantiate on a prefab instance loses the prefab connection, so
            // SaveAsPrefabAsset on that clone would produce a plain Prefab instead of a Variant.
            // Use the same editor-internal path that Ctrl+D in the Hierarchy uses — it preserves
            // the prefab connection on the duplicate, so the saved result becomes a Variant of
            // the base. Selection is restored afterwards to avoid disturbing the user's state.
            var previousSelection = Selection.objects;
            Selection.activeGameObject = hierarchyInstance;
            Unsupported.DuplicateGameObjectsUsingPasteboard(); // TODO: より良い実装方法があれば変えたい
            var duplicate = Selection.activeGameObject;
            Selection.objects = previousSelection;

            if (duplicate == null || duplicate == hierarchyInstance)
            {
                result.errorMessage =
                    "Failed to duplicate the Hierarchy instance while preserving its prefab connection.";
                return;
            }

            try
            {
                PrefabUtility.SaveAsPrefabAsset(duplicate, fullPath, out bool firstSaveSuccess);
                if (!firstSaveSuccess)
                {
                    result.errorMessage = $"PrefabUtility.SaveAsPrefabAsset failed for '{fullPath}'.";
                    return;
                }

                var contents = PrefabUtility.LoadPrefabContents(fullPath);
                try
                {
                    int appliedCount = ApplyMaterialOverrides(contents, materialOverrides);
                    PrefabUtility.SaveAsPrefabAsset(contents, fullPath, out bool secondSaveSuccess);
                    result.success = secondSaveSuccess;
                    result.path = fullPath;
                    if (secondSaveSuccess)
                    {
                        Debug.Log(
                            $"[Color Variant Generator] Generated Prefab Variant: '{fullPath}' " +
                            $"({appliedCount} material overrides applied; native mode)");
                    }
                    else
                    {
                        result.errorMessage =
                            $"PrefabUtility.SaveAsPrefabAsset failed when applying material overrides for '{fullPath}'.";
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            finally
            {
                Object.DestroyImmediate(duplicate);
            }
        }

        /// <summary>
        /// Filtered Standard mode path: instantiates the base prefab fresh and selectively
        /// transfers GameObject add/remove plus GameObject-level property overrides on existing
        /// objects (m_Name, m_IsActive, m_Layer, m_TagString, m_StaticEditorFlags, etc.).
        /// Transform / Component property changes and component add/remove are excluded by design;
        /// material slot modifications are excluded here and re-applied via the material override system.
        /// </summary>
        private static void GenerateStandardVariantFiltered(
            GameObject basePrefabAsset,
            GameObject hierarchyInstance,
            List<MaterialOverride> materialOverrides,
            string fullPath,
            GenerationResult result)
        {
            var instance = PrefabUtility.InstantiatePrefab(basePrefabAsset) as GameObject;
            if (instance == null)
            {
                result.errorMessage = "Failed to instantiate base prefab.";
                return;
            }

            try
            {
                // Step 1: Apply removed GameObjects (destroy before copying to avoid conflicts)
                PrefabModificationHelper.ApplyRemovedGameObjects(hierarchyInstance, instance);

                // Step 2: Apply filtered property modifications. With includePropertyChanges = false
                // the helper passes through GameObject-level overrides (m_Name, m_IsActive, m_Layer,
                // m_TagString, m_StaticEditorFlags, etc.) and skips Transform/Component property changes.
                var filteredMods = PrefabModificationHelper.ExtractFilteredModifications(
                    hierarchyInstance, new StandardModeOptions { includePropertyChanges = false });
                PrefabModificationHelper.ApplyModifications(instance, filteredMods);

                // Step 3: Copy added GameObjects with all their state
                PrefabModificationHelper.CopyAddedGameObjects(hierarchyInstance, instance);

                // Step 4: Apply material overrides (same as Strict mode)
                int appliedCount = ApplyMaterialOverrides(instance, materialOverrides);
                SaveAsVariant(instance, fullPath, appliedCount, result);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Batch generates multiple Prefab Variants.
        /// </summary>
        public static List<GenerationResult> GenerateVariantsBatch(
            GameObject basePrefabAsset,
            List<(string variantName, List<MaterialOverride> overrides)> variants,
            string outputPath,
            string namingTemplate)
        {
            var results = new List<GenerationResult>();

            try
            {
                // Suspend asset DB imports during the loop for better performance
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < variants.Count; i++)
                {
                    var (variantName, overrides) = variants[i];

                    float progress = (float)i / variants.Count;
                    EditorUtility.DisplayProgressBar(
                        "Color Variant Generator",
                        $"Generating {variantName}... ({i + 1}/{variants.Count})",
                        progress);

                    var result = GenerateVariant(basePrefabAsset, overrides, variantName, outputPath, namingTemplate);
                    results.Add(result);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();

            // Log summary
            int successCount = 0;
            int failCount = 0;
            foreach (var r in results)
            {
                if (r.success) successCount++;
                else failCount++;
            }
            Debug.Log($"[Color Variant Generator] Batch generation complete: {successCount} succeeded, {failCount} failed");

            return results;
        }

        /// <summary>
        /// Validates common generation inputs. Returns an error message if invalid, null if valid.
        /// </summary>
        private static string ValidateInputs(GameObject basePrefabAsset, string variantName, string outputPath)
        {
            if (basePrefabAsset == null)
                return "Base prefab is null.";
            if (string.IsNullOrEmpty(variantName))
                return "Variant name is empty.";
            if (!EditorUIUtility.IsValidOutputPath(outputPath))
                return $"Output path must be inside the project's Assets folder: '{outputPath}'";
            return null;
        }

        /// <summary>
        /// Computes the full output file path for a variant.
        /// </summary>
        private static string ComputeOutputPath(string baseName, string variantName, string outputPath, string namingTemplate)
        {
            string variantFileName = EditorUIUtility.ResolveFileName(namingTemplate, baseName, variantName);
            return EditorUIUtility.NormalizePath(Path.Combine(outputPath, variantFileName + ".prefab"));
        }

        private static void EnsureDirectoryExists(string fullPath)
        {
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        /// <summary>
        /// Applies material overrides to a GameObject instance.
        /// Returns the number of successfully applied overrides.
        /// </summary>
        private static int ApplyMaterialOverrides(GameObject instance, List<MaterialOverride> overrides)
        {
            // Null is a caller bug — empty list is the valid "no overrides" case
            // (guarded by UI-layer confirmation dialogs / allowEmptyOverrides toggle).
            if (overrides == null)
                throw new ArgumentNullException(nameof(overrides));

            // Lazily-built map of base-asset Renderer ID → instance Renderer. Used to
            // resolve overrides whose source Hierarchy GameObject was renamed/reparented
            // after the override was captured (path lookup would silently fail).
            Dictionary<int, Renderer> instanceRendererBySource = null;

            int appliedCount = 0;
            foreach (var materialOverride in overrides)
            {
                if (materialOverride?.overrideMaterial == null) continue;
                if (materialOverride.slot == null) continue;

                var renderer = ResolveTargetRenderer(
                    instance, materialOverride.slot, ref instanceRendererBySource);

                if (renderer == null)
                {
                    Debug.LogWarning(
                        $"[Color Variant Generator] Renderer not found for slot '{materialOverride.slot.DisplayName}' (path '{materialOverride.slot.rendererPath}')");
                    continue;
                }

                var materials = renderer.sharedMaterials;
                if (materialOverride.slot.slotIndex >= 0 && materialOverride.slot.slotIndex < materials.Length)
                {
                    materials[materialOverride.slot.slotIndex] = materialOverride.overrideMaterial;
                    renderer.sharedMaterials = materials;
                    appliedCount++;
                }
                else
                {
                    Debug.LogWarning(
                        $"[Color Variant Generator] Slot index {materialOverride.slot.slotIndex} out of range for '{materialOverride.slot.rendererPath}' (has {materials.Length} slots)");
                }
            }

            return appliedCount;
        }

        /// <summary>
        /// Finds the Renderer on <paramref name="instance"/> that corresponds to the slot.
        /// Prefers Renderer-correspondence (rename-safe) when the slot carries a live
        /// Renderer reference; falls back to path-based lookup otherwise.
        /// </summary>
        private static Renderer ResolveTargetRenderer(
            GameObject instance,
            MaterialSlotIdentifier slot,
            ref Dictionary<int, Renderer> instanceRendererBySource)
        {
            if (slot.renderer != null)
            {
                instanceRendererBySource ??= BuildInstanceRendererBySourceMap(instance);
                var sourceId = ResolveTopmostSourceInstanceId(slot.renderer);
                if (sourceId != 0 && instanceRendererBySource.TryGetValue(sourceId, out var matched))
                    return matched;
            }

            Transform target = string.IsNullOrEmpty(slot.rendererPath)
                ? instance.transform
                : instance.transform.Find(slot.rendererPath);
            return target != null ? target.GetComponent<Renderer>() : null;
        }

        /// <summary>
        /// Builds a map from each instance Renderer's topmost source asset (by instance ID)
        /// to the live Renderer on the fresh instance. Walking the variant chain to the
        /// top makes lookup robust to multi-level Variant hierarchies.
        /// </summary>
        private static Dictionary<int, Renderer> BuildInstanceRendererBySourceMap(GameObject instance)
        {
            var map = new Dictionary<int, Renderer>();
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                int sourceId = ResolveTopmostSourceInstanceId(r);
                if (sourceId != 0 && !map.ContainsKey(sourceId))
                    map[sourceId] = r;
            }
            return map;
        }

        /// <summary>
        /// Walks <see cref="PrefabUtility.GetCorrespondingObjectFromSource"/> to the
        /// topmost source asset and returns its instance ID. Returns the input's
        /// instance ID when it has no source (already an asset).
        /// </summary>
        private static int ResolveTopmostSourceInstanceId(Object obj)
        {
            if (obj == null) return 0;
            Object current = obj;
            while (true)
            {
                var next = PrefabUtility.GetCorrespondingObjectFromSource(current);
                if (next == null) return current.GetInstanceID();
                current = next;
            }
        }

        /// <summary>
        /// Saves a GameObject instance as a Prefab Variant and populates the result.
        /// </summary>
        private static void SaveAsVariant(
            GameObject instance, string fullPath, int appliedCount, GenerationResult result)
        {
            bool success;
            PrefabUtility.SaveAsPrefabAsset(instance, fullPath, out success);

            result.success = success;
            result.path = fullPath;

            if (success)
            {
                Debug.Log($"[Color Variant Generator] Generated Prefab Variant: '{fullPath}' ({appliedCount} material overrides applied)");
            }
            else
            {
                result.errorMessage = $"PrefabUtility.SaveAsPrefabAsset failed for '{fullPath}'.";
            }
        }
    }
}
