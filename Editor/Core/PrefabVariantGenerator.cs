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
