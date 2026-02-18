using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kanameliser.ColorVariantGenerator
{
    internal partial class CreatorWindow
    {
        // ────────────────────────────────────────────────
        // Section: Material Slots
        // ────────────────────────────────────────────────

        private void CreateMaterialSlotsSection(VisualElement container)
        {
            var section = new VisualElement();
            section.AddToClassList("section-container");
            section.AddToClassList("material-slots-section");

            // Header row: label + mode toggle
            var headerRow = new VisualElement();
            headerRow.AddToClassList("material-slots-header-row");

            _slotsInfoLabel = new Label(Localization.S("creator.materialSlots"));
            _slotsInfoLabel.AddToClassList("section-label");
            headerRow.Add(_slotsInfoLabel);

            // Mode toggle: Normal | Bulk
            var modeToggle = new VisualElement();
            modeToggle.AddToClassList("mode-toggle-group");

            _normalModeButton = new Button(() => SetBulkMode(false))
            {
                text = Localization.S("creator.materialSlots.mode.normal")
            };
            _normalModeButton.AddToClassList("mode-toggle-button");
            _normalModeButton.AddToClassList("mode-toggle-button--active");
            modeToggle.Add(_normalModeButton);

            _bulkModeButton = new Button(() => SetBulkMode(true))
            {
                text = Localization.S("creator.materialSlots.mode.bulk")
            };
            _bulkModeButton.AddToClassList("mode-toggle-button");
            modeToggle.Add(_bulkModeButton);

            headerRow.Add(modeToggle);
            section.Add(headerRow);

            _materialSlotsScroll = new ScrollView(ScrollViewMode.Vertical);
            _materialSlotsScroll.AddToClassList("material-slots-scroll");
            section.Add(_materialSlotsScroll);

            container.Add(section);
        }

        private void SetBulkMode(bool bulk)
        {
            if (_bulkMode == bulk) return;
            _bulkMode = bulk;

            if (bulk)
            {
                _normalModeButton.RemoveFromClassList("mode-toggle-button--active");
                _bulkModeButton.AddToClassList("mode-toggle-button--active");
            }
            else
            {
                _bulkModeButton.RemoveFromClassList("mode-toggle-button--active");
                _normalModeButton.AddToClassList("mode-toggle-button--active");
            }

            RefreshMaterialSlotsUI();
        }

        // ────────────────────────────────────────────────
        // Refresh: Dispatch
        // ────────────────────────────────────────────────

        private void RefreshMaterialSlotsUI()
        {
            _materialSlotsScroll.Clear();
            _slotObjectFields.Clear();

            if (_scannedSlots.Count == 0)
            {
                _slotsInfoLabel.text = Localization.S("creator.materialSlots");
                if (_basePrefabAsset != null)
                {
                    var warning = new Label(Localization.S("creator.materialSlots.noRenderers"));
                    warning.AddToClassList("base-prefab-warning");
                    _materialSlotsScroll.Add(warning);
                }
                return;
            }

            if (_bulkMode)
            {
                RefreshBulkModeUI();
            }
            else
            {
                RefreshNormalModeUI();
            }
        }

        // ────────────────────────────────────────────────
        // Normal Mode
        // ────────────────────────────────────────────────

        private void RefreshNormalModeUI()
        {
            var groupedSlots = _scannedSlots
                .GroupBy(s => s.identifier.rendererPath)
                .OrderBy(g => g.Key)
                .ToList();

            _slotsInfoLabel.text = Localization.S("creator.materialSlots.info", groupedSlots.Count, _scannedSlots.Count);

            // Alt+click: toggle all collapsible groups at once
            Action<bool> toggleAll = open =>
            {
                foreach (var g in groupedSlots)
                    _rendererFoldoutState[g.Key] = open;
                SetAllCollapsibleStates(_materialSlotsScroll, open);
            };

            foreach (var group in groupedSlots)
            {
                var firstSlot = group.First();
                var rendererPath = firstSlot.identifier.rendererPath;

                bool defaultOpen = !_rendererFoldoutState.TryGetValue(rendererPath, out var savedState) || savedState;

                var (headerContent, content, collapsible) = CreateCollapsibleGroup(
                    defaultOpen,
                    isOpen => _rendererFoldoutState[rendererPath] = isOpen,
                    toggleAll);

                // Renderer icon
                var iconContent = GetRendererIcon(firstSlot.identifier.rendererType);
                if (iconContent != null)
                {
                    var icon = new Image { image = iconContent };
                    icon.AddToClassList("renderer-icon");
                    headerContent.Add(icon);
                }

                // Renderer name
                var nameLabel = new Label(firstSlot.identifier.objectName);
                nameLabel.AddToClassList("renderer-name");
                headerContent.Add(nameLabel);

                // Click header content to select GameObject in Hierarchy
                var capturedRendererPath = rendererPath;
                headerContent.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.button == 0 && _baseInstance != null)
                    {
                        var target = string.IsNullOrEmpty(capturedRendererPath)
                            ? _baseInstance.transform
                            : _baseInstance.transform.Find(capturedRendererPath);
                        if (target != null)
                            Selection.activeGameObject = target.gameObject;
                    }
                });

                _materialSlotsScroll.Add(collapsible);

                // Slot rows
                foreach (var slot in group.OrderBy(s => s.identifier.slotIndex))
                {
                    var row = CreateSlotRow(slot);
                    content.Add(row);
                }
            }
        }

        /// <summary>
        /// Builds one slot row: [index] [base preview] [base name] -> [override preview] [override field] [clear]
        /// </summary>
        private VisualElement CreateSlotRow(ScannedMaterialSlot slot)
        {
            var row = new VisualElement();
            row.AddToClassList("slot-row");

            bool hasOverride = _overrides.TryGetValue(slot.identifier, out var currentOverride) && currentOverride != null;
            if (!hasOverride)
            {
                row.AddToClassList("slot-row-unchanged");
            }

            // Slot index
            var indexLabel = new Label($"[{slot.identifier.slotIndex}]");
            indexLabel.AddToClassList("slot-index");
            row.Add(indexLabel);

            // Base material preview thumbnail
            var basePreview = new Image();
            basePreview.AddToClassList("slot-base-preview");
            EditorUIUtility.SetMaterialPreview(basePreview, slot.baseMaterial);
            row.Add(basePreview);

            // Base material name
            var baseName = new Label(slot.baseMaterial != null ? slot.baseMaterial.name : Localization.S("creator.materialSlots.none"));
            baseName.AddToClassList("slot-base-name");
            row.Add(baseName);

            // Arrow
            var arrow = new Label(EditorUIUtility.Arrow);
            arrow.AddToClassList("slot-arrow");
            row.Add(arrow);

            // Override material preview thumbnail
            var overridePreview = new Image();
            overridePreview.AddToClassList("slot-override-preview");
            if (hasOverride)
            {
                EditorUIUtility.SetMaterialPreview(overridePreview, currentOverride);
            }
            row.Add(overridePreview);

            // Override material ObjectField
            var overrideField = new ObjectField
            {
                objectType = typeof(Material),
                allowSceneObjects = false
            };
            overrideField.AddToClassList("slot-override-field");

            if (_overrides.TryGetValue(slot.identifier, out var existingOverride))
            {
                overrideField.value = existingOverride;
            }

            var capturedSlot = slot;
            var capturedRow = row;
            overrideField.RegisterValueChangedCallback(evt =>
            {
                OnMaterialOverrideChanged(capturedSlot.identifier, evt.newValue as Material, capturedRow);
            });
            row.Add(overrideField);

            // Store ObjectField reference for browser integration
            _slotObjectFields[slot.identifier] = overrideField;

            // Clear button
            var clearBtn = new Button(() =>
            {
                overrideField.value = null;
            })
            {
                text = EditorUIUtility.Cross,
                tooltip = Localization.S("creator.slotClear.tooltip")
            };
            clearBtn.AddToClassList("slot-clear-button");
            row.Add(clearBtn);

            // Click override preview to select the override material in the Inspector
            overridePreview.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0 && _overrides.TryGetValue(capturedSlot.identifier, out var overrideMat) && overrideMat != null)
                {
                    Selection.activeObject = overrideMat;
                    _materialBrowser?.HighlightMaterial(overrideMat);
                    HighlightSlotRow(row);
                    evt.StopPropagation();
                }
            });

            // Click row to select the base material in the Inspector and highlight it in the browser
            row.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0 && capturedSlot.baseMaterial != null)
                {
                    Selection.activeObject = capturedSlot.baseMaterial;
                    _materialBrowser?.HighlightMaterial(capturedSlot.baseMaterial);
                    HighlightSlotRow(row);
                }
            });

            RegisterSlotDragAndDrop(row, overrideField);

            return row;
        }

        private IVisualElementScheduledItem _slotHighlightSchedule;
        private VisualElement _highlightedSlotRow;

        /// <summary>
        /// Temporarily highlights a slot row so the user can see which row was clicked.
        /// </summary>
        private void HighlightSlotRow(VisualElement row)
        {
            // Clear previous highlight immediately
            if (_highlightedSlotRow != null)
            {
                _highlightedSlotRow.RemoveFromClassList("slot-row-highlight");
                _slotHighlightSchedule?.Pause();
            }

            _highlightedSlotRow = row;
            row.AddToClassList("slot-row-highlight");
            _slotHighlightSchedule = row.schedule.Execute(() =>
            {
                row.RemoveFromClassList("slot-row-highlight");
                _highlightedSlotRow = null;
            }).StartingIn(5000);
        }

        private void OnMaterialOverrideChanged(MaterialSlotIdentifier slot, Material material, VisualElement row)
        {
            if (material != null)
            {
                _overrides[slot] = material;
                row.RemoveFromClassList("slot-row-unchanged");
            }
            else
            {
                _overrides.Remove(slot);
                row.AddToClassList("slot-row-unchanged");
                row.RemoveFromClassList("slot-row-drag-hover");
            }

            // Update override preview thumbnail
            var overridePreview = row.Q<Image>(className: "slot-override-preview");
            if (overridePreview != null)
            {
                EditorUIUtility.SetMaterialPreview(overridePreview, material);
            }

            // Apply Scene preview
            ApplyPreviewMaterial(slot, material);

            UpdateOutputPreview();
            UpdateGenerateButtonState();
        }

        // ────────────────────────────────────────────────
        // Bulk Mode
        // ────────────────────────────────────────────────

        private void RefreshBulkModeUI()
        {
            var materialGroups = GroupByEffectiveMaterial();

            _slotsInfoLabel.text = Localization.S("creator.materialSlots.bulkInfo",
                materialGroups.Count, _scannedSlots.Count);

            // Alt+click: toggle all collapsible groups at once
            Action<bool> toggleAll = open =>
            {
                foreach (var g in materialGroups)
                    SetBulkFoldout(g.Key, open);
                SetAllCollapsibleStates(_materialSlotsScroll, open);
            };

            foreach (var group in materialGroups)
            {
                var effectiveMaterial = group.Key;
                var slots = group.ToList();
                int overrideCount = slots.Count(s => _overrides.ContainsKey(s.identifier));

                bool defaultOpen = GetBulkFoldout(effectiveMaterial);

                var (headerContent, content, collapsible) = CreateCollapsibleGroup(
                    defaultOpen,
                    isOpen => SetBulkFoldout(effectiveMaterial, isOpen),
                    toggleAll);

                if (overrideCount == 0)
                {
                    collapsible.AddToClassList("bulk-group-no-overrides");
                }

                CreateBulkGroupHeader(headerContent, effectiveMaterial, slots, overrideCount);

                foreach (var slot in slots.OrderBy(s => s.identifier.objectName).ThenBy(s => s.identifier.slotIndex))
                {
                    var childRow = CreateBulkChildRow(slot, effectiveMaterial);
                    content.Add(childRow);
                }

                _materialSlotsScroll.Add(collapsible);
            }
        }

        /// <summary>
        /// Groups scanned slots by their effective material (override if set, base otherwise).
        /// </summary>
        private List<IGrouping<Material, ScannedMaterialSlot>> GroupByEffectiveMaterial()
        {
            return _scannedSlots
                .GroupBy(s =>
                {
                    if (_overrides.TryGetValue(s.identifier, out var overrideMat) && overrideMat != null)
                        return overrideMat;
                    return s.baseMaterial;
                })
                .OrderBy(g => g.Key == null ? 1 : 0)
                .ThenBy(g => g.Key != null ? g.Key.name : "")
                .ToList();
        }

        private void CreateBulkGroupHeader(VisualElement headerContent, Material effectiveMaterial,
            List<ScannedMaterialSlot> slots, int overrideCount)
        {
            // Material thumbnail
            var thumbnail = new Image();
            thumbnail.AddToClassList("slot-base-preview");
            EditorUIUtility.SetMaterialPreview(thumbnail, effectiveMaterial);
            headerContent.Add(thumbnail);

            // Material name
            string matName = effectiveMaterial != null
                ? effectiveMaterial.name
                : Localization.S("creator.materialSlots.none");
            var nameLabel = new Label(matName);
            nameLabel.tooltip = matName;
            nameLabel.AddToClassList("bulk-group-name");
            headerContent.Add(nameLabel);

            // Slot count
            var countLabel = new Label($"({Localization.S("creator.bulk.slotsCount", slots.Count)})");
            countLabel.AddToClassList("bulk-group-count");
            headerContent.Add(countLabel);

            // Override count badge (only shown when overrides exist)
            if (overrideCount > 0)
            {
                var badge = new Label(overrideCount.ToString());
                badge.AddToClassList("bulk-override-badge");
                headerContent.Add(badge);
            }

            // Arrow
            var arrow = new Label(EditorUIUtility.Arrow);
            arrow.AddToClassList("slot-arrow");
            headerContent.Add(arrow);

            // Override ObjectField for bulk assignment
            var overrideField = new ObjectField
            {
                objectType = typeof(Material),
                allowSceneObjects = false
            };
            overrideField.AddToClassList("slot-override-field");
            overrideField.AddToClassList("bulk-header-override-field");

            var capturedSlots = slots;
            overrideField.RegisterValueChangedCallback(evt =>
            {
                var mat = evt.newValue as Material;
                if (mat != null)
                {
                    ApplyBulkOverride(capturedSlots, mat);
                }
            });
            headerContent.Add(overrideField);

            // Clear button
            bool hasOverrides = overrideCount > 0;
            var clearBtn = new Button(() => ClearBulkOverride(capturedSlots))
            {
                text = EditorUIUtility.Cross,
                tooltip = Localization.S("creator.bulk.clearGroup.tooltip")
            };
            clearBtn.AddToClassList("slot-clear-button");
            clearBtn.SetEnabled(hasOverrides);
            headerContent.Add(clearBtn);

            // Click thumbnail/name to select material in Inspector + highlight in browser
            var capturedMaterial = effectiveMaterial;
            RegisterMaterialClickHandler(thumbnail, capturedMaterial, headerContent);
            RegisterMaterialClickHandler(nameLabel, capturedMaterial, headerContent);

            // Header D&D for bulk assignment
            RegisterBulkHeaderDragAndDrop(headerContent, capturedSlots);
        }

        private void RegisterMaterialClickHandler(VisualElement element, Material material, VisualElement highlightRow = null)
        {
            element.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0 && material != null)
                {
                    Selection.activeObject = material;
                    _materialBrowser?.HighlightMaterial(material);
                    if (highlightRow != null)
                        HighlightSlotRow(highlightRow);
                    evt.StopPropagation();
                }
            });
        }

        private VisualElement CreateBulkChildRow(ScannedMaterialSlot slot, Material groupMaterial)
        {
            var row = new VisualElement();
            row.AddToClassList("slot-row");
            row.AddToClassList("bulk-child-row");

            bool hasOverride = _overrides.TryGetValue(slot.identifier, out var currentOverride) && currentOverride != null;
            if (!hasOverride)
            {
                row.AddToClassList("slot-row-unchanged");
            }

            // ObjectName / Slot N
            var displayLabel = new Label($"{slot.identifier.objectName} / Slot {slot.identifier.slotIndex}");
            displayLabel.AddToClassList("bulk-child-label");
            row.Add(displayLabel);

            // Base material info (show for overridden slots where base differs from group material)
            if (hasOverride)
            {
                string baseName = slot.baseMaterial != null
                    ? slot.baseMaterial.name
                    : Localization.S("creator.materialSlots.none");
                var baseLabel = new Label(Localization.S("creator.bulk.base", baseName));
                baseLabel.AddToClassList("bulk-child-base-label");
                RegisterMaterialClickHandler(baseLabel, slot.baseMaterial);
                row.Add(baseLabel);
            }

            // Clear button for individual override
            if (hasOverride)
            {
                var capturedSlot = slot;
                var clearBtn = new Button(() =>
                {
                    _overrides.Remove(capturedSlot.identifier);
                    ApplyPreviewMaterial(capturedSlot.identifier, null);
                    UpdateOutputPreview();
                    UpdateGenerateButtonState();
                    RefreshMaterialSlotsUI();
                })
                {
                    text = EditorUIUtility.Cross,
                    tooltip = Localization.S("creator.slotClear.tooltip")
                };
                clearBtn.AddToClassList("slot-clear-button");
                row.Add(clearBtn);
            }

            // Click to select the GameObject in Hierarchy
            var capturedRendererPath = slot.identifier.rendererPath;
            row.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0 && _baseInstance != null)
                {
                    var target = string.IsNullOrEmpty(capturedRendererPath)
                        ? _baseInstance.transform
                        : _baseInstance.transform.Find(capturedRendererPath);
                    if (target != null)
                        Selection.activeGameObject = target.gameObject;
                }
            });

            return row;
        }

        // ────────────────────────────────────────────────
        // Bulk Operations
        // ────────────────────────────────────────────────

        /// <summary>
        /// Applies a material override to all slots in a bulk group as a single undo operation.
        /// </summary>
        private void ApplyBulkOverride(List<ScannedMaterialSlot> slots, Material material)
        {
            if (material == null) return;

            Undo.SetCurrentGroupName("Bulk Material Replace");
            _previewActive = true;

            foreach (var slot in slots)
            {
                _overrides[slot.identifier] = material;
                SetRendererMaterial(slot.identifier, material);
            }

            SceneView.RepaintAll();
            RefreshAllUI();
        }

        /// <summary>
        /// Clears all overrides in a bulk group as a single undo operation.
        /// </summary>
        private void ClearBulkOverride(List<ScannedMaterialSlot> slots)
        {
            bool hadAnyOverride = false;

            Undo.SetCurrentGroupName("Bulk Clear Overrides");
            _previewActive = true;

            foreach (var slot in slots)
            {
                if (_overrides.Remove(slot.identifier))
                {
                    hadAnyOverride = true;
                    SetRendererMaterial(slot.identifier, null);
                }
            }

            if (hadAnyOverride)
            {
                SceneView.RepaintAll();
                RefreshAllUI();
            }
        }

        // ────────────────────────────────────────────────
        // Bulk State Helpers
        // ────────────────────────────────────────────────

        private bool GetBulkFoldout(Material material)
        {
            if (material == null) return _bulkNullMaterialFoldout;
            return _bulkFoldoutState.TryGetValue(material, out var state) && state;
        }

        private void SetBulkFoldout(Material material, bool open)
        {
            if (material == null)
                _bulkNullMaterialFoldout = open;
            else
                _bulkFoldoutState[material] = open;
        }

        // ────────────────────────────────────────────────
        // Drag & Drop
        // ────────────────────────────────────────────────

        private static void RegisterSlotDragAndDrop(VisualElement row, ObjectField overrideField)
        {
            // Use TrickleDown so the event fires even when the cursor is over a child
            // element (e.g. the ObjectField) that would otherwise stop propagation.
            row.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (DragAndDrop.objectReferences.Length > 0 && DragAndDrop.objectReferences[0] is Material)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Link;
                    row.AddToClassList("slot-row-drag-hover");
                }
            }, TrickleDown.TrickleDown);

            row.RegisterCallback<DragLeaveEvent>(evt =>
            {
                // Only remove highlight when the cursor truly left the row bounds.
                // DragLeaveEvent can also fire when the cursor moves to a child element
                // within the row, in which case we want to keep the highlight.
                if (!row.worldBound.Contains(evt.mousePosition))
                {
                    row.RemoveFromClassList("slot-row-drag-hover");
                }
            });

            row.RegisterCallback<DragPerformEvent>(evt =>
            {
                row.RemoveFromClassList("slot-row-drag-hover");
                if (DragAndDrop.objectReferences.Length > 0 && DragAndDrop.objectReferences[0] is Material droppedMaterial)
                {
                    DragAndDrop.AcceptDrag();
                    overrideField.value = droppedMaterial;
                }
            }, TrickleDown.TrickleDown);

            row.RegisterCallback<DragExitedEvent>(evt =>
            {
                row.RemoveFromClassList("slot-row-drag-hover");
            });
        }

        private void RegisterBulkHeaderDragAndDrop(VisualElement header, List<ScannedMaterialSlot> slots)
        {
            header.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (DragAndDrop.objectReferences.Length > 0 && DragAndDrop.objectReferences[0] is Material)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Link;
                    header.AddToClassList("slot-row-drag-hover");
                }
            }, TrickleDown.TrickleDown);

            header.RegisterCallback<DragLeaveEvent>(evt =>
            {
                if (!header.worldBound.Contains(evt.mousePosition))
                {
                    header.RemoveFromClassList("slot-row-drag-hover");
                }
            });

            var capturedSlots = slots;
            header.RegisterCallback<DragPerformEvent>(evt =>
            {
                header.RemoveFromClassList("slot-row-drag-hover");
                if (DragAndDrop.objectReferences.Length > 0 && DragAndDrop.objectReferences[0] is Material mat)
                {
                    DragAndDrop.AcceptDrag();
                    ApplyBulkOverride(capturedSlots, mat);
                }
            }, TrickleDown.TrickleDown);

            header.RegisterCallback<DragExitedEvent>(evt =>
            {
                header.RemoveFromClassList("slot-row-drag-hover");
            });
        }

        // ────────────────────────────────────────────────
        // Collapsible Helper
        // ────────────────────────────────────────────────

        /// <summary>
        /// Creates a collapsible group with an arrow toggle, header content area, and hideable content area.
        /// The arrow click toggles expand/collapse; other clicks on the header content are handled by callers.
        /// </summary>
        private static (VisualElement headerContent, VisualElement content, VisualElement root)
            CreateCollapsibleGroup(bool defaultOpen, Action<bool> onToggle, Action<bool> onAltToggle = null)
        {
            var root = new VisualElement();
            root.AddToClassList("collapsible-group");

            var header = new VisualElement();
            header.AddToClassList("collapsible-header");

            var arrowLabel = new Label("\u25B6");
            arrowLabel.AddToClassList("collapsible-arrow");
            if (defaultOpen) arrowLabel.AddToClassList("collapsible-arrow--open");
            header.Add(arrowLabel);

            var headerContent = new VisualElement();
            headerContent.AddToClassList("collapsible-header-content");
            header.Add(headerContent);

            root.Add(header);

            var content = new VisualElement();
            content.AddToClassList("collapsible-content");
            content.style.display = defaultOpen ? DisplayStyle.Flex : DisplayStyle.None;

            arrowLabel.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                evt.StopPropagation();
                bool isOpen = content.style.display == DisplayStyle.Flex;
                bool newState = !isOpen;

                if (evt.altKey && onAltToggle != null)
                {
                    onAltToggle.Invoke(newState);
                }
                else
                {
                    content.style.display = newState ? DisplayStyle.Flex : DisplayStyle.None;
                    arrowLabel.EnableInClassList("collapsible-arrow--open", newState);
                    onToggle?.Invoke(newState);
                }
            });

            root.Add(content);
            return (headerContent, content, root);
        }

        /// <summary>
        /// Expands or collapses all collapsible groups within a container.
        /// </summary>
        private static void SetAllCollapsibleStates(VisualElement container, bool open)
        {
            foreach (var content in container.Query(className: "collapsible-content").ToList())
                content.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var arrow in container.Query<Label>(className: "collapsible-arrow").ToList())
                arrow.EnableInClassList("collapsible-arrow--open", open);
        }

        // ────────────────────────────────────────────────
        // Utility
        // ────────────────────────────────────────────────

        private static Texture GetRendererIcon(string rendererType)
        {
            if (rendererType == nameof(SkinnedMeshRenderer))
            {
                return EditorGUIUtility.IconContent("SkinnedMeshRenderer Icon")?.image;
            }
            if (rendererType == nameof(MeshRenderer))
            {
                return EditorGUIUtility.IconContent("MeshRenderer Icon")?.image;
            }
            return null;
        }
    }
}
