using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kanameliser.ColorVariantGenerator
{
    /// <summary>
    /// Editor window for defining and creating color variant Prefab Variants one at a time.
    /// Supports Scene view preview by temporarily applying materials to the Hierarchy instance.
    /// </summary>
    internal partial class CreatorWindow : EditorWindow
    {
        private ObjectField _basePrefabField;
        private ScrollView _materialSlotsScroll;
        private TextField _variantNameField;
        private TextField _outputPathField;
        private TextField _namingTemplateField;
        private Label _outputPreviewLabel;
        private Button _generateButton;
        private Button _clearOverridesButton;

        private Label _slotsInfoLabel;
        private MaterialBrowserPanel _materialBrowser;
        private VisualElement _importSection;
        private ObjectField _importPrefabField;
        private Button _importApplyButton;
        private Label _importMessageLabel;
        private Label _basePrefabWarningLabel;
        private Button _optionsButton;
        private Button _clearDropdownButton;
        private string _currentWarningKey;

        private GameObject _baseInstance;
        private GameObject _basePrefabAsset;
        private List<ScannedMaterialSlot> _scannedSlots = new List<ScannedMaterialSlot>();
        private Dictionary<MaterialSlotIdentifier, Material> _overrides = new Dictionary<MaterialSlotIdentifier, Material>();
        private Dictionary<MaterialSlotIdentifier, Material> _originalMaterials = new Dictionary<MaterialSlotIdentifier, Material>();
        private Dictionary<MaterialSlotIdentifier, ObjectField> _slotObjectFields = new Dictionary<MaterialSlotIdentifier, ObjectField>();
        private bool _previewActive;
        private HashSet<MaterialSlotIdentifier> _preExistingOverrides = new HashSet<MaterialSlotIdentifier>();
        private const string PreviewRevertModeKey = "ColorVariantGenerator.PreviewRevertMode";

        [MenuItem("Tools/Color Variant Prefab Generator/Creator", priority = 1000)]
        public static void ShowWindow()
        {
            var window = GetWindow<CreatorWindow>();
            window.titleContent = new GUIContent("CV Creator");
            window.minSize = new Vector2(800, 500);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/net.kanameliser.color-variant-generator/Editor/UI/Creator/CreatorWindow.uss");
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            // Split layout: left = editor, right = material browser
            // Start with a temporary value; will be adjusted to 50% on first layout
            var splitContainer = new TwoPaneSplitView(0, 300, TwoPaneSplitViewOrientation.Horizontal);
            splitContainer.AddToClassList("split-container");
            root.Add(splitContainer);

            // Set initial split to 50/50 once the layout width is known
            EventCallback<GeometryChangedEvent> onFirstLayout = null;
            onFirstLayout = evt =>
            {
                if (evt.newRect.width > 0)
                {
                    splitContainer.fixedPaneInitialDimension = evt.newRect.width * 0.5f;
                    splitContainer.UnregisterCallback(onFirstLayout);
                }
            };
            splitContainer.RegisterCallback(onFirstLayout);

            // Left pane: main editor (scrollable)
            var leftPane = new ScrollView(ScrollViewMode.Vertical);
            leftPane.AddToClassList("main-container");

            // Language switcher
            var langSwitcher = new IMGUIContainer(Localization.ShowLanguageUI);
            langSwitcher.AddToClassList("language-switcher");
            leftPane.Add(langSwitcher);

            CreateBasePrefabSection(leftPane);
            CreateMaterialSlotsSection(leftPane);
            CreateOutputSection(leftPane);
            CreateActionSection(leftPane);

            splitContainer.Add(leftPane);

            // Right pane: material browser
            var rightPane = new VisualElement();
            rightPane.AddToClassList("browser-pane");

            _materialBrowser = new MaterialBrowserPanel();
            _materialBrowser.OnRefreshRequested += OnBrowserRefreshRequested;
            rightPane.Add(_materialBrowser);

            splitContainer.Add(rightPane);

            UpdateGenerateButtonState();

            // Localization: auto-translate ndmf-tr elements + apply language-specific fonts
            Localization.LocalizeUIElements(root);

            // Localization: register callback for dynamic content
            Localization.RegisterLanguageChangeCallback(this, w =>
            {
                w.RefreshMaterialSlotsUI();
                w.UpdateOutputPreview();
                w.UpdateClearOverridesLabel();
                w._basePrefabField.tooltip = Localization.S("creator.basePrefab:tooltip");
                w._importPrefabField.tooltip = Localization.S("creator.importField.tooltip");
                w._optionsButton.tooltip = Localization.S("creator.optionsButton.tooltip");
                w._clearDropdownButton.tooltip = Localization.S("creator.clearDropdown.tooltip");
                if (w._basePrefabWarningLabel.style.display == DisplayStyle.Flex && w._currentWarningKey != null)
                    w._basePrefabWarningLabel.text = Localization.S(w._currentWarningKey);
            });
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }


        private void OnUndoRedoPerformed()
        {
            if (_baseInstance == null || _scannedSlots.Count == 0) return;

            // Undo/redo may have changed renderer materials directly, so rebuild
            // _overrides by comparing current renderer state against the original snapshot
            _overrides.Clear();
            foreach (var slot in _scannedSlots)
            {
                var renderer = FindRenderer(slot.identifier);
                if (renderer == null) continue;

                var materials = renderer.sharedMaterials;
                if (slot.identifier.slotIndex < 0 || slot.identifier.slotIndex >= materials.Length) continue;

                var currentMat = materials[slot.identifier.slotIndex];
                if (_originalMaterials.TryGetValue(slot.identifier, out var originalMat) && currentMat != originalMat)
                {
                    _overrides[slot.identifier] = currentMat;
                }
            }

            RefreshAllUI();
        }

        private void RefreshAllUI()
        {
            RefreshMaterialSlotsUI();
            UpdateOutputPreview();
            UpdateGenerateButtonState();
        }

        private void OnBrowserRefreshRequested()
        {
            RefreshMaterialSlotsUI();
        }

        private void UpdateImportSectionState()
        {
            bool hasBase = _basePrefabAsset != null;

            if (_importPrefabField != null)
                _importPrefabField.SetEnabled(hasBase);
            if (_importApplyButton != null)
                _importApplyButton.SetEnabled(hasBase);
            if (_importMessageLabel != null)
                _importMessageLabel.style.display = hasBase ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }
}
