using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Kanameliser.ColorVariantGenerator
{
    internal partial class BatchGeneratorWindow
    {
        // ────────────────────────────────────────────────
        // Matching
        // ────────────────────────────────────────────────

        /// <summary>
        /// Re-scans all variant entries, compares each against the new base prefab,
        /// and rebuilds the aggregated match results list and UI.
        /// </summary>
        private void RunAllMatching()
        {
            _allMatchResults.Clear();

            foreach (var entry in _variantEntries)
            {
                entry.matchResults = null;
                if (entry.variantPrefab == null || _newBasePrefab == null) continue;

                var sourceSlots = PrefabScanner.ScanRenderers(entry.variantPrefab);
                entry.matchResults = RendererMatcher.CompareRenderers(sourceSlots, _newBaseSlots);
                _allMatchResults.AddRange(entry.matchResults);
            }

            RefreshMatchResultsUI();
            UpdateVariantOverrideLabels();
            UpdateOutputPreview();
        }

        private void UpdateVariantOverrideLabels()
        {
            for (int i = 0; i < _variantEntries.Count && i < _variantListContainer.childCount; i++)
            {
                var entry = _variantEntries[i];
                var row = _variantListContainer[i];
                var overrideLabel = row.Q<Label>(className: "variant-override-label");
                if (overrideLabel == null) continue;

                if (entry.matchResults != null && entry.matchResults.Count > 0)
                {
                    int count = entry.matchResults.Count(r => r.targetSlot != null);
                    overrideLabel.text = Localization.S("batch.variantList.overrides", count);
                    overrideLabel.tooltip = Localization.S("batch.variantList.overridesTooltip", count);
                }
                else
                {
                    overrideLabel.text = "";
                    overrideLabel.tooltip = "";
                }
            }
        }

        // ────────────────────────────────────────────────
        // Section: Matching Results
        //
        // UI structure:
        //   "Matching Results" (fixed label)
        //   "No matching results yet..." (empty label, shown when no results)
        //   ▶ "N/M variants fully matched" (summary foldout, hidden when no results, auto-expand when unmatched)
        //     ▶ "{variantName} (N/M matched)" (per-variant foldout)
        //       ⚠ Unmatched (N) + unmatched rows (if any)
        //       ▶ Matched (N) sub-foldout (only when mixed; all-matched shows rows directly)
        // ────────────────────────────────────────────────

        private void CreateMatchResultsSection(VisualElement container)
        {
            var section = new VisualElement();
            section.AddToClassList("section-container");
            section.AddToClassList("match-results-section");

            var label = new Label("batch.matching");
            label.AddToClassList("section-label");
            label.AddToClassList("ndmf-tr");
            section.Add(label);

            _matchEmptyLabel = new Label("batch.matching.empty");
            _matchEmptyLabel.AddToClassList("match-empty-label");
            _matchEmptyLabel.AddToClassList("ndmf-tr");
            section.Add(_matchEmptyLabel);

            _matchResultsFoldout = new Foldout { value = false };
            _matchResultsFoldout.AddToClassList("match-results-foldout");
            _matchResultsFoldout.style.display = DisplayStyle.None;
            section.Add(_matchResultsFoldout);

            _matchResultsContainer = new VisualElement();
            _matchResultsContainer.AddToClassList("match-results-container");
            _matchResultsFoldout.Add(_matchResultsContainer);

            container.Add(section);
        }

        private void RefreshMatchResultsUI()
        {
            _matchResultsContainer.Clear();

            var variantsWithResults = _variantEntries
                .Where(e => e.matchResults != null && e.matchResults.Count > 0)
                .ToList();

            if (variantsWithResults.Count == 0)
            {
                _matchEmptyLabel.style.display = DisplayStyle.Flex;
                _matchResultsFoldout.style.display = DisplayStyle.None;
                _matchResultsFoldout.RemoveFromClassList("match-summary-warning");
                return;
            }

            _matchEmptyLabel.style.display = DisplayStyle.None;
            _matchResultsFoldout.style.display = DisplayStyle.Flex;

            int totalVariants = variantsWithResults.Count;
            int fullyMatchedVariants = variantsWithResults.Count(e =>
                e.matchResults.All(r => r.targetSlot != null));

            bool hasUnmatched = fullyMatchedVariants < totalVariants;
            _matchResultsFoldout.text = Localization.S("batch.matching.summary", fullyMatchedVariants, totalVariants);
            _matchResultsFoldout.value = hasUnmatched;
            if (hasUnmatched)
                _matchResultsFoldout.AddToClassList("match-summary-warning");
            else
                _matchResultsFoldout.RemoveFromClassList("match-summary-warning");

            foreach (var entry in variantsWithResults)
            {
                var group = CreateVariantMatchGroup(entry);
                _matchResultsContainer.Add(group);
            }

            RemoveLastChildBottomBorder(_matchResultsContainer);
        }

        private VisualElement CreateVariantMatchGroup(VariantEntry entry)
        {
            int matched = entry.matchResults.Count(r => r.targetSlot != null);
            int total = entry.matchResults.Count;
            int unmatched = total - matched;
            bool isFullyMatched = unmatched == 0;
            string variantName = entry.EffectiveVariantName;
            if (string.IsNullOrEmpty(variantName)) variantName = Localization.S("batch.matching.unnamed");

            // Auto-expand foldout when there are unmatched items to draw attention
            var foldout = new Foldout
            {
                text = Localization.S("batch.matching.variantSummary", variantName, matched, total),
                value = !isFullyMatched
            };
            foldout.AddToClassList("variant-match-foldout");
            if (!isFullyMatched)
                foldout.AddToClassList("variant-match-foldout--warning");

            // Unmatched items (shown prominently)
            var unmatchedResults = entry.matchResults.Where(r => r.targetSlot == null).ToList();
            if (unmatchedResults.Count > 0)
            {
                var unmatchedHeader = new Label($"{EditorUIUtility.Warning} {Localization.S("batch.matching.unmatched", unmatchedResults.Count)}");
                unmatchedHeader.AddToClassList("unmatched-header");
                foldout.Add(unmatchedHeader);

                foreach (var result in unmatchedResults)
                {
                    var row = CreateUnmatchedRow(result);
                    foldout.Add(row);
                }
            }

            // Matched items
            var matchedResults = entry.matchResults.Where(r => r.targetSlot != null).ToList();
            if (matchedResults.Count > 0)
            {
                if (isFullyMatched)
                {
                    // All matched — show rows directly without sub-foldout
                    foreach (var result in matchedResults)
                    {
                        var row = CreateMatchedRow(result);
                        foldout.Add(row);
                    }
                }
                else
                {
                    // Mixed — wrap matched items in a collapsed sub-foldout
                    var matchedFoldout = new Foldout
                    {
                        text = Localization.S("batch.matching.matched", matchedResults.Count),
                        value = false
                    };
                    matchedFoldout.AddToClassList("matched-foldout");

                    foreach (var result in matchedResults)
                    {
                        var row = CreateMatchedRow(result);
                        matchedFoldout.Add(row);
                    }

                    foldout.Add(matchedFoldout);
                }
            }

            return foldout;
        }

        private VisualElement CreateUnmatchedRow(RendererMatchResult result)
        {
            var row = new VisualElement();
            row.AddToClassList("match-row");
            row.AddToClassList("match-row-unmatched");

            // Source info
            var sourceLabel = new Label($"{result.sourceSlot.DisplayName} ({result.overrideMaterial?.name ?? "?"})");
            sourceLabel.AddToClassList("match-source-label");
            row.Add(sourceLabel);

            var arrow = new Label(EditorUIUtility.Arrow);
            arrow.AddToClassList("match-arrow");
            row.Add(arrow);

            // Dropdown to manually assign a target slot from the new base prefab.
            // Populated with all available target slots so the user can fix unmatched items.
            var dropdown = new PopupField<string>();
            dropdown.AddToClassList("match-target-dropdown");

            var targetChoices = new List<string> { Localization.S("batch.matching.unmatchedChoice") };
            var targetSlotMap = new Dictionary<string, MaterialSlotIdentifier>();

            foreach (var slot in _newBaseSlots)
            {
                string display = $"{slot.identifier.DisplayName} [{slot.baseMaterial?.name ?? "None"}]";
                targetChoices.Add(display);
                targetSlotMap[display] = slot.identifier;
            }

            dropdown.choices = targetChoices;
            dropdown.value = targetChoices[0];

            var capturedResult = result;
            dropdown.RegisterValueChangedCallback(evt =>
            {
                if (targetSlotMap.TryGetValue(evt.newValue, out var targetSlot))
                {
                    capturedResult.targetSlot = targetSlot;
                    capturedResult.matchPriority = 0; // Manual match
                }
                else
                {
                    capturedResult.targetSlot = null;
                }
                RefreshMatchResultsUI();
                UpdateGenerateButtonState();
            });
            row.Add(dropdown);

            // Exclude button
            var excludeBtn = new Button(() =>
            {
                _allMatchResults.Remove(capturedResult);

                // Also remove from the variant entry
                foreach (var entry in _variantEntries)
                {
                    entry.matchResults?.Remove(capturedResult);
                }

                RefreshMatchResultsUI();
                UpdateGenerateButtonState();
            })
            {
                text = Localization.S("batch.matching.exclude"),
                tooltip = Localization.S("batch.matching.excludeTooltip")
            };
            excludeBtn.AddToClassList("match-exclude-button");
            row.Add(excludeBtn);

            return row;
        }

        private VisualElement CreateMatchedRow(RendererMatchResult result)
        {
            var row = new VisualElement();
            row.AddToClassList("match-row");
            row.AddToClassList("match-row-matched");

            var sourceLabel = new Label(result.sourceSlot.DisplayName);
            sourceLabel.AddToClassList("match-source-label");
            row.Add(sourceLabel);

            var arrow = new Label(EditorUIUtility.Arrow);
            arrow.AddToClassList("match-arrow");
            row.Add(arrow);

            var targetLabel = new Label(result.targetSlot?.DisplayName ?? "?");
            targetLabel.AddToClassList("match-target-label");
            row.Add(targetLabel);

            // Show material change and match priority (P1=exact path, P2=depth+name, P3=name, P4=case-insensitive)
            string baseName = result.targetBaseMaterial?.name ?? "?";
            string overrideName = result.overrideMaterial?.name ?? "?";
            var materialLabel = new Label($"[{baseName} {EditorUIUtility.Arrow} {overrideName}] (P{result.matchPriority})");
            materialLabel.AddToClassList("match-priority-label");
            row.Add(materialLabel);

            return row;
        }
    }
}
