using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kanameliser.ColorVariantGenerator
{
    internal partial class BatchGeneratorWindow
    {
        // ────────────────────────────────────────────────
        // Section: Variant List
        // ────────────────────────────────────────────────

        private void CreateVariantListSection(VisualElement container)
        {
            _variantListSection = new VisualElement();
            _variantListSection.AddToClassList("section-container");
            _variantListSection.AddToClassList("variant-list-section");

            var headerRow = new VisualElement();
            headerRow.AddToClassList("variant-list-header");

            var label = new Label("batch.variantList");
            label.AddToClassList("section-label");
            label.AddToClassList("ndmf-tr");
            headerRow.Add(label);

            _addVariantButton = new Button(OnAddVariantClicked) { text = "batch.addVariant" };
            _addVariantButton.AddToClassList("add-variant-button");
            _addVariantButton.AddToClassList("ndmf-tr");
            headerRow.Add(_addVariantButton);

            _variantListSection.Add(headerRow);

            var variantListWrapper = new VisualElement();
            variantListWrapper.AddToClassList("variant-list-container");

            _variantListPlaceholder = new Label("batch.variantList.placeholder");
            _variantListPlaceholder.AddToClassList("variant-list-placeholder");
            _variantListPlaceholder.AddToClassList("ndmf-tr");
            variantListWrapper.Add(_variantListPlaceholder);

            _variantListContainer = new VisualElement();
            variantListWrapper.Add(_variantListContainer);

            _variantListSection.Add(variantListWrapper);

            // D&D: accept prefab drops anywhere in the section to add variants
            // Skip events that originate from child ObjectFields (they handle D&D natively)
            _variantListSection.RegisterCallback<DragEnterEvent>(evt =>
            {
                if (!IsDragTargetingChildObjectField(evt.target) && HasDraggablePrefabs())
                {
                    _variantListSection.AddToClassList("variant-list-section-drag-hover");
                }
            });

            _variantListSection.RegisterCallback<DragLeaveEvent>(evt =>
            {
                if (!IsDragTargetingChildObjectField(evt.target))
                {
                    _variantListSection.RemoveFromClassList("variant-list-section-drag-hover");
                }
            });

            // Clean up highlight when drag operation ends for any reason
            _variantListSection.RegisterCallback<DragExitedEvent>(evt =>
            {
                _variantListSection.RemoveFromClassList("variant-list-section-drag-hover");
            });

            _variantListSection.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (!IsDragTargetingChildObjectField(evt.target) && HasDraggablePrefabs())
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Link;
                }
            });

            _variantListSection.RegisterCallback<DragPerformEvent>(evt =>
            {
                _variantListSection.RemoveFromClassList("variant-list-section-drag-hover");
                if (IsDragTargetingChildObjectField(evt.target)) return;

                var prefabs = GetDraggablePrefabs();
                if (prefabs.Count > 0)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (var prefab in prefabs)
                    {
                        AddVariantRow(prefab);
                    }
                }
            });

            container.Add(_variantListSection);
        }

        private void OnAddVariantClicked()
        {
            AddVariantRow(null);
        }

        /// <summary>
        /// Walks up the visual tree from the event target to check if the drag is over a
        /// child ObjectField. If so, the section-level D&D handler should not interfere.
        /// </summary>
        private bool IsDragTargetingChildObjectField(IEventHandler target)
        {
            var ve = target as VisualElement;
            while (ve != null && ve != _variantListSection)
            {
                if (ve is ObjectField) return true;
                ve = ve.parent;
            }
            return false;
        }

        private static bool IsDraggablePrefab(Object obj)
        {
            return obj is GameObject go && PrefabUtility.IsPartOfPrefabAsset(go);
        }

        private bool HasDraggablePrefabs()
        {
            return DragAndDrop.objectReferences.Any(IsDraggablePrefab);
        }

        private List<GameObject> GetDraggablePrefabs()
        {
            return DragAndDrop.objectReferences
                .Where(IsDraggablePrefab)
                .Cast<GameObject>()
                .ToList();
        }

        private void AddVariantRow(GameObject prefab)
        {
            var entry = new VariantEntry();
            _variantEntries.Add(entry);

            var row = new VisualElement();
            row.AddToClassList("variant-row");

            AddVariantNameElements(row, entry);
            AddVariantObjectField(row, entry, prefab);

            // Override count label
            var overrideLabel = new Label();
            overrideLabel.AddToClassList("variant-override-label");
            row.Add(overrideLabel);

            AddVariantRemoveButton(row, entry);

            _variantListContainer.Add(row);
            UpdateVariantListPlaceholder();
            UpdateVariantRowBorders();

            if (prefab != null)
            {
                entry.variantPrefab = prefab;
                OnVariantChanged(entry);
            }
        }

        /// <summary>
        /// Adds an inline-editable variant name: label (read-only) + hidden text field + edit button.
        /// Clicking the edit button swaps to the text field; focus-out commits the change.
        /// </summary>
        private void AddVariantNameElements(VisualElement row, VariantEntry entry)
        {
            var nameLabel = new Label();
            nameLabel.AddToClassList("variant-name-label");
            nameLabel.tooltip = Localization.S("batch.variantName.tooltip");

            var nameField = new TextField();
            nameField.AddToClassList("variant-name-field");
            nameField.style.display = DisplayStyle.None;

            var editBtn = new Button()
            {
                text = EditorUIUtility.Pencil,
                tooltip = Localization.S("batch.variantEdit.tooltip")
            };
            editBtn.AddToClassList("variant-edit-button");

            editBtn.clicked += () =>
            {
                nameField.SetValueWithoutNotify(entry.EffectiveVariantName);
                nameLabel.style.display = DisplayStyle.None;
                editBtn.style.display = DisplayStyle.None;
                nameField.style.display = DisplayStyle.Flex;
                nameField.Focus();
            };

            nameField.RegisterCallback<FocusOutEvent>(_ =>
            {
                entry.customVariantName = nameField.value;
                entry.isNameManuallyEdited = !string.IsNullOrEmpty(nameField.value)
                    && nameField.value != (entry.autoVariantName ?? "");
                nameLabel.text = entry.EffectiveVariantName;
                nameField.style.display = DisplayStyle.None;
                nameLabel.style.display = DisplayStyle.Flex;
                editBtn.style.display = DisplayStyle.Flex;
                UpdateOutputPreview();
            });

            row.Add(nameLabel);
            row.Add(nameField);
            row.Add(editBtn);
        }

        private void AddVariantObjectField(VisualElement row, VariantEntry entry, GameObject prefab)
        {
            var field = new ObjectField
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false,
                tooltip = Localization.S("batch.variantField.tooltip")
            };
            field.AddToClassList("variant-field");
            if (prefab != null) field.value = prefab;

            field.RegisterValueChangedCallback(evt =>
            {
                entry.variantPrefab = evt.newValue as GameObject;
                OnVariantChanged(entry);
            });
            row.Add(field);
        }

        private void AddVariantRemoveButton(VisualElement row, VariantEntry entry)
        {
            var removeBtn = new Button(() =>
            {
                _variantEntries.Remove(entry);
                _variantListContainer.Remove(row);
                UpdateVariantListPlaceholder();
                UpdateVariantRowBorders();
                RunAllMatching();
                UpdateGenerateButtonState();
            })
            {
                text = EditorUIUtility.Cross,
                tooltip = Localization.S("batch.variantRemove.tooltip")
            };
            removeBtn.AddToClassList("variant-remove-button");
            row.Add(removeBtn);
        }

        private void UpdateVariantListPlaceholder()
        {
            _variantListPlaceholder.style.display =
                _variantEntries.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateVariantRowBorders()
        {
            RemoveLastChildBottomBorder(_variantListContainer);
        }

        private void UpdateVariantRowTooltips()
        {
            foreach (var row in _variantListContainer.Children())
            {
                var nameLabel = row.Q<Label>(className: "variant-name-label");
                if (nameLabel != null) nameLabel.tooltip = Localization.S("batch.variantName.tooltip");

                var editBtn = row.Q<Button>(className: "variant-edit-button");
                if (editBtn != null) editBtn.tooltip = Localization.S("batch.variantEdit.tooltip");

                var field = row.Q<ObjectField>(className: "variant-field");
                if (field != null) field.tooltip = Localization.S("batch.variantField.tooltip");

                var removeBtn = row.Q<Button>(className: "variant-remove-button");
                if (removeBtn != null) removeBtn.tooltip = Localization.S("batch.variantRemove.tooltip");
            }
        }

        private void OnVariantChanged(VariantEntry entry)
        {
            int index = _variantEntries.IndexOf(entry);
            VisualElement row = (index >= 0 && index < _variantListContainer.childCount)
                ? _variantListContainer[index] : null;

            if (entry.variantPrefab != null)
            {
                // Derive variant name from the prefab hierarchy:
                // If it's a variant of another prefab, strip the parent name (e.g. "Avatar_Black" → "Black").
                // Otherwise, use the full prefab name as the variant name.
                var parent = PrefabUtility.GetCorrespondingObjectFromSource(entry.variantPrefab);
                if (parent != null && parent != entry.variantPrefab)
                {
                    entry.autoVariantName = VariantAnalyzer.DeriveVariantName(parent.name, entry.variantPrefab.name);
                }
                else
                {
                    entry.autoVariantName = entry.variantPrefab.name;
                }

                // Clear stale customVariantName so EffectiveVariantName falls back to autoVariantName
                if (!entry.isNameManuallyEdited)
                {
                    entry.customVariantName = null;
                }

                if (row != null)
                {
                    var nameLabel = row.Q<Label>(className: "variant-name-label");
                    if (nameLabel != null)
                    {
                        nameLabel.text = entry.EffectiveVariantName;
                    }
                }
            }
            else
            {
                entry.autoVariantName = null;
                if (row != null)
                {
                    var nameLabel = row.Q<Label>(className: "variant-name-label");
                    if (nameLabel != null) nameLabel.text = "";
                }
            }

            // Run matching (override count label is updated in RefreshMatchResultsUI)
            RunAllMatching();
            UpdateGenerateButtonState();
        }
    }
}
