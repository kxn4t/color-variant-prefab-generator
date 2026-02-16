using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kanameliser.ColorVariantGenerator
{
    /// <summary>
    /// Matches Renderer slots between a source prefab and a target prefab using a 4-tier algorithm.
    /// Used by the batch generator to map material overrides from one avatar base to another.
    /// Algorithm reference: editor-plus ObjectMatcher.cs
    /// </summary>
    internal static class RendererMatcher
    {
        /// <summary>
        /// Matches source material overrides to target prefab renderer slots.
        /// Returns a list of match results, one per source override.
        /// </summary>
        /// <remarks>
        /// Not currently called by the built-in UI (replaced by <see cref="CompareRenderers"/>),
        /// but retained for future features that work with
        /// pre-extracted <see cref="MaterialOverrideInfo"/> lists.
        /// </remarks>
        public static List<RendererMatchResult> MatchRenderers(
            List<MaterialOverrideInfo> sourceOverrides,
            GameObject targetPrefab)
        {
            var results = new List<RendererMatchResult>();
            if (sourceOverrides == null || targetPrefab == null) return results;

            var targetSlots = PrefabScanner.ScanRenderers(targetPrefab);
            var matchedTargetKeys = new HashSet<string>();

            foreach (var sourceOverride in sourceOverrides)
            {
                var matchResult = new RendererMatchResult
                {
                    sourceSlot = sourceOverride.slot,
                    overrideMaterial = sourceOverride.overrideMaterial,
                    targetSlot = null,
                    matchPriority = 0
                };

                var match = TryMatch(sourceOverride.slot, sourceOverride.baseMaterial, targetSlots, matchedTargetKeys);
                if (match.HasValue)
                {
                    matchResult.targetSlot = match.Value.slot;
                    matchResult.matchPriority = match.Value.priority;
                    matchedTargetKeys.Add(match.Value.slot.GetLookupKey());
                }

                results.Add(matchResult);
            }

            return results;
        }

        /// <summary>
        /// Compares two sets of scanned renderer slots and returns match results
        /// only for slots where the material differs between source and target.
        /// Used by both the Batch Generator and Creator Import.
        /// </summary>
        public static List<RendererMatchResult> CompareRenderers(
            List<ScannedMaterialSlot> sourceSlots,
            List<ScannedMaterialSlot> targetSlots)
        {
            var results = new List<RendererMatchResult>();
            if (sourceSlots == null || targetSlots == null) return results;

            // Track already-matched targets to prevent multiple sources mapping to the same slot
            var matchedTargetKeys = new HashSet<string>();

            // Build a lookup for O(1) access to target slot data by key
            var targetSlotLookup = new Dictionary<string, ScannedMaterialSlot>(targetSlots.Count);
            foreach (var ts in targetSlots)
            {
                var key = ts.identifier.GetLookupKey();
                if (!targetSlotLookup.ContainsKey(key))
                    targetSlotLookup[key] = ts;
            }

            foreach (var sourceSlot in sourceSlots)
            {
                var match = TryMatch(sourceSlot.identifier, sourceSlot.baseMaterial, targetSlots, matchedTargetKeys);
                if (!match.HasValue)
                {
                    // Unmatched source slot — include only if it has a material
                    if (sourceSlot.baseMaterial != null)
                    {
                        results.Add(new RendererMatchResult
                        {
                            sourceSlot = sourceSlot.identifier,
                            targetSlot = null,
                            matchPriority = 0,
                            overrideMaterial = sourceSlot.baseMaterial,
                            targetBaseMaterial = null
                        });
                    }
                    continue;
                }

                var targetId = match.Value.slot;
                int priority = match.Value.priority;
                matchedTargetKeys.Add(targetId.GetLookupKey());

                // Find the target's current material
                if (!targetSlotLookup.TryGetValue(targetId.GetLookupKey(), out var targetSlot))
                    continue;

                // Only include if materials differ
                if (sourceSlot.baseMaterial != targetSlot.baseMaterial)
                {
                    results.Add(new RendererMatchResult
                    {
                        sourceSlot = sourceSlot.identifier,
                        targetSlot = targetId,
                        matchPriority = priority,
                        overrideMaterial = sourceSlot.baseMaterial,
                        targetBaseMaterial = targetSlot.baseMaterial
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// Tries to find a matching target slot using 4 priority levels.
        /// Returns (slot, priority) or null if no match found.
        /// </summary>
        private static (MaterialSlotIdentifier slot, int priority)? TryMatch(
            MaterialSlotIdentifier sourceSlot,
            Material baseMaterial,
            List<ScannedMaterialSlot> targetSlots,
            HashSet<string> matchedTargetKeys)
        {
            // Inline candidate filtering — avoids allocating a new list per call
            bool IsAvailable(ScannedMaterialSlot s) => !matchedTargetKeys.Contains(s.identifier.GetLookupKey());

            // Priority 1: Exact relative path + object name match + slot index
            var p1 = targetSlots.Where(c => IsAvailable(c) &&
                c.identifier.rendererPath == sourceSlot.rendererPath &&
                c.identifier.objectName == sourceSlot.objectName &&
                c.identifier.slotIndex == sourceSlot.slotIndex).ToList();
            if (p1.Count == 1)
            {
                return (p1[0].identifier, 1);
            }
            if (p1.Count > 1)
            {
                var best = SelectBestCandidate(p1, sourceSlot, baseMaterial);
                return (best.identifier, 1);
            }

            // Priority 2: Same hierarchy depth + exact name match + slot index
            var p2 = targetSlots.Where(c => IsAvailable(c) &&
                c.identifier.hierarchyDepth == sourceSlot.hierarchyDepth &&
                c.identifier.objectName == sourceSlot.objectName &&
                c.identifier.slotIndex == sourceSlot.slotIndex).ToList();
            if (p2.Count > 0)
            {
                var best = SelectBestCandidate(p2, sourceSlot, baseMaterial);
                return (best.identifier, 2);
            }

            // Priority 3: Exact name match (any depth) + slot index
            var p3 = targetSlots.Where(c => IsAvailable(c) &&
                c.identifier.objectName == sourceSlot.objectName &&
                c.identifier.slotIndex == sourceSlot.slotIndex).ToList();
            if (p3.Count > 0)
            {
                var best = SelectBestCandidate(p3, sourceSlot, baseMaterial);
                return (best.identifier, 3);
            }

            // Priority 4: Case-insensitive name match + slot index
            var p4 = targetSlots.Where(c => IsAvailable(c) &&
                string.Equals(c.identifier.objectName, sourceSlot.objectName, StringComparison.OrdinalIgnoreCase) &&
                c.identifier.slotIndex == sourceSlot.slotIndex).ToList();
            if (p4.Count > 0)
            {
                var best = SelectBestCandidate(p4, sourceSlot, baseMaterial);
                return (best.identifier, 4);
            }

            return null;
        }

        /// <summary>
        /// Selects the best candidate from multiple matches based on depth proximity and path similarity.
        /// </summary>
        private static ScannedMaterialSlot SelectBestCandidate(
            List<ScannedMaterialSlot> candidates,
            MaterialSlotIdentifier sourceSlot,
            Material baseMaterial)
        {
            if (candidates.Count == 1) return candidates[0];

            // Prefer matching base material name
            if (baseMaterial != null)
            {
                var byMaterial = candidates.Where(c =>
                    c.baseMaterial != null &&
                    c.baseMaterial.name == baseMaterial.name).ToList();
                if (byMaterial.Count == 1) return byMaterial[0];
                if (byMaterial.Count > 1) candidates = byMaterial;
            }

            // Prefer closest hierarchy depth, then tiebreak by path similarity (Levenshtein distance)
            int sourceDepth = sourceSlot.hierarchyDepth;
            string sourcePath = sourceSlot.rendererPath ?? "";
            candidates = candidates
                .OrderBy(c => Math.Abs(c.identifier.hierarchyDepth - sourceDepth))
                .ThenBy(c => LevenshteinDistance(sourcePath, c.identifier.rendererPath ?? ""))
                .ToList();

            return candidates[0];
        }

        /// <summary>
        /// Computes the Levenshtein distance between two strings.
        /// </summary>
        private static int LevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s)) return t?.Length ?? 0;
            if (string.IsNullOrEmpty(t)) return s.Length;

            int n = s.Length;
            int m = t.Length;
            var d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[n, m];
        }
    }
}
