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
        private HashSet<string> _lastSlotKeys = new HashSet<string>();

        // Bulk mode state
        private bool _bulkMode;
        private Dictionary<Material, bool> _bulkFoldoutState = new Dictionary<Material, bool>();
        private bool _bulkNullMaterialFoldout;
        private Dictionary<string, bool> _rendererFoldoutState = new Dictionary<string, bool>();
        private Button _normalModeButton;
        private Button _bulkModeButton;
        private List<GameObject> _ancestorChain = new List<GameObject>();
        private GameObject _selectedVariantParent;
        private VisualElement _parentDropdownContainer;
        private PopupField<GameObject> _parentDropdown;
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

            RestoreCreatorMode();
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
                if (w._parentDropdown != null)
                    w._parentDropdown.tooltip = Localization.S("creator.variantParent:tooltip");
                if (w._basePrefabWarningLabel.style.display == DisplayStyle.Flex && w._currentWarningKey != null)
                    w._basePrefabWarningLabel.text = Localization.S(w._currentWarningKey);
                if (w._normalModeButton != null)
                    w._normalModeButton.text = Localization.S("creator.materialSlots.mode.normal");
                if (w._bulkModeButton != null)
                    w._bulkModeButton.text = Localization.S("creator.materialSlots.mode.bulk");
                if (w._strictIndicatorLabel != null)
                {
                    w._strictIndicatorLabel.text = Localization.S("creator.strictIndicator");
                    w._strictIndicatorLabel.tooltip = Localization.S("creator.strictIndicator:tooltip");
                }
                if (w._includePropertyChangesToggle != null)
                {
                    w._includePropertyChangesToggle.label = Localization.S("creator.standard.includePropertyChanges");
                    w._includePropertyChangesToggle.tooltip = Localization.S("creator.standard.includePropertyChanges:tooltip");
                }
            });
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        }


        private void OnUndoRedoPerformed()
        {
            SyncOverridesFromRenderers();
            RefreshAllUI();
        }

        /// <summary>
        /// Rebuilds <see cref="_overrides"/> by comparing the current renderer materials
        /// against the <see cref="_originalMaterials"/> snapshot. Picks up any material
        /// change made outside this tool (Scene view drag-drop, Inspector edits, Undo/Redo).
        /// </summary>
        /// <returns>true if the override set actually changed.</returns>
        private bool SyncOverridesFromRenderers()
        {
            if (_baseInstance == null || _scannedSlots.Count == 0) return false;

            bool changed = false;
            int previousCount = _overrides.Count;

            // Build new override set and compare against existing
            var newOverrides = new Dictionary<MaterialSlotIdentifier, Material>();
            foreach (var slot in _scannedSlots)
            {
                var renderer = FindRenderer(slot.identifier);
                if (renderer == null) continue;

                var materials = renderer.sharedMaterials;
                if (slot.identifier.slotIndex < 0 || slot.identifier.slotIndex >= materials.Length) continue;

                var currentMat = materials[slot.identifier.slotIndex];
                if (_originalMaterials.TryGetValue(slot.identifier, out var originalMat) && currentMat != originalMat)
                {
                    newOverrides[slot.identifier] = currentMat;

                    // Detect if this is a new or changed override
                    if (!changed)
                    {
                        if (!_overrides.TryGetValue(slot.identifier, out var prev) || prev != currentMat)
                            changed = true;
                    }
                }
            }

            if (!changed && newOverrides.Count != previousCount)
                changed = true;

            if (changed)
                _overrides = newOverrides;

            return changed;
        }

        private void RefreshAllUI()
        {
            RefreshMaterialSlotsUI();
            UpdateOutputPreview();
            UpdateGenerateButtonState();
        }

        private void OnHierarchyChanged()
        {
            if (_baseInstance != null && _basePrefabAsset != null)
            {
                if (HasSlotSetChanged())
                {
                    // Structural change: renderers added/removed — full rescan needed
                    RescanMaterialSlots();
                    RefreshAllUI();
                }
                else if (SyncOverridesFromRenderers())
                {
                    // Material-only change detected — refresh UI
                    RefreshAllUI();
                }
            }
            RefreshStructuralChanges();
        }

        private void OnBrowserRefreshRequested()
        {
            if (_baseInstance != null && _basePrefabAsset != null)
            {
                RescanMaterialSlots();
            }
            RefreshStructuralChanges();
            RefreshAllUI();
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
