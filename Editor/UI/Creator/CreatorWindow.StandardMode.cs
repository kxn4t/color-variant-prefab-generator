using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kanameliser.ColorVariantGenerator
{
    internal partial class CreatorWindow
    {
        // ────────────────────────────────────────────────
        // Standard / Strict Mode state
        // ────────────────────────────────────────────────

        private Toggle _includePropertyChangesToggle;
        private VisualElement _standardOptionsContainer;
        private Label _strictIndicatorLabel;

        private StructuralChangeSummary _structuralSummary;

        private const string CreatorModeKey = "ColorVariantGenerator.CreatorMode";
        private const string IncludePropertyChangesKey = "ColorVariantGenerator.IncludePropertyChanges";

        private CreatorMode _creatorMode = CreatorMode.Standard;

        /// <summary>
        /// Restores persisted mode. Called early in CreateGUI.
        /// </summary>
        private void RestoreCreatorMode()
        {
            _creatorMode = (CreatorMode)EditorPrefs.GetInt(CreatorModeKey, (int)CreatorMode.Standard);
        }

        /// <summary>
        /// Builds StandardModeOptions from the current EditorPrefs state.
        /// EditorPrefs is the single source of truth for these options.
        /// </summary>
        private static StandardModeOptions BuildStandardModeOptions()
        {
            return new StandardModeOptions
            {
                includePropertyChanges = EditorPrefs.GetBool(IncludePropertyChangesKey, false)
            };
        }

        /// <summary>
        /// Creates the Standard mode option toggle inside the Output section.
        /// Shows "Include Transform/component changes" checkbox.
        /// Hidden in Strict mode.
        /// </summary>
        private void CreateStandardModeOptions(VisualElement outputSection)
        {
            _standardOptionsContainer = new VisualElement();
            _standardOptionsContainer.AddToClassList("standard-options-container");
            _standardOptionsContainer.style.display = _creatorMode == CreatorMode.Standard
                ? DisplayStyle.Flex : DisplayStyle.None;

            _includePropertyChangesToggle = new Toggle(
                Localization.S("creator.standard.includePropertyChanges"));
            _includePropertyChangesToggle.value = EditorPrefs.GetBool(IncludePropertyChangesKey, false);
            _includePropertyChangesToggle.tooltip =
                Localization.S("creator.standard.includePropertyChanges:tooltip");
            _includePropertyChangesToggle.AddToClassList("standard-option-toggle");
            _includePropertyChangesToggle.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.SetBool(IncludePropertyChangesKey, evt.newValue);
            });
            _standardOptionsContainer.Add(_includePropertyChangesToggle);

            outputSection.Add(_standardOptionsContainer);
        }

        /// <summary>
        /// Rescans structural changes from the hierarchy instance.
        /// Called on base prefab change, hierarchy change, and manual refresh.
        /// </summary>
        private void RefreshStructuralChanges()
        {
            if (_creatorMode != CreatorMode.Standard) return;
            if (_baseInstance == null || _basePrefabAsset == null)
            {
                _structuralSummary = null;
                return;
            }

            _structuralSummary = PrefabModificationHelper.AnalyzeStructuralChanges(_baseInstance);
        }

        private void SetCreatorMode(CreatorMode mode)
        {
            _creatorMode = mode;
            EditorPrefs.SetInt(CreatorModeKey, (int)mode);

            // Toggle options visibility
            if (_standardOptionsContainer != null)
                _standardOptionsContainer.style.display = mode == CreatorMode.Standard
                    ? DisplayStyle.Flex : DisplayStyle.None;

            if (_strictIndicatorLabel != null)
                _strictIndicatorLabel.style.display = mode == CreatorMode.Strict
                    ? DisplayStyle.Flex : DisplayStyle.None;

            // Refresh structural changes when switching to Standard mode
            if (mode == CreatorMode.Standard)
                RefreshStructuralChanges();

            RefreshMaterialSlotsUI();
        }
    }
}
