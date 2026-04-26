using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Kanameliser.ColorVariantGenerator
{
    /// <summary>
    /// Analyzes Prefab instance modifications for Standard mode.
    /// Provides a structural diff used by the UI for empty-change confirmation, and a
    /// post-duplication trimmer used by the generator when the user opts out of Transform /
    /// component property transfer.
    /// </summary>
    internal static class PrefabModificationHelper
    {
        /// <summary>
        /// Analyzes a hierarchy instance and returns a summary of all structural and property changes
        /// relative to its base Prefab.
        /// </summary>
        public static StructuralChangeSummary AnalyzeStructuralChanges(GameObject hierarchyInstance)
        {
            var summary = new StructuralChangeSummary();

            if (hierarchyInstance == null || !PrefabUtility.IsPartOfPrefabInstance(hierarchyInstance))
                return summary;

            // Detect added GameObjects
            var addedObjects = PrefabUtility.GetAddedGameObjects(hierarchyInstance);
            foreach (var added in addedObjects)
            {
                string path = GetRelativePath(hierarchyInstance.transform, added.instanceGameObject.transform);
                summary.addedGameObjects.Add(path);
            }

            // Detect removed GameObjects
            var removedObjects = PrefabUtility.GetRemovedGameObjects(hierarchyInstance);
            foreach (var removed in removedObjects)
            {
                string path = removed.assetGameObject != null ? removed.assetGameObject.name : "(unknown)";
                // Try to build a fuller path from the parent
                if (removed.parentOfRemovedGameObjectInInstance != null)
                {
                    string parentPath = GetRelativePath(
                        hierarchyInstance.transform, removed.parentOfRemovedGameObjectInInstance.transform);
                    path = string.IsNullOrEmpty(parentPath)
                        ? removed.assetGameObject.name
                        : $"{parentPath}/{removed.assetGameObject.name}";
                }
                summary.removedGameObjects.Add(path);
            }

            // Use GetObjectOverrides to detect only actual overrides visible in the Inspector.
            // GetPropertyModifications returns internal/default values that are not real overrides.
            var objectOverrides = PrefabUtility.GetObjectOverrides(hierarchyInstance, true);
            var addedInstanceIds = BuildAddedInstanceIds(hierarchyInstance);

            foreach (var objectOverride in objectOverrides)
            {
                var target = objectOverride.instanceObject;
                if (target == null) continue;

                // Skip overrides on added GameObjects (those are always included wholesale)
                if (IsAddedObject(target, addedInstanceIds))
                    continue;

                // Detect renames (GameObject override with m_Name changed)
                if (target is GameObject go)
                {
                    var assetObject = objectOverride.GetAssetObject() as GameObject;
                    if (assetObject != null && go.name != assetObject.name)
                    {
                        string objectPath = GetRelativePath(hierarchyInstance.transform, go.transform);
                        if (!summary.renamedGameObjects.Contains(objectPath))
                            summary.renamedGameObjects.Add(objectPath);
                    }
                }

                // Transform and component overrides on existing objects are not tracked here.
                // They are optionally included during generation via StandardModeOptions.
            }

            // Detect GameObject-level property overrides other than rename
            // (active state, tag, layer, static flags, etc.). These are transferred by the
            // filtered Standard path, so they must count as real changes for the Generate
            // button's empty-change confirmation.
            var allModifications = PrefabUtility.GetPropertyModifications(hierarchyInstance);
            if (allModifications != null)
            {
                Dictionary<Object, Object> baseToInstance = null;

                foreach (var mod in allModifications)
                {
                    if (!(mod.target is GameObject)) continue;
                    if (string.IsNullOrEmpty(mod.propertyPath)) continue;
                    if (mod.propertyPath == "m_Name") continue; // already tracked as rename
                    if (mod.propertyPath.StartsWith("m_Component")) continue; // component add/remove is tracked separately
                    if (IsAddedObject(mod.target, addedInstanceIds)) continue;
                    if (!ValueDiffersFromBase(mod)) continue;

                    baseToInstance ??= BuildBaseAssetToTargetMapping(hierarchyInstance);
                    string objectPath = GetGameObjectPathForModification(
                        hierarchyInstance, mod.target, baseToInstance);
                    string entry = $"{objectPath}::{mod.propertyPath}";
                    if (!summary.changedGameObjectProperties.Contains(entry))
                        summary.changedGameObjectProperties.Add(entry);
                }
            }

            // Detect added components on existing GameObjects
            var addedComponents = PrefabUtility.GetAddedComponents(hierarchyInstance);
            foreach (var addedComp in addedComponents)
            {
                var comp = addedComp.instanceComponent;
                if (comp == null) continue;
                string path = GetRelativePath(hierarchyInstance.transform, comp.transform);
                summary.addedComponents.Add($"{path}::{comp.GetType().Name}");
            }

            // Detect removed components on existing GameObjects
            var removedComponents = PrefabUtility.GetRemovedComponents(hierarchyInstance);
            foreach (var removedComp in removedComponents)
            {
                var assetComp = removedComp.assetComponent;
                if (assetComp == null) continue;
                Transform parentInInstance = removedComp.containingInstanceGameObject != null
                    ? removedComp.containingInstanceGameObject.transform
                    : null;
                string path = parentInInstance != null
                    ? GetRelativePath(hierarchyInstance.transform, parentInInstance)
                    : "(unknown)";
                summary.removedComponents.Add($"{path}::{assetComp.GetType().Name}");
            }

            return summary;
        }

        /// <summary>
        /// Strips non-structural overrides from a freshly duplicated Prefab instance so that
        /// only structural changes (added / removed GameObjects, GameObject-level property
        /// overrides like rename / active state / tag / layer / static flags) survive into the
        /// saved Variant. Used by the generator when the user opted out of Transform / component
        /// property transfer.
        /// </summary>
        /// <remarks>
        /// Reverts performed:
        ///   - Added components on existing GameObjects (added subtrees are exempt).
        ///   - Removed components on existing GameObjects.
        ///   - Property overrides on Transforms / components belonging to existing GameObjects.
        ///     m_Materials is included in this revert; the generator re-applies user-selected
        ///     material overrides afterwards through the contents API.
        ///   - GameObject-level "no-op" PropertyModification entries (override value equals base).
        ///     Unity retains these entries even after the user toggles a value back to the base,
        ///     and SaveAsPrefabAsset on a Pasteboard duplicate can persist them on the saved
        ///     Variant; trim them explicitly to keep the asset clean.
        /// Preserved:
        ///   - All overrides inside added GameObject subtrees, including nested Prefab structural
        ///     overrides (which is the key reason we duplicate instead of rebuilding from base).
        ///   - GameObject-level overrides (m_Name / m_IsActive / m_TagString / m_Layer /
        ///     m_StaticEditorFlags) on existing GameObjects whose value differs from the base.
        /// </remarks>
        public static void RevertNonStructuralOverrides(GameObject duplicate)
        {
            if (duplicate == null) return;

            var addedInstanceIds = BuildAddedInstanceIds(duplicate);

            // 1) Revert components added to existing GameObjects.
            //    GetAddedComponents does not include components that live inside an added
            //    GameObject subtree (those are part of the added subtree, not "added on the
            //    base"), but the addedInstanceIds guard is kept as a defensive backstop.
            var addedComponents = PrefabUtility.GetAddedComponents(duplicate);
            foreach (var addedComp in addedComponents)
            {
                var comp = addedComp.instanceComponent;
                if (comp == null) continue;
                if (IsAddedObject(comp, addedInstanceIds)) continue;
                PrefabUtility.RevertAddedComponent(comp, InteractionMode.AutomatedAction);
            }

            // 2) Re-add components removed from existing GameObjects.
            var removedComponents = PrefabUtility.GetRemovedComponents(duplicate);
            foreach (var removedComp in removedComponents)
            {
                if (removedComp.assetComponent == null) continue;
                var host = removedComp.containingInstanceGameObject;
                if (host == null) continue;
                if (IsAddedObject(host, addedInstanceIds)) continue;
                removedComp.Revert(InteractionMode.AutomatedAction);
            }

            // 3) Revert property overrides on Transforms / components of existing GameObjects.
            //    GameObject-level overrides are deliberately preserved (m_Name, m_IsActive,
            //    m_TagString, m_Layer, m_StaticEditorFlags etc. all live on the GameObject
            //    object itself, not on a component), so this loop skips GameObject targets.
            var objectOverrides = PrefabUtility.GetObjectOverrides(duplicate, true);
            foreach (var objectOverride in objectOverrides)
            {
                var target = objectOverride.instanceObject;
                if (target == null) continue;
                if (target is GameObject) continue;
                if (!(target is Component component)) continue;
                if (IsAddedObject(component, addedInstanceIds)) continue;
                PrefabUtility.RevertObjectOverride(target, InteractionMode.AutomatedAction);
            }

            // 4) Drop GameObject-level "no-op" property modifications whose override value
            //    matches the base. Unity keeps these entries in the modification list even
            //    after the user toggles the value back to the base value (e.g. rename then
            //    rename back), and SaveAsPrefabAsset on a Pasteboard duplicate may persist
            //    them as override rows on the saved Variant. Trim them explicitly so the
            //    generated asset reflects only meaningful changes.
            TrimNoOpGameObjectModifications(duplicate, addedInstanceIds);
        }

        /// <summary>
        /// Rewrites the duplicate's PropertyModification list, removing entries on
        /// GameObject targets (existing, non-added) whose override value already equals the
        /// base asset's current value. Modifications on components or on objects inside an
        /// added subtree are passed through untouched (the former are reverted by Step 3,
        /// the latter must be preserved wholesale).
        /// </summary>
        private static void TrimNoOpGameObjectModifications(GameObject duplicate, HashSet<int> addedInstanceIds)
        {
            var modifications = PrefabUtility.GetPropertyModifications(duplicate);
            if (modifications == null || modifications.Length == 0) return;

            var kept = new List<PropertyModification>(modifications.Length);
            bool anyDropped = false;
            foreach (var mod in modifications)
            {
                if (mod.target is GameObject
                    && !IsAddedObject(mod.target, addedInstanceIds)
                    && !ValueDiffersFromBase(mod))
                {
                    anyDropped = true;
                    continue;
                }
                kept.Add(mod);
            }

            if (anyDropped)
                PrefabUtility.SetPropertyModifications(duplicate, kept.ToArray());
        }

        /// <summary>
        /// Returns true if the modification's override value differs from the current value on
        /// the base Prefab asset. Used to drop "no-op" overrides that Unity keeps in its
        /// PropertyModification list even after the user reverts the value by toggling it back
        /// to the base. Returns true (include) as a safe default when the base cannot be read.
        /// </summary>
        /// <remarks>
        /// PropertyModification.target references the *source* Prefab asset object (not the
        /// scene instance), so reading SerializedObject(mod.target) yields the base value —
        /// not the user's override. The override value lives in mod.value as a string. Compare
        /// that string against the base asset's current value.
        /// </remarks>
        private static bool ValueDiffersFromBase(PropertyModification mod)
        {
            if (mod.target == null) return true;

            using var baseSO = new SerializedObject(mod.target);
            var baseProp = baseSO.FindProperty(mod.propertyPath);
            if (baseProp == null) return true;

            return !SerializedValueEqualsString(baseProp, mod.value, mod.objectReference);
        }

        /// <summary>
        /// Compares a SerializedProperty's current value against a PropertyModification-style
        /// string representation. Returns false on parse failure so callers default to "differs"
        /// and include the mod rather than silently dropping it.
        /// </summary>
        private static bool SerializedValueEqualsString(SerializedProperty prop, string value, Object objectReference)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.Character:
                    return long.TryParse(value, out long l) && prop.longValue == l;
                case SerializedPropertyType.Boolean:
                    return bool.TryParse(NormalizeBoolString(value), out bool b) && prop.boolValue == b;
                case SerializedPropertyType.Float:
                    // Use approximate equality so tiny rounding between Unity's serialized
                    // and runtime representations doesn't resurrect the no-op override that
                    // this branch exists to filter out.
                    return float.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float f)
                        && Mathf.Approximately(prop.floatValue, f);
                case SerializedPropertyType.String:
                    return prop.stringValue == (value ?? string.Empty);
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue == objectReference;
                case SerializedPropertyType.Enum:
                    // Use intValue (the underlying enum integer) rather than enumValueIndex
                    // (the ordinal into the enum names array). They diverge for non-sequential
                    // enums and are meaningless for [Flags] enums such as m_StaticEditorFlags.
                    return int.TryParse(value, out int ei) && prop.intValue == ei;
                default:
                    return false;
            }
        }

        // PropertyModification.value stores booleans as "0"/"1"; normalize to "false"/"true"
        // so bool.TryParse can consume them.
        private static string NormalizeBoolString(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (value == "0") return "false";
            if (value == "1") return "true";
            return value;
        }

        /// <summary>
        /// Builds a mapping from base Prefab asset objects (GameObjects + Transforms + Components)
        /// to the corresponding objects in the live instance. Used by AnalyzeStructuralChanges
        /// to remap PropertyModification.target, which Unity stores as a reference to the base
        /// asset object rather than the scene instance object.
        /// </summary>
        private static Dictionary<Object, Object> BuildBaseAssetToTargetMapping(GameObject targetInstance)
        {
            var mapping = new Dictionary<Object, Object>();
            CollectBaseToTarget(targetInstance.transform, mapping);
            return mapping;
        }

        private static void CollectBaseToTarget(Transform target, Dictionary<Object, Object> mapping)
        {
            var baseGO = PrefabUtility.GetCorrespondingObjectFromSource(target.gameObject);
            if (baseGO != null)
                mapping[baseGO] = target.gameObject;

            var baseTransform = PrefabUtility.GetCorrespondingObjectFromSource(target);
            if (baseTransform != null)
                mapping[baseTransform] = target;

            foreach (var comp in target.GetComponents<Component>())
            {
                if (comp == null) continue;
                var baseComp = PrefabUtility.GetCorrespondingObjectFromSource(comp);
                if (baseComp != null && !mapping.ContainsKey(baseComp))
                    mapping[baseComp] = comp;
            }

            for (int i = 0; i < target.childCount; i++)
                CollectBaseToTarget(target.GetChild(i), mapping);
        }

        private static string GetGameObjectPathForModification(
            GameObject hierarchyInstance,
            Object modificationTarget,
            Dictionary<Object, Object> baseToInstance)
        {
            if (baseToInstance.TryGetValue(modificationTarget, out var remapped)
                && remapped is GameObject instanceGo)
            {
                return GetRelativePath(hierarchyInstance.transform, instanceGo.transform);
            }

            return modificationTarget is GameObject targetGo ? targetGo.name : "(unknown)";
        }

        /// <summary>
        /// Gets the relative path from root to target transform.
        /// Returns empty string if target is the root itself.
        /// When root is null, walks up to the hierarchy root (parent == null).
        /// </summary>
        private static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root) return "";

            var parts = new List<string>();
            var current = target;
            while (current != null && current != root && current.parent != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            // If root was specified and we didn't reach it, fall back to the target name
            if (root != null && current != root) return target.name;

            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>
        /// Builds a set of instance IDs covering all GameObjects and Components
        /// under each added root. Used to identify whether a given object belongs
        /// to an added subtree.
        /// </summary>
        private static HashSet<int> BuildAddedInstanceIds(GameObject hierarchyInstance)
        {
            var ids = new HashSet<int>();
            var addedObjects = PrefabUtility.GetAddedGameObjects(hierarchyInstance);
            foreach (var added in addedObjects)
                CollectInstanceIds(added.instanceGameObject.transform, ids);
            return ids;
        }

        /// <summary>
        /// Returns true if the given object belongs to an added GameObject subtree.
        /// </summary>
        private static bool IsAddedObject(Object obj, HashSet<int> addedInstanceIds)
        {
            var go = obj is GameObject g ? g : (obj is Component c ? c.gameObject : null);
            return go != null && addedInstanceIds.Contains(go.GetInstanceID());
        }

        private static void CollectInstanceIds(Transform root, HashSet<int> ids)
        {
            ids.Add(root.gameObject.GetInstanceID());
            foreach (var comp in root.GetComponents<Component>())
            {
                if (comp != null) ids.Add(comp.GetInstanceID());
            }
            for (int i = 0; i < root.childCount; i++)
            {
                CollectInstanceIds(root.GetChild(i), ids);
            }
        }
    }
}
