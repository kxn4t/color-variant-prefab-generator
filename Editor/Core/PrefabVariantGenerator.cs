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
                // Validate inputs
                if (basePrefabAsset == null)
                {
                    result.errorMessage = "Base prefab is null.";
                    return result;
                }

                if (string.IsNullOrEmpty(variantName))
                {
                    result.errorMessage = "Variant name is empty.";
                    return result;
                }

                if (!EditorUIUtility.IsValidOutputPath(outputPath))
                {
                    result.errorMessage = $"Output path must be inside the project's Assets folder: '{outputPath}'";
                    return result;
                }

                // Compute output file path
                string baseName = basePrefabAsset.name;
                string variantFileName = EditorUIUtility.ResolveFileName(namingTemplate, baseName, variantName);
                string fullPath = EditorUIUtility.NormalizePath(
                    Path.Combine(outputPath, variantFileName + ".prefab"));

                // Ensure output directory exists
                string directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Create a fresh instance from the base prefab (maintains the prefab link)
                var instance = PrefabUtility.InstantiatePrefab(basePrefabAsset) as GameObject;
                if (instance == null)
                {
                    result.errorMessage = "Failed to instantiate base prefab.";
                    return result;
                }

                try
                {
                    // Apply material overrides only
                    int appliedCount = 0;
                    foreach (var materialOverride in overrides)
                    {
                        if (materialOverride?.overrideMaterial == null) continue;
                        if (materialOverride.slot == null) continue;

                        Transform target;
                        if (string.IsNullOrEmpty(materialOverride.slot.rendererPath))
                        {
                            target = instance.transform;
                        }
                        else
                        {
                            target = instance.transform.Find(materialOverride.slot.rendererPath);
                        }

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
                            Debug.LogWarning($"[Color Variant Generator] Slot index {materialOverride.slot.slotIndex} out of range for '{materialOverride.slot.rendererPath}' (has {materials.Length} slots)");
                        }
                    }

                    // Save as Prefab Variant
                    // When an instance created via InstantiatePrefab is saved with SaveAsPrefabAsset,
                    // Unity automatically creates a Prefab Variant linked to the base prefab.
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
                finally
                {
                    // Clean up the temporary instance
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
                // Validate inputs
                if (request.basePrefabAsset == null)
                {
                    result.errorMessage = "Base prefab is null.";
                    return result;
                }

                if (request.hierarchyInstance == null)
                {
                    result.errorMessage = "Hierarchy instance is null.";
                    return result;
                }

                if (string.IsNullOrEmpty(request.variantName))
                {
                    result.errorMessage = "Variant name is empty.";
                    return result;
                }

                if (!EditorUIUtility.IsValidOutputPath(request.outputPath))
                {
                    result.errorMessage = $"Output path must be inside the project's Assets folder: '{request.outputPath}'";
                    return result;
                }

                // Compute output file path
                string baseName = request.basePrefabAsset.name;
                string variantFileName = EditorUIUtility.ResolveFileName(
                    request.namingTemplate, baseName, request.variantName);
                string fullPath = EditorUIUtility.NormalizePath(
                    Path.Combine(request.outputPath, variantFileName + ".prefab"));

                // Ensure output directory exists
                string directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Create a fresh instance from the base prefab (maintains the prefab link)
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
                    int appliedCount = 0;
                    if (request.materialOverrides != null)
                    {
                        foreach (var materialOverride in request.materialOverrides)
                        {
                            if (materialOverride?.overrideMaterial == null) continue;
                            if (materialOverride.slot == null) continue;

                            Transform target;
                            if (string.IsNullOrEmpty(materialOverride.slot.rendererPath))
                            {
                                target = instance.transform;
                            }
                            else
                            {
                                target = instance.transform.Find(materialOverride.slot.rendererPath);
                            }

                            if (target == null)
                            {
                                Debug.LogWarning(
                                    $"[Color Variant Generator] Renderer path not found: '{materialOverride.slot.rendererPath}'");
                                continue;
                            }

                            var renderer = target.GetComponent<Renderer>();
                            if (renderer == null)
                            {
                                Debug.LogWarning(
                                    $"[Color Variant Generator] No Renderer component on: '{materialOverride.slot.rendererPath}'");
                                continue;
                            }

                            var materials = renderer.sharedMaterials;
                            if (materialOverride.slot.slotIndex >= 0 &&
                                materialOverride.slot.slotIndex < materials.Length)
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
                    }

                    // Save as Prefab Variant
                    bool success;
                    PrefabUtility.SaveAsPrefabAsset(instance, fullPath, out success);

                    result.success = success;
                    result.path = fullPath;

                    if (success)
                    {
                        Debug.Log(
                            $"[Color Variant Generator] Generated Prefab Variant (Standard): '{fullPath}' ({appliedCount} material overrides applied)");
                    }
                    else
                    {
                        result.errorMessage = $"PrefabUtility.SaveAsPrefabAsset failed for '{fullPath}'.";
                    }
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
    }
}
