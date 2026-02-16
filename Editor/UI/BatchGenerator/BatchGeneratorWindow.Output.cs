using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kanameliser.ColorVariantGenerator
{
    internal partial class BatchGeneratorWindow
    {
        // ────────────────────────────────────────────────
        // Section: Output Settings
        // ────────────────────────────────────────────────

        private void CreateOutputSection(VisualElement container)
        {
            var section = new VisualElement();
            section.AddToClassList("section-container");
            section.AddToClassList("output-section");

            var label = new Label("batch.outputSettings");
            label.AddToClassList("section-label");
            label.AddToClassList("ndmf-tr");
            section.Add(label);

            // Output path (shared helper)
            section.Add(EditorUIUtility.CreateOutputPathRow(
                out _outputPathField, UpdateOutputPreview, GetDefaultOutputPath));

            // Naming template (shared helper)
            section.Add(EditorUIUtility.CreateNamingTemplateRow(
                out _namingTemplateField, UpdateOutputPreview));

            // Allow empty overrides toggle
            section.Add(CreateAllowEmptyToggleRow());

            // Preview
            _outputPreviewLabel = new Label();
            _outputPreviewLabel.AddToClassList("output-preview-label");
            section.Add(_outputPreviewLabel);

            container.Add(section);
        }

        private VisualElement CreateAllowEmptyToggleRow()
        {
            var row = new VisualElement();
            row.AddToClassList("output-row");

            var rowLabel = new Label("batch.options");
            rowLabel.AddToClassList("output-label");
            rowLabel.AddToClassList("ndmf-tr");
            row.Add(rowLabel);

            var toggle = new Toggle();
            toggle.labelElement.style.display = DisplayStyle.None;
            toggle.style.flexShrink = 0;
            toggle.RegisterValueChangedCallback(evt =>
            {
                _allowEmptyOverrides = evt.newValue;
                UpdateGenerateButtonState();
                UpdateOutputPreview();
            });
            row.Add(toggle);

            var toggleLabel = new Label("batch.allowEmptyOverrides");
            toggleLabel.AddToClassList("ndmf-tr");
            toggleLabel.RegisterCallback<ClickEvent>(_ => toggle.value = !toggle.value);
            row.Add(toggleLabel);

            return row;
        }

        private string GetDefaultOutputPath()
        {
            return _newBasePrefab != null
                ? Path.GetDirectoryName(AssetDatabase.GetAssetPath(_newBasePrefab))
                : null;
        }

        private void UpdateOutputPreview()
        {
            if (_newBasePrefab == null || _outputPreviewLabel == null) return;

            string baseName = _newBasePrefab.name;
            string template = _namingTemplateField?.value ?? EditorUIUtility.DefaultNamingTemplate;
            string outputPath = GetOutputPath();

            var lines = new List<string>();
            var validEntries = _variantEntries
                .Where(e => e.variantPrefab != null && e.matchResults != null).ToList();
            var variantNames = validEntries.Select(e => e.EffectiveVariantName).ToList();
            var dedupedNames = EditorUIUtility.DeduplicateVariantNames(variantNames);
            for (int i = 0; i < validEntries.Count; i++)
            {
                string fileName = EditorUIUtility.ResolveFileName(template, baseName, dedupedNames[i]);
                lines.Add($"  {outputPath}/{fileName}.prefab");
            }

            if (lines.Count > 0)
            {
                _outputPreviewLabel.text = Localization.S("batch.output.preview") + "\n" + string.Join("\n", lines);
            }
            else
            {
                _outputPreviewLabel.text = "";
            }
        }

        private string GetOutputPath()
        {
            string outputPath = _outputPathField?.value ?? "";
            if (string.IsNullOrEmpty(outputPath))
            {
                outputPath = GetDefaultOutputPath() ?? "";
            }
            return outputPath;
        }

        // ────────────────────────────────────────────────
        // Section: Actions
        // ────────────────────────────────────────────────

        private void CreateActionSection(VisualElement container)
        {
            var section = new VisualElement();
            section.AddToClassList("action-section");

            _generateButton = new Button(OnGenerateClicked)
            {
                text = "batch.generate"
            };
            _generateButton.AddToClassList("generate-button");
            _generateButton.AddToClassList("ndmf-tr");
            section.Add(_generateButton);

            container.Add(section);
        }

        private void OnGenerateClicked()
        {
            // Pipeline: validate input → resolve path → build overrides → confirm overwrites → execute
            if (!ValidateBatchInput(out var validEntries))
                return;

            string outputPath = EditorUIUtility.ResolveAndValidateOutputPath(
                _outputPathField?.value, GetDefaultOutputPath);
            if (outputPath == null)
                return;

            string namingTemplate = _namingTemplateField?.value ?? EditorUIUtility.DefaultNamingTemplate;

            var variants = BuildBatchOverrides(validEntries);
            if (variants == null)
                return;

            if (!ConfirmBatchFileOverwrites(variants, outputPath, namingTemplate))
                return;

            ExecuteBatchGeneration(variants, outputPath, namingTemplate);
        }

        private bool ValidateBatchInput(out List<VariantEntry> validEntries)
        {
            validEntries = null;

            if (_newBasePrefab == null)
            {
                EditorUtility.DisplayDialog(Localization.S("common.error"), Localization.S("batch.error.noBasePrefab"), "OK");
                return false;
            }

            // Only entries with both a prefab and match results are considered valid
            validEntries = _variantEntries.Where(e => e.variantPrefab != null && e.matchResults != null).ToList();
            if (validEntries.Count == 0)
            {
                EditorUtility.DisplayDialog(Localization.S("common.error"), Localization.S("batch.error.noValidSources"), "OK");
                return false;
            }

            // Warn about unmatched slots and let the user decide whether to proceed
            int unmatched = _allMatchResults.Count(r => r.targetSlot == null);
            if (unmatched > 0)
            {
                bool proceed = EditorUtility.DisplayDialog(
                    Localization.S("common.warning"),
                    Localization.S("batch.warning.unmatchedSlots", unmatched),
                    Localization.S("common.continue"), Localization.S("common.cancel"));
                if (!proceed) return false;
            }

            return true;
        }

        private List<(string variantName, List<MaterialOverride> overrides)> BuildBatchOverrides(List<VariantEntry> validEntries)
        {
            var variants = new List<(string variantName, List<MaterialOverride> overrides)>();

            foreach (var entry in validEntries)
            {
                var overrides = new List<MaterialOverride>();

                // Skip unmatched slots and slots with no override material
                foreach (var match in entry.matchResults)
                {
                    if (match.targetSlot == null) continue;
                    if (match.overrideMaterial == null) continue;

                    overrides.Add(new MaterialOverride
                    {
                        slot = match.targetSlot,
                        overrideMaterial = match.overrideMaterial
                    });
                }

                if (overrides.Count > 0 || _allowEmptyOverrides)
                {
                    variants.Add((entry.EffectiveVariantName, overrides));
                }
            }

            if (variants.Count == 0)
            {
                EditorUtility.DisplayDialog(Localization.S("common.error"), Localization.S("batch.error.noOverrides"), "OK");
                return null;
            }

            // Deduplicate variant names: first occurrence keeps its name, subsequent get _2, _3, etc.
            var names = variants.Select(v => v.variantName).ToList();
            var deduped = EditorUIUtility.DeduplicateVariantNames(names);
            variants = deduped.Select((n, i) => (n, variants[i].overrides)).ToList();

            return variants;
        }

        private bool ConfirmBatchFileOverwrites(
            List<(string variantName, List<MaterialOverride> overrides)> variants,
            string outputPath, string namingTemplate)
        {
            string baseName = _newBasePrefab.name;
            var existingFiles = new List<string>();
            foreach (var (variantName, _) in variants)
            {
                string fileName = EditorUIUtility.ResolveFileName(namingTemplate, baseName, variantName);
                string fullPath = EditorUIUtility.NormalizePath(
                    Path.Combine(outputPath, fileName + ".prefab"));
                if (File.Exists(fullPath))
                {
                    existingFiles.Add(fileName + ".prefab");
                }
            }

            if (existingFiles.Count > 0)
            {
                return EditorUtility.DisplayDialog(
                    Localization.S("common.filesExist"),
                    Localization.S("common.filesExist.message", string.Join("\n", existingFiles)),
                    Localization.S("common.overwriteAll"), Localization.S("common.cancel"));
            }

            return true;
        }

        private void ExecuteBatchGeneration(
            List<(string variantName, List<MaterialOverride> overrides)> variants,
            string outputPath, string namingTemplate)
        {
            var results = PrefabVariantGenerator.GenerateVariantsBatch(
                _newBasePrefab, variants, outputPath, namingTemplate);

            int successCount = results.Count(r => r.success);
            int failCount = results.Count(r => !r.success);

            string message;
            if (failCount > 0)
            {
                var errors = results.Where(r => !r.success)
                    .Select(r => $"  {r.variantName}: {r.errorMessage}");
                message = Localization.S("batch.result.messageWithErrors", successCount, failCount, string.Join("\n", errors));
            }
            else
            {
                message = Localization.S("batch.result.message", successCount, failCount);
            }

            EditorUtility.DisplayDialog(Localization.S("batch.result"), message, "OK");

            // Highlight the first successfully generated asset in the Project window
            if (successCount > 0)
            {
                var firstSuccess = results.FirstOrDefault(r => r.success);
                if (firstSuccess != null)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(firstSuccess.path);
                    if (asset != null) EditorGUIUtility.PingObject(asset);
                }
            }
        }
    }
}
