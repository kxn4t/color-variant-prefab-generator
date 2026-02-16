using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kanameliser.ColorVariantGenerator
{
    internal partial class CreatorWindow
    {
        // ────────────────────────────────────────────────
        // Section: Output Settings
        // ────────────────────────────────────────────────

        private void CreateOutputSection(VisualElement container)
        {
            var section = new VisualElement();
            section.AddToClassList("section-container");
            section.AddToClassList("output-section");

            var label = new Label("creator.outputSettings");
            label.AddToClassList("section-label");
            label.AddToClassList("ndmf-tr");
            section.Add(label);

            // Variant name
            var nameRow = new VisualElement();
            nameRow.AddToClassList("output-row");

            var nameLabel = new Label("creator.variantName");
            nameLabel.AddToClassList("output-label");
            nameLabel.AddToClassList("ndmf-tr");
            nameRow.Add(nameLabel);

            _variantNameField = new TextField();
            _variantNameField.AddToClassList("output-field");
            _variantNameField.RegisterValueChangedCallback(_ =>
            {
                UpdateOutputPreview();
                UpdateGenerateButtonState();
            });
            nameRow.Add(_variantNameField);

            section.Add(nameRow);

            // Output path (shared helper)
            section.Add(EditorUIUtility.CreateOutputPathRow(
                out _outputPathField, UpdateOutputPreview, GetDefaultOutputPath));

            // Naming template (shared helper)
            section.Add(EditorUIUtility.CreateNamingTemplateRow(
                out _namingTemplateField, UpdateOutputPreview));

            // Preview
            _outputPreviewLabel = new Label();
            _outputPreviewLabel.AddToClassList("output-preview-label");
            section.Add(_outputPreviewLabel);

            container.Add(section);
        }

        private string GetDefaultOutputPath()
        {
            return _basePrefabAsset != null
                ? Path.GetDirectoryName(AssetDatabase.GetAssetPath(_basePrefabAsset))
                : null;
        }

        private void UpdateOutputPreview()
        {
            if (_basePrefabAsset == null || string.IsNullOrEmpty(_variantNameField?.value))
            {
                if (_outputPreviewLabel != null)
                    _outputPreviewLabel.text = "";
                return;
            }

            string baseName = _basePrefabAsset.name;
            string variantName = _variantNameField.value;
            string template = _namingTemplateField?.value ?? EditorUIUtility.DefaultNamingTemplate;
            string fileName = EditorUIUtility.ResolveFileName(template, baseName, variantName);

            string outputPath = _outputPathField?.value ?? "";
            if (string.IsNullOrEmpty(outputPath))
            {
                outputPath = GetDefaultOutputPath() ?? "";
            }

            if (_outputPreviewLabel != null)
                _outputPreviewLabel.text = Localization.S("creator.output.preview", outputPath, fileName);
        }

        // ────────────────────────────────────────────────
        // Section: Actions
        // ────────────────────────────────────────────────

        private void CreateActionSection(VisualElement container)
        {
            // Main action buttons
            var section = new VisualElement();
            section.AddToClassList("action-section");

            // Split-button: Clear Overrides + revert mode dropdown
            var clearGroup = new VisualElement();
            clearGroup.AddToClassList("clear-overrides-group");

            _clearOverridesButton = new Button(OnClearOverrides)
            {
                tooltip = Localization.S("creator.clearOverrides.tooltip")
            };
            _clearOverridesButton.AddToClassList("clear-overrides-button");
            UpdateClearOverridesLabel();
            clearGroup.Add(_clearOverridesButton);

            _clearDropdownButton = new Button(OnClearOverridesDropdown)
            {
                text = EditorUIUtility.DropdownArrow,
                tooltip = Localization.S("creator.clearDropdown.tooltip")
            };
            _clearDropdownButton.AddToClassList("clear-overrides-dropdown");
            clearGroup.Add(_clearDropdownButton);

            section.Add(clearGroup);

            _generateButton = new Button(OnGenerateClicked)
            {
                text = "creator.generate"
            };
            _generateButton.AddToClassList("generate-button");
            _generateButton.AddToClassList("ndmf-tr");
            section.Add(_generateButton);

            container.Add(section);
        }

        private void OnGenerateClicked()
        {
            if (!ValidateCreatorInput(out string variantName))
                return;

            var overridesList = BuildOverridesList();
            if (overridesList == null)
                return;

            string namingTemplate = _namingTemplateField?.value;
            if (string.IsNullOrEmpty(namingTemplate))
                namingTemplate = EditorUIUtility.DefaultNamingTemplate;

            string outputPath = EditorUIUtility.ResolveAndValidateOutputPath(
                _outputPathField.value, GetDefaultOutputPath);
            if (outputPath == null)
                return;

            if (!EditorUIUtility.ConfirmSingleFileOverwrite(outputPath, namingTemplate, _basePrefabAsset.name, variantName))
                return;

            ExecuteGeneration(overridesList, variantName, outputPath, namingTemplate);
        }

        private bool ValidateCreatorInput(out string variantName)
        {
            variantName = null;

            if (_basePrefabAsset == null)
            {
                EditorUtility.DisplayDialog(Localization.S("common.error"), Localization.S("creator.error.noBasePrefab"), "OK");
                return false;
            }

            variantName = _variantNameField.value;
            if (string.IsNullOrEmpty(variantName))
            {
                EditorUtility.DisplayDialog(Localization.S("common.error"), Localization.S("creator.error.variantNameEmpty"), "OK");
                return false;
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            if (variantName.IndexOfAny(invalidChars) >= 0)
            {
                EditorUtility.DisplayDialog(Localization.S("common.error"), Localization.S("creator.error.variantNameInvalid"), "OK");
                return false;
            }

            return true;
        }

        private List<MaterialOverride> BuildOverridesList()
        {
            var overridesList = new List<MaterialOverride>();
            foreach (var kvp in _overrides)
            {
                if (kvp.Value != null)
                {
                    overridesList.Add(new MaterialOverride
                    {
                        slot = kvp.Key,
                        overrideMaterial = kvp.Value
                    });
                }
            }

            if (overridesList.Count == 0)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    Localization.S("common.warning"),
                    Localization.S("creator.warning.noOverrides"),
                    Localization.S("creator.generateAnyway"), Localization.S("common.cancel"));
                if (!proceed) return null;
            }

            return overridesList;
        }

        private void ExecuteGeneration(List<MaterialOverride> overridesList, string variantName, string outputPath, string namingTemplate)
        {
            var result = PrefabVariantGenerator.GenerateVariant(
                _basePrefabAsset, overridesList, variantName, outputPath, namingTemplate);

            if (result.success)
            {
                var generatedAsset = AssetDatabase.LoadAssetAtPath<GameObject>(result.path);
                if (generatedAsset != null)
                {
                    EditorGUIUtility.PingObject(generatedAsset);
                }

                var revertMode = (PreviewRevertMode)EditorPrefs.GetInt(PreviewRevertModeKey, 0);
                string revertModeName = revertMode switch
                {
                    PreviewRevertMode.SelectiveRevert => Localization.S("creator.revertMode.selectiveRevert"),
                    PreviewRevertMode.FullRevert => Localization.S("creator.revertMode.fullRevert"),
                    _ => Localization.S("creator.revertMode.visualOnly")
                };

                bool keep = EditorUtility.DisplayDialog(
                    Localization.S("creator.success"),
                    Localization.S("creator.success.message", result.path, revertModeName),
                    Localization.S("creator.success.keepOverrides"),
                    Localization.S("creator.success.clearOverrides"));

                if (!keep)
                {
                    _variantNameField.value = "";
                    ResetPreview();
                    _overrides.Clear();
                    RefreshAllUI();
                }
            }
            else
            {
                EditorUtility.DisplayDialog(
                    Localization.S("common.error"),
                    Localization.S("creator.error.generateFailed", result.errorMessage),
                    "OK");
            }
        }

        private void OnClearOverridesDropdown()
        {
            var current = (PreviewRevertMode)EditorPrefs.GetInt(PreviewRevertModeKey, 0);
            var menu = new GenericMenu();
            menu.AddItem(
                new GUIContent(Localization.S("creator.revertMode.visualOnly")),
                current == PreviewRevertMode.VisualOnly,
                () => { EditorPrefs.SetInt(PreviewRevertModeKey, (int)PreviewRevertMode.VisualOnly); UpdateClearOverridesLabel(); });
            menu.AddItem(
                new GUIContent(Localization.S("creator.revertMode.selectiveRevert")),
                current == PreviewRevertMode.SelectiveRevert,
                () => { EditorPrefs.SetInt(PreviewRevertModeKey, (int)PreviewRevertMode.SelectiveRevert); UpdateClearOverridesLabel(); });
            menu.AddItem(
                new GUIContent(Localization.S("creator.revertMode.fullRevert")),
                current == PreviewRevertMode.FullRevert,
                () => { EditorPrefs.SetInt(PreviewRevertModeKey, (int)PreviewRevertMode.FullRevert); UpdateClearOverridesLabel(); });
            menu.ShowAsContext();
        }

        private void UpdateClearOverridesLabel()
        {
            if (_clearOverridesButton == null) return;

            var mode = (PreviewRevertMode)EditorPrefs.GetInt(PreviewRevertModeKey, 0);
            _clearOverridesButton.text = mode switch
            {
                PreviewRevertMode.SelectiveRevert => Localization.S("creator.clearButton.selectiveRevert"),
                PreviewRevertMode.FullRevert => Localization.S("creator.clearButton.fullRevert"),
                _ => Localization.S("creator.clearButton.default")
            };
            _clearOverridesButton.tooltip = Localization.S("creator.clearOverrides.tooltip");
        }

        private void UpdateGenerateButtonState()
        {
            if (_generateButton == null) return;

            bool canGenerate = _basePrefabAsset != null
                && !string.IsNullOrEmpty(_variantNameField?.value);

            _generateButton.SetEnabled(canGenerate);
        }
    }
}
