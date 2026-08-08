using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>Separators used to tokenize prefab names for variant name derivation.</summary>
        private static readonly char[] NameSeparators = { '_', '-', '.', ' ' };

        /// <summary>
        /// Derives the variant name from the variant file name. Strips the base name when it
        /// is a strict prefix; otherwise removes every token contained in the base name,
        /// regardless of position, which handles bases that are not a prefix of the variant
        /// name (e.g. a "_Base"/"_White" suffixed base).
        /// e.g., base="HonmeiKnit_Airi_Base", variant="HonmeiKnit_Airi_Black" → "Black"
        /// </summary>
        internal static string DeriveVariantName(string baseName, string variantName)
        {
            // Fast path: base name is a strict prefix. Kept ahead of token removal because it
            // also handles variants that repeat a base token (e.g. "White_Knit" + "White_Knit_White").
            if (variantName.StartsWith(baseName))
            {
                string suffix = variantName.Substring(baseName.Length).TrimStart('_', '-', ' ');
                if (!string.IsNullOrEmpty(suffix))
                {
                    return suffix;
                }
            }

            var baseTokens = new HashSet<string>(
                baseName.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);

            var remaining = variantName
                .Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => !baseTokens.Contains(t))
                .ToList();

            // Fallback: every token is part of the base name — use the full variant name
            return remaining.Count > 0 ? string.Join("_", remaining) : variantName;
        }

        /// <summary>
        /// Strips leading and trailing token sequences shared by ALL names, always leaving at
        /// least one token per name. Reduces a set of prefab names that share outfit/avatar
        /// affixes (e.g. "Knit_Airi_Black" / "Knit_Airi_White" → "Black" / "White"), which
        /// covers color prefabs distributed as standalone prefabs rather than Prefab Variants.
        /// Returns the input list unchanged when fewer than two names are given or nothing is shared.
        /// </summary>
        internal static List<string> StripCommonAffixTokens(List<string> names)
        {
            if (names == null || names.Count < 2) return names;

            var tokens = names
                .Select(n => n.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries))
                .ToArray();
            if (tokens.Any(t => t.Length == 0)) return names;

            int minLength = tokens.Min(t => t.Length);

            // Longest shared leading sequence, capped so the shortest name keeps one token
            int prefix = 0;
            while (prefix < minLength - 1 && TokensMatchAcross(tokens, prefix, fromEnd: false))
                prefix++;

            // Longest shared trailing sequence, capped so every name keeps one token after both cuts
            int maxSuffix = tokens.Min(t => t.Length - prefix) - 1;
            int suffix = 0;
            while (suffix < maxSuffix && TokensMatchAcross(tokens, suffix, fromEnd: true))
                suffix++;

            if (prefix == 0 && suffix == 0) return names;

            var result = new List<string>(names.Count);
            foreach (var t in tokens)
            {
                result.Add(string.Join("_", t.Skip(prefix).Take(t.Length - prefix - suffix)));
            }
            return result;
        }

        private static bool TokensMatchAcross(string[][] tokens, int index, bool fromEnd)
        {
            string first = fromEnd ? tokens[0][tokens[0].Length - 1 - index] : tokens[0][index];
            for (int i = 1; i < tokens.Length; i++)
            {
                string other = fromEnd ? tokens[i][tokens[i].Length - 1 - index] : tokens[i][index];
                if (!string.Equals(first, other, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }
    }
}
