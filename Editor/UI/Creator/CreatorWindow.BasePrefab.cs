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

            var label = new Label("creator.basePrefab");
            label.AddToClassList("section-label");
            label.AddToClassList("ndmf-tr");
            section.Add(label);

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

            _baseInstance = evt.newValue as GameObject;
            _basePrefabAsset = null;
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
                }
            }

            // Update import section enabled state
            UpdateImportSectionState();

            RefreshAllUI();
        }

        private void ScanMaterialSlots()
        {
            if (_baseInstance == null) return;

            _scannedSlots = PrefabScanner.ScanRenderers(_baseInstance);

            // Store original materials for preview reset
            _originalMaterials.Clear();
            _preExistingOverrides.Clear();

            bool isPrefabInstance = PrefabUtility.IsPartOfPrefabInstance(_baseInstance);

            foreach (var slot in _scannedSlots)
            {
                _originalMaterials[slot.identifier] = slot.baseMaterial;

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
                            _preExistingOverrides.Add(slot.identifier);
                        }
                    }
                }
            }
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

            // Compare source and base renderers using 4-tier matching, then populate
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
