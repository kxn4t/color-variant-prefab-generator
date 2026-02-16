using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kanameliser.ColorVariantGenerator
{
    /// <summary>
    /// Batch generator window: takes source Prefabs with different materials,
    /// compares them against a base Prefab, and generates Prefab Variants in bulk
    /// with only the differing materials as overrides.
    /// </summary>
    internal partial class BatchGeneratorWindow : EditorWindow
    {
        private ObjectField _newBasePrefabField;
        private VisualElement _variantListSection;
        private VisualElement _variantListContainer;
        private Label _variantListPlaceholder;
        private Button _addVariantButton;
        private VisualElement _matchResultsContainer;
        private Label _matchEmptyLabel;
        private Foldout _matchResultsFoldout;
        private TextField _outputPathField;
        private TextField _namingTemplateField;
        private Label _outputPreviewLabel;
        private Button _generateButton;
        private ScrollView _mainScroll;

        private GameObject _newBasePrefab;
        private List<ScannedMaterialSlot> _newBaseSlots = new List<ScannedMaterialSlot>();
        private List<VariantEntry> _variantEntries = new List<VariantEntry>();
        private List<RendererMatchResult> _allMatchResults = new List<RendererMatchResult>();
        private bool _allowEmptyOverrides;

        /// <summary>
        /// Holds per-variant state: the source prefab, its match results, and naming info.
        /// customVariantName takes priority over autoVariantName (derived from the prefab name).
        /// </summary>
        private class VariantEntry
        {
            public GameObject variantPrefab;
            public List<RendererMatchResult> matchResults;
            public string customVariantName;
            public bool isNameManuallyEdited;
            public string autoVariantName;

            public string EffectiveVariantName =>
                !string.IsNullOrEmpty(customVariantName) ? customVariantName
                : autoVariantName ?? "";
        }

        [MenuItem("Tools/Color Variant Prefab Generator/Batch Generator", priority = 1001)]
        public static void ShowWindow()
        {
            var window = GetWindow<BatchGeneratorWindow>();
            window.titleContent = new GUIContent("CV Batch Generator");
            window.minSize = new Vector2(700, 500);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/net.kanameliser.color-variant-generator/Editor/UI/BatchGenerator/BatchGeneratorWindow.uss");
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            _mainScroll = new ScrollView(ScrollViewMode.Vertical);
            _mainScroll.AddToClassList("main-container");
            root.Add(_mainScroll);

            // Language switcher at top
            var langSwitcher = new IMGUIContainer(Localization.ShowLanguageUI);
            langSwitcher.AddToClassList("language-switcher");
            _mainScroll.Add(langSwitcher);

            CreateNewBaseSection(_mainScroll);
            CreateVariantListSection(_mainScroll);
            CreateMatchResultsSection(_mainScroll);
            CreateOutputSection(_mainScroll);
            CreateActionSection(_mainScroll);

            UpdateGenerateButtonState();

            // Localize ndmf-tr elements
            Localization.LocalizeUIElements(root);

            // Update dynamic text on language change
            Localization.RegisterLanguageChangeCallback(this, w =>
            {
                w.RefreshMatchResultsUI();
                w.UpdateOutputPreview();
                w.UpdateVariantOverrideLabels();
                w.UpdateVariantRowTooltips();
                w._newBasePrefabField.tooltip = Localization.S("batch.basePrefabField.tooltip");
            });
        }

        // ────────────────────────────────────────────────
        // Section: New Base Prefab
        // ────────────────────────────────────────────────

        private void CreateNewBaseSection(VisualElement container)
        {
            var section = new VisualElement();
            section.AddToClassList("section-container");

            var label = new Label("batch.newBasePrefab");
            label.AddToClassList("section-label");
            label.AddToClassList("ndmf-tr");
            section.Add(label);

            var helpBox = new Label("batch.newBasePrefab.help");
            helpBox.AddToClassList("info-box");
            helpBox.AddToClassList("ndmf-tr");
            section.Add(helpBox);

            var row = new VisualElement();
            row.AddToClassList("base-prefab-row");

            _newBasePrefabField = new ObjectField
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false,
                tooltip = Localization.S("batch.basePrefabField.tooltip")
            };
            _newBasePrefabField.AddToClassList("base-prefab-field");
            _newBasePrefabField.RegisterValueChangedCallback(OnNewBasePrefabChanged);
            row.Add(_newBasePrefabField);

            section.Add(row);
            container.Add(section);
        }

        private void OnNewBasePrefabChanged(ChangeEvent<Object> evt)
        {
            _newBasePrefab = evt.newValue as GameObject;
            _newBaseSlots.Clear();

            if (_newBasePrefab != null)
            {
                // Verify it's a prefab asset (not a scene object)
                if (!PrefabUtility.IsPartOfPrefabAsset(_newBasePrefab))
                {
                    Debug.LogWarning("[Color Variant Generator] Selected object is not a Prefab asset.");
                    _newBasePrefab = null;
                }
                else
                {
                    _newBaseSlots = PrefabScanner.ScanRenderers(_newBasePrefab);
                    Debug.Log($"[Color Variant Generator] New base '{_newBasePrefab.name}': {_newBaseSlots.Count} material slots scanned.");
                }
            }

            // Re-run matching for all existing variants
            RunAllMatching();
            UpdateGenerateButtonState();
        }

        // ────────────────────────────────────────────────
        // Shared Utilities
        // ────────────────────────────────────────────────

        private void UpdateGenerateButtonState()
        {
            if (_generateButton == null) return;

            // Require: base prefab set, at least one valid variant,
            // and either at least one matched slot or empty-override mode enabled
            bool hasValidEntry = _variantEntries.Any(e => e.variantPrefab != null && e.matchResults != null);
            bool hasMatchedSlot = _allMatchResults.Any(r => r.targetSlot != null);
            bool canGenerate = _newBasePrefab != null
                && hasValidEntry
                && (hasMatchedSlot || _allowEmptyOverrides);

            _generateButton.SetEnabled(canGenerate);
        }

        private static void RemoveLastChildBottomBorder(VisualElement container)
        {
            for (int i = 0; i < container.childCount; i++)
            {
                container[i].style.borderBottomWidth = i == container.childCount - 1 ? 0 : 1;
            }
        }
    }
}
