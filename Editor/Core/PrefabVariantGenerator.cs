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

                var instance = PrefabUtility.InstantiatePrefab(request.basePrefabAsset) as GameObject;
                if (instance == null)
                {
                    result.errorMessage = "Failed to instantiate base prefab.";
                    return result;
                }

                try
                {
                    // Step 1: Apply removed GameObjects (destroy before copying to avoid conflicts)
                    PrefabModificationHelper.ApplyRemovedGameObjects(request.hierarchyInstance, instance);

                    // Step 2: Apply filtered property modifications on existing objects first
                    // (renames, active state, optional Transform/component changes).
                    // This must happen BEFORE CopyAddedGameObjects, because SetPropertyModifications
                    // replaces the entire modification list and would discard added objects.
                    var options = request.options ?? new StandardModeOptions();
                    var filteredMods = PrefabModificationHelper.ExtractFilteredModifications(
                        request.hierarchyInstance, options);
                    PrefabModificationHelper.ApplyModifications(request.hierarchyInstance, instance, filteredMods);

                    // Step 3: Copy added GameObjects with all their state
                    PrefabModificationHelper.CopyAddedGameObjects(request.hierarchyInstance, instance);

                    // Step 4: Apply material overrides (same as Strict mode)
                    int appliedCount = ApplyMaterialOverrides(
                        instance, request.materialOverrides ?? new List<MaterialOverride>());
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
                Debug.LogError(
                    $"[Color Variant Generator] Error generating variant '{request.variantName}': {ex}");
            }

            return result;
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

            int appliedCount = 0;
            foreach (var materialOverride in overrides)
            {
                if (materialOverride?.overrideMaterial == null) continue;
                if (materialOverride.slot == null) continue;

                Transform target = string.IsNullOrEmpty(materialOverride.slot.rendererPath)
                    ? instance.transform
                    : instance.transform.Find(materialOverride.slot.rendererPath);

                if (target == null)
                {
                    Debug.LogWarning($"[Color Variant Generator] Renderer path not found: '{materialOverride.slot.rendererPath}'");
                    continue;
                }

                var renderer = target.GetComponent<Renderer>();
                if (renderer == null)
                {
                    Debug.LogWarning($"[Color Variant Generator] No Renderer component on: '{materialOverride.slot.rendererPath}'");
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
