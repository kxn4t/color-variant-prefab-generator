using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Kanameliser.ColorVariantGenerator
{
    /// <summary>
    /// Matches Renderer slots between a source prefab and a target prefab using a 5-tier algorithm.
    /// Used by the batch generator to map material overrides from one avatar base to another.
    /// All tiers require slotIndex + rendererType match as a hard filter.
    /// P1: Exact path+name, P2: Same depth+name, P3: Name (any depth),
    /// P4: Case-insensitive name, P5: Similar name (scored)
    /// </summary>
    internal static class RendererMatcher
    {
        // Scoring constants for PathSegmentScore
        private const float ExactSegmentScore = 100f;
        private const float NormalizedSegmentScore = 75f;
        private const float FuzzySegmentScore = 50f;
        private const float SegmentWeightDecay = 0.7f;
        private const float GapPenalty = -5f;

        // Fuzzy tier constants
        private const int EligibilityMinTokenLength = 3;
        private const int RankingMinTokenLength = 1;
        private const float NormalizedNameScore = 100f;
        private const float FuzzyNameScore = 60f;

        private static readonly char[] NameSeparators = { '_', '-', '.', ' ' };

        // NormalizeName suffix patterns (applied in order, first match per pass, then restart)
        private static readonly Regex NumericSuffix = new Regex(@"[_\-\s]\d+$", RegexOptions.Compiled);
        private static readonly Regex ParenthesizedSuffix = new Regex(@"\s*\(\d+\)$", RegexOptions.Compiled);
        private static readonly Regex BlenderSuffix = new Regex(@"\.\d{3}$", RegexOptions.Compiled);
        private static readonly Regex VersionSuffix = new Regex(@"[_\-\s]v(?:er)?\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex CopySuffix = new Regex(@"[_\-\s](?:copy|variant)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex TrailingSeparators = new Regex(@"[_\-\.\s]+$", RegexOptions.Compiled);

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
        /// Tries to find a matching target slot using 5 priority levels.
        /// P1-P4 require slotIndex + rendererType to match.
        /// P5 first tries with rendererType, then falls back to cross-type matching.
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

            // Common hard filters: slotIndex + rendererType
            bool HasSlotAndType(ScannedMaterialSlot c) =>
                c.identifier.slotIndex == sourceSlot.slotIndex &&
                c.identifier.rendererType == sourceSlot.rendererType;

            // P1: Exact relative path + exact object name + slotIndex + rendererType
            var p1 = targetSlots.Where(c => IsAvailable(c) && HasSlotAndType(c) &&
                c.identifier.rendererPath == sourceSlot.rendererPath &&
                c.identifier.objectName == sourceSlot.objectName).ToList();
            if (p1.Count == 1)
            {
                return (p1[0].identifier, 1);
            }
            if (p1.Count > 1)
            {
                var best = SelectBestCandidate(p1, sourceSlot, baseMaterial);
                return (best.identifier, 1);
            }

            // P2: Same hierarchy depth + exact name + slotIndex + rendererType
            var p2 = targetSlots.Where(c => IsAvailable(c) && HasSlotAndType(c) &&
                c.identifier.hierarchyDepth == sourceSlot.hierarchyDepth &&
                c.identifier.objectName == sourceSlot.objectName).ToList();
            if (p2.Count > 0)
            {
                var best = SelectBestCandidate(p2, sourceSlot, baseMaterial);
                return (best.identifier, 2);
            }

            // P3: Exact name (any depth) + slotIndex + rendererType
            var p3 = targetSlots.Where(c => IsAvailable(c) && HasSlotAndType(c) &&
                c.identifier.objectName == sourceSlot.objectName).ToList();
            if (p3.Count > 0)
            {
                var best = SelectBestCandidate(p3, sourceSlot, baseMaterial);
                return (best.identifier, 3);
            }

            // P4: Case-insensitive name + slotIndex + rendererType
            var p4 = targetSlots.Where(c => IsAvailable(c) && HasSlotAndType(c) &&
                string.Equals(c.identifier.objectName, sourceSlot.objectName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (p4.Count > 0)
            {
                var best = SelectBestCandidate(p4, sourceSlot, baseMaterial);
                return (best.identifier, 4);
            }

            // P5: Similar name (scored) + slotIndex + rendererType
            string sourceNormalized = NormalizeName(sourceSlot.objectName);
            var p5 = targetSlots.Where(c => IsAvailable(c) && HasSlotAndType(c) &&
                IsFuzzyEligible(sourceSlot.objectName, sourceNormalized, c.identifier.objectName)).ToList();
            if (p5.Count > 0)
            {
                var best = SelectBestFuzzyCandidate(p5, sourceSlot, sourceNormalized, baseMaterial);
                return (best.identifier, 5);
            }

            // P5 cross-type fallback: Fuzzy + slotIndex only (relax rendererType)
            // Catches cross-renderer-type matches (e.g. MeshRenderer ↔ SkinnedMeshRenderer)
            // when source and target use different renderer types for the same logical mesh.
            var p5CrossType = targetSlots.Where(c => IsAvailable(c) &&
                c.identifier.slotIndex == sourceSlot.slotIndex &&
                c.identifier.rendererType != sourceSlot.rendererType &&
                IsFuzzyEligible(sourceSlot.objectName, sourceNormalized, c.identifier.objectName)).ToList();
            if (p5CrossType.Count > 0)
            {
                var best = SelectBestFuzzyCandidate(p5CrossType, sourceSlot, sourceNormalized, baseMaterial);
                return (best.identifier, 5);
            }

            return null;
        }

        /// <summary>
        /// Determines whether a target name is eligible for the P5 fuzzy tier.
        /// Eligible if normalized names match or names share a common base name.
        /// </summary>
        private static bool IsFuzzyEligible(string sourceName, string sourceNormalized, string targetName)
        {
            string targetNormalized = NormalizeName(targetName);
            if (string.Equals(sourceNormalized, targetNormalized, StringComparison.OrdinalIgnoreCase)) return true;
            if (HasCommonBaseName(sourceName, targetName, EligibilityMinTokenLength)) return true;
            return false;
        }

        /// <summary>
        /// Selects the best candidate from multiple matches (P1-P4) using material name,
        /// path segment scoring, depth proximity, and Levenshtein distance.
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

            // Score by hierarchy path similarity, then depth proximity, then Levenshtein distance
            string sourcePath = sourceSlot.rendererPath ?? "";
            int sourceDepth = sourceSlot.hierarchyDepth;
            candidates = candidates
                .OrderByDescending(c => PathSegmentScore(sourcePath, c.identifier.rendererPath ?? ""))
                .ThenBy(c => Math.Abs(c.identifier.hierarchyDepth - sourceDepth))
                .ThenBy(c => LevenshteinDistance(sourcePath, c.identifier.rendererPath ?? ""))
                .ToList();

            return candidates[0];
        }

        /// <summary>
        /// Selects the best candidate for the P5 fuzzy tier using a composite score
        /// of name similarity and hierarchy path similarity.
        /// </summary>
        private static ScannedMaterialSlot SelectBestFuzzyCandidate(
            List<ScannedMaterialSlot> candidates,
            MaterialSlotIdentifier sourceSlot,
            string sourceNormalized,
            Material baseMaterial)
        {
            if (candidates.Count == 1) return candidates[0];

            string sourcePath = sourceSlot.rendererPath ?? "";
            int sourceDepth = sourceSlot.hierarchyDepth;

            // Compute TotalScore = NameScore + PathSegmentScore for each candidate
            var scored = candidates.Select(c =>
            {
                string targetNormalized = NormalizeName(c.identifier.objectName);
                float nameScore;
                if (string.Equals(sourceNormalized, targetNormalized, StringComparison.OrdinalIgnoreCase))
                    nameScore = NormalizedNameScore;
                else
                    nameScore = FuzzyNameScore;

                float pathScore = PathSegmentScore(sourcePath, c.identifier.rendererPath ?? "");
                float totalScore = nameScore + pathScore;

                bool materialMatch = baseMaterial != null &&
                    c.baseMaterial != null &&
                    c.baseMaterial.name == baseMaterial.name;

                return new
                {
                    slot = c,
                    totalScore,
                    materialMatch,
                    depthDiff = Math.Abs(c.identifier.hierarchyDepth - sourceDepth),
                    levenshtein = LevenshteinDistance(sourcePath, c.identifier.rendererPath ?? "")
                };
            }).ToList();

            // Sort: highest TotalScore, then material match, then smaller depth diff, then smaller Levenshtein
            scored = scored
                .OrderByDescending(x => x.totalScore)
                .ThenByDescending(x => x.materialMatch)
                .ThenBy(x => x.depthDiff)
                .ThenBy(x => x.levenshtein)
                .ToList();

            return scored[0].slot;
        }

        #region Name Normalization and Matching Utilities

        /// <summary>
        /// Strips trailing structural suffixes repeatedly until stable.
        /// Handles numeric, parenthesized, Blender, version, and copy/variant suffixes.
        /// Never strips if the result would be empty.
        /// After pattern stripping, trims trailing separators.
        /// </summary>
        internal static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name ?? "";

            string current = name;

            // Repeatedly strip suffixes until stable
            while (true)
            {
                string previous = current;

                // Try each pattern in order; on first match, restart the loop
                if (TryStripSuffix(current, NumericSuffix, out string stripped) ||
                    TryStripSuffix(current, ParenthesizedSuffix, out stripped) ||
                    TryStripSuffix(current, BlenderSuffix, out stripped) ||
                    TryStripSuffix(current, VersionSuffix, out stripped) ||
                    TryStripSuffix(current, CopySuffix, out stripped))
                {
                    current = stripped;
                }

                if (current == previous) break;
            }

            // Trim trailing separators
            string trimmed = TrailingSeparators.Replace(current, "");
            if (trimmed.Length > 0)
                current = trimmed;

            // Never return empty — fall back to original name
            return string.IsNullOrEmpty(current) ? name : current;
        }

        /// <summary>
        /// Attempts to strip a regex suffix from the name.
        /// Returns false if stripping would produce an empty result.
        /// </summary>
        private static bool TryStripSuffix(string input, Regex pattern, out string result)
        {
            var match = pattern.Match(input);
            if (match.Success)
            {
                string candidate = input.Substring(0, match.Index);
                if (!string.IsNullOrEmpty(candidate))
                {
                    result = candidate;
                    return true;
                }
            }
            result = input;
            return false;
        }

        /// <summary>
        /// Determines whether two names share a common base name using token-based matching.
        /// Returns true if:
        /// - Both strings are non-empty and equal, OR
        /// - Both have at least 2 tokens and their first tokens match, OR
        /// - One name is a single token that equals the first token of the other multi-token name
        ///   (e.g. "Shoes" matches "Shoes_red").
        /// In both cases the matching token must be >= minTokenLength.
        /// Separators: '_', '-', '.', space.
        /// </summary>
        internal static bool HasCommonBaseName(string a, string b, int minTokenLength = 3)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            if (a == b) return true;

            string[] partsA = a.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries);
            string[] partsB = b.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries);

            // Both have 2+ tokens: compare first tokens
            if (partsA.Length >= 2 && partsB.Length >= 2 &&
                partsA[0] == partsB[0] &&
                partsA[0].Length >= minTokenLength)
                return true;

            // One is a single token matching the other's first token
            // e.g. "Shoes" ↔ "Shoes_red"
            if (partsA.Length == 1 && partsB.Length >= 2 &&
                partsA[0].Length >= minTokenLength && partsA[0] == partsB[0])
                return true;
            if (partsB.Length == 1 && partsA.Length >= 2 &&
                partsB[0].Length >= minTokenLength && partsB[0] == partsA[0])
                return true;

            return false;
        }

        #endregion

        #region Path and Distance Utilities

        /// <summary>
        /// Computes a path similarity score by aligning segments from the leaf side.
        /// Uses 3-level scoring per segment: exact match, NormalizeName match, HasCommonBaseName match.
        /// Weight decreases for segments further from the leaf.
        /// Includes a gap penalty for segment count differences.
        /// </summary>
        internal static float PathSegmentScore(string sourcePath, string targetPath)
        {
            string[] sourceSegments = string.IsNullOrEmpty(sourcePath)
                ? Array.Empty<string>() : sourcePath.Split('/');
            string[] targetSegments = string.IsNullOrEmpty(targetPath)
                ? Array.Empty<string>() : targetPath.Split('/');

            if (sourceSegments.Length == 0 && targetSegments.Length == 0)
                return 0f;

            float score = 0f;
            float weight = 1.0f;
            int alignCount = Math.Min(sourceSegments.Length, targetSegments.Length);

            // Compare segments from the leaf upward
            for (int i = 0; i < alignCount; i++)
            {
                string s = sourceSegments[sourceSegments.Length - 1 - i];
                string t = targetSegments[targetSegments.Length - 1 - i];

                if (s == t)
                {
                    score += ExactSegmentScore * weight;
                }
                else if (string.Equals(NormalizeName(s), NormalizeName(t), StringComparison.OrdinalIgnoreCase))
                {
                    score += NormalizedSegmentScore * weight;
                }
                else if (HasCommonBaseName(s, t, RankingMinTokenLength))
                {
                    score += FuzzySegmentScore * weight;
                }
                // else: mismatch, adds nothing

                weight *= SegmentWeightDecay;
            }

            // Penalty for unmatched segments at the top
            int extraSegments = Math.Abs(sourceSegments.Length - targetSegments.Length);
            score += extraSegments * GapPenalty;

            return score;
        }

        /// <summary>
        /// Computes the Levenshtein (edit) distance between two strings.
        /// Used as a tiebreaker in candidate selection.
        /// </summary>
        internal static int LevenshteinDistance(string s, string t)
        {
            if (s == null) s = "";
            if (t == null) t = "";

            int sLen = s.Length;
            int tLen = t.Length;

            if (sLen == 0) return tLen;
            if (tLen == 0) return sLen;

            // Use single-row optimization to reduce memory usage
            var prev = new int[tLen + 1];
            var curr = new int[tLen + 1];

            for (int j = 0; j <= tLen; j++)
                prev[j] = j;

            for (int i = 1; i <= sLen; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= tLen; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(
                        Math.Min(curr[j - 1] + 1, prev[j] + 1),
                        prev[j - 1] + cost);
                }

                // Swap rows
                var temp = prev;
                prev = curr;
                curr = temp;
            }

            return prev[tLen];
        }

        #endregion
    }
}
