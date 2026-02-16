using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kanameliser.ColorVariantGenerator
{
    /// <summary>
    /// Analyzes existing Prefab Variants to extract material override information.
    /// Used by the batch generator to determine what materials were changed in each variant.
    /// </summary>
    internal static class VariantAnalyzer
    {
        /// <summary>
        /// Analyzes a Prefab Variant and extracts all material overrides relative to its base prefab.
        /// </summary>
        /// <remarks>
        /// Not currently called by the built-in UI (the Batch Generator now uses
        /// <see cref="RendererMatcher.CompareRenderers"/> for direct comparison),
        /// but retained for future features that need to introspect existing Prefab Variants.
        /// </remarks>
        public static VariantAnalysisResult AnalyzeVariant(GameObject variantPrefab)
        {
            var result = new VariantAnalysisResult();

            if (variantPrefab == null)
            {
                Debug.LogWarning("[Color Variant Generator] VariantAnalyzer: variantPrefab is null.");
                return result;
            }

            // Get the original base prefab
            var basePrefab = PrefabUtility.GetCorrespondingObjectFromOriginalSource(variantPrefab);
            if (basePrefab == null)
            {
                Debug.LogWarning($"[Color Variant Generator] Could not find base prefab for '{variantPrefab.name}'.");
                return result;
            }

            result.basePrefab = basePrefab;

            // Derive variant name from file name difference
            result.variantName = DeriveVariantName(basePrefab.name, variantPrefab.name);

            // Scan both base and variant renderers
            var baseSlots = PrefabScanner.ScanRenderers(basePrefab);
            var variantSlots = PrefabScanner.ScanRenderers(variantPrefab);

            // Build lookup for variant slots
            var variantLookup = new Dictionary<string, Material>();
            foreach (var vs in variantSlots)
            {
                string key = vs.identifier.GetLookupKey();
                variantLookup[key] = vs.baseMaterial;
            }

            // Compare and find overrides
            foreach (var baseSlot in baseSlots)
            {
                string key = baseSlot.identifier.GetLookupKey();
                if (!variantLookup.TryGetValue(key, out var variantMaterial)) continue;

                // Compare materials — if different, it's an override
                if (variantMaterial != baseSlot.baseMaterial)
                {
                    result.overrides.Add(new MaterialOverrideInfo
                    {
                        slot = baseSlot.identifier,
                        baseMaterial = baseSlot.baseMaterial,
                        overrideMaterial = variantMaterial
                    });
                }
            }

            Debug.Log($"[Color Variant Generator] Analyzed '{variantPrefab.name}': " +
                      $"base='{basePrefab.name}', {result.overrides.Count} material override(s) found.");

            return result;
        }

        /// <summary>
        /// Derives the variant name by removing the base name prefix from the variant file name.
        /// e.g., base="Airi_HonmeiKnit", variant="Airi_HonmeiKnit_Black" → "Black"
        /// </summary>
        internal static string DeriveVariantName(string baseName, string variantName)
        {
            if (variantName.StartsWith(baseName))
            {
                string suffix = variantName.Substring(baseName.Length).TrimStart('_', '-', ' ');
                if (!string.IsNullOrEmpty(suffix))
                {
                    return suffix;
                }
            }

            // Fallback: use full variant name
            return variantName;
        }
    }
}
