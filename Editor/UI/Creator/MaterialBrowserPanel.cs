using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kanameliser.ColorVariantGenerator
{
    /// <summary>
    /// A panel that displays materials from a root folder, grouped by subfolder with thumbnail grids.
    /// Materials can be dragged from this panel to material slot rows.
    /// </summary>
    internal class MaterialBrowserPanel : VisualElement
    {
        // Below this thumbnail size (px), switch from grid view to compact list view
        private const int ListModeThreshold = 16;
        private const float SliderMin = 0f;
        private const float SliderMax = 96f;
        private const float DefaultThumbnailSize = 48f;

        private TextField _rootFolderField;
        private Button _browseFolderButton;
        private ScrollView _materialListScroll;
        private Slider _viewSlider;
        private string _currentRootFolder;
        private float _thumbnailSize = DefaultThumbnailSize;
        private bool _dragInitiated;
        private Button _refreshButton;

        private bool IsListMode => _thumbnailSize < ListModeThreshold;

        /// <summary>
        /// Fired when a material is selected (clicked) in the browser.
        /// </summary>
        public event Action<Material> OnMaterialSelected;

        /// <summary>
        /// Fired when the refresh button is pressed, so the parent window can also refresh its previews.
        /// </summary>
        public event Action OnRefreshRequested;

        public MaterialBrowserPanel()
        {
            AddToClassList("material-browser-panel");
            BuildUI();
            RefreshMaterialList();

            // Refresh dynamic content (empty messages, folder headers) on language change
            Localization.RegisterLanguageChangeCallback(this, p =>
            {
                p.RefreshMaterialList();
                p._refreshButton.tooltip = Localization.S("browser.refresh.tooltip");
            });
        }

        private void BuildUI()
        {
            // Header
            var header = new Label("browser.title");
            header.AddToClassList("browser-header");
            header.AddToClassList("ndmf-tr");
            Add(header);

            // Folder selection row
            var folderRow = new VisualElement();
            folderRow.AddToClassList("browser-folder-row");

            var folderLabel = new Label("browser.root");
            folderLabel.AddToClassList("browser-folder-label");
            folderLabel.AddToClassList("ndmf-tr");
            folderRow.Add(folderLabel);

            _rootFolderField = new TextField();
            _rootFolderField.AddToClassList("browser-folder-field");
            _rootFolderField.RegisterValueChangedCallback(evt =>
            {
                _currentRootFolder = evt.newValue;
                RefreshMaterialList();
            });
            folderRow.Add(_rootFolderField);

            _browseFolderButton = new Button(OnBrowseFolder)
            {
                text = "..."
            };
            _browseFolderButton.AddToClassList("browser-browse-button");
            folderRow.Add(_browseFolderButton);

            // D&D: accept folder drops on the folder row
            EditorUIUtility.RegisterFolderDrop(folderRow, _rootFolderField);

            Add(folderRow);

            // Material list scroll area
            _materialListScroll = new ScrollView(ScrollViewMode.Vertical);
            _materialListScroll.AddToClassList("browser-scroll");
            Add(_materialListScroll);

            // Bottom toolbar with refresh button and view mode slider
            var toolbar = new VisualElement();
            toolbar.AddToClassList("browser-toolbar");

            _refreshButton = new Button(OnRefreshButtonClicked)
            {
                tooltip = Localization.S("browser.refresh.tooltip")
            };
            _refreshButton.AddToClassList("browser-refresh-button");

            var refreshIcon = EditorGUIUtility.IconContent("Refresh");
            if (refreshIcon?.image != null)
            {
                var icon = new Image { image = refreshIcon.image };
                icon.AddToClassList("browser-refresh-icon");
                _refreshButton.Add(icon);
            }
            else
            {
                _refreshButton.text = EditorUIUtility.Refresh;
            }

            toolbar.Add(_refreshButton);

            _viewSlider = new Slider(SliderMin, SliderMax);
            _viewSlider.value = DefaultThumbnailSize;
            _viewSlider.AddToClassList("browser-view-slider");
            _viewSlider.RegisterValueChangedCallback(evt =>
            {
                _thumbnailSize = evt.newValue;
                RefreshMaterialList();
            });
            toolbar.Add(_viewSlider);

            Add(toolbar);
        }

        /// <summary>
        /// Sets the root folder path and refreshes the material list.
        /// </summary>
        public void SetRootFolder(string folderPath)
        {
            _currentRootFolder = folderPath;
            _rootFolderField.SetValueWithoutNotify(folderPath);
            RefreshMaterialList();
        }

        private void OnBrowseFolder()
        {
            string defaultPath = _currentRootFolder;
            if (string.IsNullOrEmpty(defaultPath)) defaultPath = "Assets";

            string selectedPath = EditorUtility.OpenFolderPanel(Localization.S("browser.selectRootFolder"), defaultPath, "");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                _rootFolderField.value = EditorUIUtility.ToProjectRelativePath(selectedPath);
            }
        }

        private void RefreshMaterialList()
        {
            _materialListScroll.Clear();

            if (string.IsNullOrEmpty(_currentRootFolder) || !AssetDatabase.IsValidFolder(_currentRootFolder))
            {
                ShowEmptyMessage(Localization.S("browser.emptyNoFolder"));
                return;
            }

            // Collect materials grouped by subfolder
            var groupedMaterials = CollectMaterialsBySubfolder(_currentRootFolder);

            if (groupedMaterials.Count == 0)
            {
                ShowEmptyMessage(Localization.S("browser.emptyNoMaterials"));
                return;
            }

            bool listMode = IsListMode;

            foreach (var group in groupedMaterials.OrderBy(g => g.Key))
            {
                // Subfolder header
                string displayName = group.Key;
                if (string.IsNullOrEmpty(displayName)) displayName = Localization.S("browser.rootFolder");

                var folderHeader = new Label($"{EditorUIUtility.HorizontalRule} {displayName}");
                folderHeader.AddToClassList("browser-folder-header");
                _materialListScroll.Add(folderHeader);

                // Material container (list or grid depending on slider)
                var container = new VisualElement();
                container.AddToClassList(listMode ? "browser-list" : "browser-grid");

                foreach (var material in group.Value.OrderBy(m => m.name))
                {
                    var item = listMode
                        ? CreateMaterialListItem(material)
                        : CreateMaterialGridItem(material);
                    container.Add(item);
                }

                _materialListScroll.Add(container);
            }
        }

        /// <summary>
        /// Collects all .mat files under rootFolder, grouped by their containing subfolder
        /// (the deepest folder that directly holds each material).
        /// </summary>
        private static Dictionary<string, List<Material>> CollectMaterialsBySubfolder(string rootFolder)
        {
            var result = new Dictionary<string, List<Material>>();

            // Find all .mat files under root
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { rootFolder });

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                if (material == null) continue;

                // Determine the containing folder relative to root
                string relativePath = assetPath.Substring(rootFolder.Length).TrimStart('/');
                int lastSlashIndex = relativePath.LastIndexOf('/');
                string subFolder;
                if (lastSlashIndex >= 0)
                {
                    // Use the full parent directory path (e.g. "Materials/Hat")
                    subFolder = relativePath.Substring(0, lastSlashIndex);
                }
                else
                {
                    // Material is directly in root folder
                    subFolder = "";
                }

                if (!result.TryGetValue(subFolder, out var list))
                    result[subFolder] = list = new List<Material>();
                list.Add(material);
            }

            return result;
        }

        private VisualElement CreateMaterialGridItem(Material material)
        {
            int size = Mathf.RoundToInt(_thumbnailSize);

            var item = new VisualElement();
            item.AddToClassList("browser-material-item");
            item.tooltip = material.name;
            item.style.width = size + 8;

            // Store material reference for drag
            item.userData = material;

            // Thumbnail
            var thumbnail = new Image();
            thumbnail.AddToClassList("browser-thumbnail");
            thumbnail.style.width = size;
            thumbnail.style.height = size;

            EditorUIUtility.SetMaterialPreview(thumbnail, material);
            item.Add(thumbnail);

            // Material name
            var nameLabel = new Label(material.name);
            nameLabel.AddToClassList("browser-material-name");
            nameLabel.style.maxWidth = size + 8;
            nameLabel.style.fontSize = Mathf.Max(9, Mathf.RoundToInt(size * 0.19f));
            item.Add(nameLabel);

            RegisterMaterialInteraction(item, material);
            return item;
        }

        private VisualElement CreateMaterialListItem(Material material)
        {
            var item = new VisualElement();
            item.AddToClassList("browser-list-item");
            item.tooltip = material.name;

            item.userData = material;

            // Small thumbnail
            var thumbnail = new Image();
            thumbnail.AddToClassList("browser-list-thumbnail");

            EditorUIUtility.SetMaterialPreview(thumbnail, material);
            item.Add(thumbnail);

            // Material name
            var nameLabel = new Label(material.name);
            nameLabel.AddToClassList("browser-list-name");
            item.Add(nameLabel);

            RegisterMaterialInteraction(item, material);
            return item;
        }

        private void RegisterMaterialInteraction(VisualElement item, Material material)
        {
            // Track whether a drag was initiated to avoid selecting on drag
            item.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    _dragInitiated = false;
                }
            });

            // Drag support - start drag on mouse down + move
            item.RegisterCallback<MouseMoveEvent>(evt =>
            {
                if (evt.pressedButtons == 1 && !_dragInitiated)
                {
                    _dragInitiated = true;
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.objectReferences = new UnityEngine.Object[] { material };
                    DragAndDrop.StartDrag(material.name);
                    evt.StopPropagation();
                }
            });

            // Select only on mouse up without drag
            item.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (evt.button == 0 && !_dragInitiated)
                {
                    Selection.activeObject = material;
                    OnMaterialSelected?.Invoke(material);
                }
            });
        }

        /// <summary>
        /// Finds a material in the browser and highlights it for a few seconds.
        /// Also scrolls the item into view.
        /// </summary>
        public void HighlightMaterial(Material material)
        {
            if (material == null) return;

            // Find the item element whose userData matches the material
            var target = _materialListScroll.Query<VisualElement>()
                .Where(e => e.userData as Material == material)
                .First();

            if (target == null) return;

            // Scroll into view
            _materialListScroll.ScrollTo(target);

            // Add highlight class and remove after a delay
            target.AddToClassList("browser-item-highlight");
            target.schedule.Execute(() =>
            {
                target.RemoveFromClassList("browser-item-highlight");
            }).StartingIn(2000);
        }

        private void OnRefreshButtonClicked()
        {
            RefreshMaterialList();
            OnRefreshRequested?.Invoke();
        }

        private void ShowEmptyMessage(string message)
        {
            var label = new Label(message);
            label.AddToClassList("browser-empty-label");
            _materialListScroll.Add(label);
        }

    }
}
