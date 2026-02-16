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

            _slotsInfoLabel = new Label(Localization.S("creator.materialSlots"));
            _slotsInfoLabel.AddToClassList("section-label");
            section.Add(_slotsInfoLabel);

            _materialSlotsScroll = new ScrollView(ScrollViewMode.Vertical);
            _materialSlotsScroll.AddToClassList("material-slots-scroll");
            section.Add(_materialSlotsScroll);

            container.Add(section);
        }

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

            // Group by renderer path
            var groupedSlots = _scannedSlots
                .GroupBy(s => s.identifier.rendererPath)
                .OrderBy(g => g.Key)
                .ToList();

            _slotsInfoLabel.text = Localization.S("creator.materialSlots.info", groupedSlots.Count, _scannedSlots.Count);

            foreach (var group in groupedSlots)
            {
                var firstSlot = group.First();

                // Renderer header
                var header = new VisualElement();
                header.AddToClassList("renderer-header");

                var iconContent = GetRendererIcon(firstSlot.identifier.rendererType);
                if (iconContent != null)
                {
                    var icon = new Image { image = iconContent };
                    icon.AddToClassList("renderer-icon");
                    header.Add(icon);
                }

                var nameLabel = new Label(firstSlot.identifier.objectName);
                nameLabel.AddToClassList("renderer-name");
                header.Add(nameLabel);

                // Click renderer header to select its GameObject in the Hierarchy
                var capturedRendererPath = firstSlot.identifier.rendererPath;
                header.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.button == 0 && _baseInstance != null)
                    {
                        Transform target;
                        if (string.IsNullOrEmpty(capturedRendererPath))
                        {
                            target = _baseInstance.transform;
                        }
                        else
                        {
                            target = _baseInstance.transform.Find(capturedRendererPath);
                        }

                        if (target != null)
                        {
                            Selection.activeGameObject = target.gameObject;
                        }
                    }
                });

                _materialSlotsScroll.Add(header);

                // Slot rows
                foreach (var slot in group.OrderBy(s => s.identifier.slotIndex))
                {
                    var row = CreateSlotRow(slot);
                    _materialSlotsScroll.Add(row);
                }
            }
        }

        /// <summary>
        /// Builds one slot row: [index] [base preview] [base name] → [override preview] [override field] [clear]
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
                }
            });

            RegisterSlotDragAndDrop(row, overrideField);

            return row;
        }

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
