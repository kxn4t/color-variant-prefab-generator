using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kanameliser.ColorVariantGenerator
{
    internal partial class CreatorWindow
    {
        // ────────────────────────────────────────────────
        // Section: Base Prefab
        // ────────────────────────────────────────────────

        private void CreateBasePrefabSection(VisualElement container)
        {
            var section = new VisualElement();
            section.AddToClassList("base-prefab-section");

            var labelRow = new VisualElement();
            labelRow.AddToClassList("section-label-row");

            var label = new Label("creator.basePrefab");
            label.AddToClassList("section-label");
            label.AddToClassList("ndmf-tr");
            labelRow.Add(label);

            // Strict mode indicator badge (right-aligned, shown only in Strict mode)
            _strictIndicatorLabel = new Label(Localization.S("creator.strictIndicator"));
            _strictIndicatorLabel.AddToClassList("strict-indicator");
            _strictIndicatorLabel.tooltip = Localization.S("creator.strictIndicator:tooltip");
            _strictIndicatorLabel.style.display = _creatorMode == CreatorMode.Strict
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            labelRow.Add(_strictIndicatorLabel);

            section.Add(labelRow);

            var row = new VisualElement();
            row.AddToClassList("base-prefab-row");

            _basePrefabField = new ObjectField
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                tooltip = Localization.S("creator.basePrefab:tooltip")
            };
            _basePrefabField.AddToClassList("base-prefab-field");
            _basePrefabField.RegisterValueChangedCallback(OnBasePrefabChanged);
            row.Add(_basePrefabField);

            // Options dropdown button
            _optionsButton = new Button(() =>
            {
                var menu = new GenericMenu();

                // Strict mode toggle (single checkbox)
                menu.AddItem(
                    new GUIContent(Localization.S("creator.menu.modeStrict")),
                    _creatorMode == CreatorMode.Strict,
                    () => SetCreatorMode(_creatorMode == CreatorMode.Strict
                        ? CreatorMode.Standard : CreatorMode.Strict));

                menu.AddSeparator("");

                // Import from Prefab toggle
                menu.AddItem(new GUIContent(Localization.S("creator.menu.importFromPrefab")), _importSection?.resolvedStyle.display != DisplayStyle.None, () =>
                {
                    if (_importSection == null) return;
                    bool visible = _importSection.resolvedStyle.display != DisplayStyle.None;
                    _importSection.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
                });
                menu.ShowAsContext();
            })
            {
                text = EditorUIUtility.DropdownArrow,
                tooltip = Localization.S("creator.optionsButton.tooltip")
            };
            _optionsButton.AddToClassList("options-button");
            row.Add(_optionsButton);

            section.Add(row);

            // Warning label for non-scene objects (hidden by default)
            _basePrefabWarningLabel = new Label();
            _basePrefabWarningLabel.AddToClassList("base-prefab-warning");
            _basePrefabWarningLabel.style.display = DisplayStyle.None;
            section.Add(_basePrefabWarningLabel);

            // Import from Prefab section (hidden by default)
            _importSection = new VisualElement();
            _importSection.AddToClassList("import-section");
            _importSection.style.display = DisplayStyle.None;

            var importLabel = new Label("creator.importFromPrefab");
            importLabel.AddToClassList("import-label");
            importLabel.AddToClassList("ndmf-tr");
            _importSection.Add(importLabel);

            // Message shown when base prefab is not set
            _importMessageLabel = new Label("creator.import.message");
            _importMessageLabel.AddToClassList("import-message");
            _importMessageLabel.AddToClassList("ndmf-tr");
            _importSection.Add(_importMessageLabel);

            var importRow = new VisualElement();
            importRow.AddToClassList("import-row");

            _importPrefabField = new ObjectField
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false,
                tooltip = Localization.S("creator.importField.tooltip")
            };
            _importPrefabField.AddToClassList("import-field");
            _importPrefabField.SetEnabled(false);
            importRow.Add(_importPrefabField);

            _importApplyButton = new Button(OnApplyImport)
            {
                text = "creator.import.apply"
            };
            _importApplyButton.AddToClassList("import-apply-button");
            _importApplyButton.AddToClassList("ndmf-tr");
            _importApplyButton.SetEnabled(false);
            importRow.Add(_importApplyButton);

            _importSection.Add(importRow);
            section.Add(_importSection);

            container.Add(section);
        }

        private void OnBasePrefabChanged(ChangeEvent<Object> evt)
        {
            ResetPreview();
            _scannedSlots.Clear();
            _overrides.Clear();
            _originalMaterials.Clear();
            _preExistingOverrides.Clear();
            _lastSlotKeys.Clear();
            _bulkFoldoutState.Clear();
            _bulkNullMaterialFoldout = false;
            _rendererFoldoutState.Clear();

            _baseInstance = evt.newValue as GameObject;
            _basePrefabAsset = null;
            _ancestorChain.Clear();
            _selectedVariantParent = null;
            _basePrefabWarningLabel.style.display = DisplayStyle.None;

            if (_baseInstance != null)
            {
                // Reject project assets — only scene instances are allowed
                if (!_baseInstance.scene.IsValid())
                {
                    _basePrefabField.SetValueWithoutNotify(null);
                    _baseInstance = null;
                    _currentWarningKey = "creator.warning.notSceneInstance";
                    _basePrefabWarningLabel.text = Localization.S(_currentWarningKey);
                    _basePrefabWarningLabel.style.display = DisplayStyle.Flex;
                    UpdateImportSectionState();
                    RefreshAllUI();
                    return;
                }

                // Verify it's a prefab instance (not just any GameObject)
                _basePrefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(_baseInstance);
                if (_basePrefabAsset == null)
                {
                    _currentWarningKey = "creator.warning.notPrefabInstance";
                    _basePrefabWarningLabel.text = Localization.S(_currentWarningKey);
                    _basePrefabWarningLabel.style.display = DisplayStyle.Flex;
                }
                else
                {
                    ScanMaterialSlots();
                    BuildAncestorChain();
                    RefreshStructuralChanges();
                }
            }

            UpdateParentDropdown();

            // Update import section enabled state
            UpdateImportSectionState();

            RefreshAllUI();
        }

        private void ScanMaterialSlots()
        {
            ScanMaterialSlotsInternal(preserveOverrides: false);
        }

        /// <summary>
        /// Rescans renderers from the current hierarchy instance, picking up added/removed
        /// GameObjects and new material slots. User-set overrides on still-existing slots
        /// are preserved when <paramref name="preserveOverrides"/> is true.
        /// </summary>
        private void RescanMaterialSlots()
        {
            ScanMaterialSlotsInternal(preserveOverrides: true);
        }

        private void ScanMaterialSlotsInternal(bool preserveOverrides)
        {
            if (_baseInstance == null) return;

            // Snapshot existing user overrides before clearing
            var previousOverrides = preserveOverrides
                ? new Dictionary<MaterialSlotIdentifier, Material>(_overrides)
                : null;

            // Snapshot which slots were already marked pre-existing on the previous scan,
            // so rescans can tell a tool-made override apart from a genuine pre-existing
            // override. matProp.prefabOverride alone can't distinguish them — writing a
            // material through this tool also sets prefabOverride — and reclassifying
            // tool-made overrides as pre-existing would defeat SelectiveRevert.
            var previousPreExistingOverrides = preserveOverrides
                ? new HashSet<MaterialSlotIdentifier>(_preExistingOverrides)
                : null;

            _scannedSlots = PrefabScanner.ScanRenderers(_baseInstance);

            // Store original materials for preview reset
            _originalMaterials.Clear();
            _preExistingOverrides.Clear();
            _overrides.Clear();

            bool isPrefabInstance = PrefabUtility.IsPartOfPrefabInstance(_baseInstance);

            foreach (var slot in _scannedSlots)
            {
                // Snapshot which slots already have prefab overrides before this tool touched them.
                // Used by SelectiveRevert to avoid reverting user-made overrides.
                if (isPrefabInstance)
                {
                    var renderer = FindRenderer(slot.identifier);
                    if (renderer != null)
                    {
                        var so = new SerializedObject(renderer);
                        var matProp = so.FindProperty($"m_Materials.Array.data[{slot.identifier.slotIndex}]");
                        if (matProp != null && matProp.prefabOverride)
                        {
                            // On a rescan, carry the pre-existing flag only for slots that were
                            // already pre-existing, or for slots we've never seen before (e.g.
                            // overrides on a newly-added nested Prefab). A slot with a tool-made
                            // override in the previous snapshot must stay out of
                            // _preExistingOverrides so SelectiveRevert can revert it.
                            bool isToolMadeOverride = preserveOverrides
                                && previousOverrides != null
                                && previousOverrides.ContainsKey(slot.identifier)
                                && !(previousPreExistingOverrides?.Contains(slot.identifier) ?? false);

                            if (!isToolMadeOverride)
                                _preExistingOverrides.Add(slot.identifier);

                            // Resolve the Prefab asset's original material and show the
                            // instance's current material as a pre-existing override.
                            var assetRenderer = PrefabUtility.GetCorrespondingObjectFromSource(renderer);
                            if (assetRenderer != null)
                            {
                                var assetMaterials = assetRenderer.sharedMaterials;
                                if (slot.identifier.slotIndex < assetMaterials.Length)
                                {
                                    var prefabOriginalMaterial = assetMaterials[slot.identifier.slotIndex];
                                    var instanceMaterial = slot.baseMaterial;

                                    // Only treat as override if the materials actually differ
                                    if (instanceMaterial != prefabOriginalMaterial)
                                    {
                                        slot.baseMaterial = prefabOriginalMaterial;
                                        _originalMaterials[slot.identifier] = prefabOriginalMaterial;
                                        _overrides[slot.identifier] = instanceMaterial;
                                        continue;
                                    }
                                }
                            }
                        }
                    }
                }

                _originalMaterials[slot.identifier] = slot.baseMaterial;
            }

            // Restore user-set overrides for slots that still exist.
            // Iterate the freshly-scanned identifiers so that, when a Hierarchy rename
            // or reparent has changed an identifier's rendererPath, the override is
            // re-keyed to the new identifier (with the up-to-date path) rather than
            // re-inserted under the stale one. Identifier equality is renderer-based
            // (rename-safe), so the lookup matches across the path change.
            if (previousOverrides != null)
            {
                foreach (var slot in _scannedSlots)
                {
                    if (_overrides.ContainsKey(slot.identifier)) continue;
                    if (previousOverrides.TryGetValue(slot.identifier, out var prevMat) && prevMat != null)
                        _overrides[slot.identifier] = prevMat;
                }
            }

            // Cache the current slot set for structural change detection
            _lastSlotKeys.Clear();
            foreach (var slot in _scannedSlots)
                _lastSlotKeys.Add(slot.identifier.GetLookupKey());
        }

        /// <summary>
        /// Checks whether the set of renderer slots has structurally changed
        /// (renderers added/removed, slot counts changed) compared to the last scan.
        /// Material-only changes are ignored.
        /// </summary>
        private bool HasSlotSetChanged()
        {
            if (_baseInstance == null) return false;

            var renderers = _baseInstance.GetComponentsInChildren<Renderer>(true);
            int currentCount = 0;
            foreach (var r in renderers)
                currentCount += r.sharedMaterials.Length;

            // Quick count check before building the full key set
            if (currentCount != _lastSlotKeys.Count) return true;

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                string path = PrefabScanner.GetRelativePathFromRoot(renderer.transform, _baseInstance.transform);
                int slotCount = renderer.sharedMaterials.Length;
                for (int i = 0; i < slotCount; i++)
                {
                    if (!_lastSlotKeys.Contains($"{path}|{i}"))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Walks up the prefab variant hierarchy from _basePrefabAsset to the root,
        /// building a list of all ancestors in root-first order.
        /// </summary>
        private void BuildAncestorChain()
        {
            _ancestorChain.Clear();
            _selectedVariantParent = _basePrefabAsset;

            if (_basePrefabAsset == null) return;

            var current = _basePrefabAsset;
            var visited = new HashSet<GameObject>();

            while (current != null && visited.Add(current))
            {
                _ancestorChain.Add(current);
                var parent = PrefabUtility.GetCorrespondingObjectFromSource(current);
                if (parent == null || parent == current) break;
                current = parent;
            }

            // Reverse to root-first order
            _ancestorChain.Reverse();
        }

        // ────────────────────────────────────────────────
        // Import
        // ────────────────────────────────────────────────

        private void OnApplyImport()
        {
            if (_basePrefabAsset == null)
            {
                EditorUtility.DisplayDialog(Localization.S("common.error"), Localization.S("creator.error.noBasePrefab"), "OK");
                return;
            }

            var sourcePrefab = _importPrefabField?.value as GameObject;
            if (sourcePrefab == null)
            {
                EditorUtility.DisplayDialog(Localization.S("common.error"), Localization.S("creator.error.noSourcePrefab"), "OK");
                return;
            }

            // Verify it's a prefab asset
            if (!PrefabUtility.IsPartOfPrefabAsset(sourcePrefab))
            {
                EditorUtility.DisplayDialog(Localization.S("common.error"), Localization.S("creator.error.notPrefabAsset"), "OK");
                return;
            }

            // Compare source and base renderers using 5-tier matching, then populate
            // overrides with material differences (source material → override on base slot)
            var sourceSlots = PrefabScanner.ScanRenderers(sourcePrefab);
            var results = RendererMatcher.CompareRenderers(sourceSlots, _scannedSlots);

            ResetPreview();
            _overrides.Clear();

            foreach (var result in results)
            {
                if (result.targetSlot == null || result.overrideMaterial == null) continue;
                _overrides[result.targetSlot] = result.overrideMaterial;
            }

            int matchCount = _overrides.Count;

            // Derive variant name from source prefab name
            _variantNameField.value = VariantAnalyzer.DeriveVariantName(_basePrefabAsset.name, sourcePrefab.name);

            // Apply preview for all overrides
            foreach (var kvp in _overrides)
            {
                if (kvp.Value != null)
                {
                    ApplyPreviewMaterial(kvp.Key, kvp.Value);
                }
            }

            RefreshAllUI();
            Debug.Log($"[Color Variant Generator] Imported {matchCount} material override(s) from '{sourcePrefab.name}'.");
        }
    }
}
