using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Kanameliser.ColorVariantGenerator
{
    /// <summary>
    /// Detects, filters, and transfers Prefab instance modifications.
    /// Used by Standard mode to preserve structural changes when generating Prefab Variants.
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
        /// Extracts filtered PropertyModifications from the hierarchy instance.
        /// Added GameObjects are transferred wholesale. For existing objects, filtering is applied
        /// based on the provided options.
        /// </summary>
        /// <remarks>
        /// Material modifications (m_Materials) are always excluded — they are handled
        /// separately by the material override mechanism.
        /// </remarks>
        public static PropertyModification[] ExtractFilteredModifications(
            GameObject hierarchyInstance, StandardModeOptions options)
        {
            if (hierarchyInstance == null) return Array.Empty<PropertyModification>();

            var allModifications = PrefabUtility.GetPropertyModifications(hierarchyInstance);
            if (allModifications == null) return Array.Empty<PropertyModification>();

            var addedInstanceIds = BuildAddedInstanceIds(hierarchyInstance);
            var filtered = new List<PropertyModification>();

            foreach (var mod in allModifications)
            {
                if (mod.target == null) continue;

                if (ShouldIncludeModification(mod, hierarchyInstance, addedInstanceIds, options))
                    filtered.Add(mod);
            }

            return filtered.ToArray();
        }

        /// <summary>
        /// Determines whether a single PropertyModification should be included in the filtered output.
        /// </summary>
        private static bool ShouldIncludeModification(
            PropertyModification mod,
            GameObject hierarchyInstance,
            HashSet<int> addedInstanceIds,
            StandardModeOptions options)
        {
            // Always exclude material modifications (handled by material override system)
            if (mod.propertyPath.StartsWith("m_Materials"))
                return false;

            // Added GameObjects: always include all modifications
            if (IsAddedObject(mod.target, addedInstanceIds))
                return true;

            // Structural rename: include only when the current value actually differs from base.
            // Unity keeps PropertyModification entries even after the user toggles a value back to
            // the base value ("no-op override"), which would otherwise produce meaningless
            // override rows on the generated Variant.
            if (mod.target is GameObject && mod.propertyPath == "m_Name")
                return ValueDiffersFromBase(mod);

            // Active state toggle: same no-op filtering as rename.
            if (mod.target is GameObject && mod.propertyPath == "m_IsActive")
                return ValueDiffersFromBase(mod);

            // Tag change: frequently used in practice (e.g. marking MA bone proxies, etc.),
            // so it's called out alongside rename / active state rather than buried in the
            // generic GameObject-level catch-all below.
            if (mod.target is GameObject && mod.propertyPath == "m_TagString")
                return ValueDiffersFromBase(mod);

            // Skip root Transform — placing a prefab in the scene always creates
            // position/rotation overrides on the root, which are not meaningful.
            if (mod.target is Transform t && t == hierarchyInstance.transform)
                return false;

            // Conditionally include: Transform and component property changes.
            // No base-value filtering here — when the user opts into
            // includePropertyChanges, honor Unity's override list as-is.
            if (mod.target is Transform || mod.target is Component)
                return options.includePropertyChanges;

            // Other GameObject-level overrides (m_Layer, m_StaticEditorFlags, etc.):
            // include only when the current value actually differs from base.
            return ValueDiffersFromBase(mod);
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
                    return float.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float f)
                        && prop.floatValue == f;
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

        /// <summary>
        /// Copies all added GameObjects from the source instance to the target instance,
        /// preserving hierarchy structure and all component data.
        /// </summary>
        /// <remarks>
        /// After cloning, runs a reference-remap pass so that ObjectReference fields on
        /// added components that point into the source hierarchy instance (either into
        /// another added root or into the base-prefab portion of the source) are rewritten
        /// to the corresponding target-side objects. Without this, those references would
        /// be dropped as missing by SaveAsPrefabAsset.
        /// </remarks>
        public static void CopyAddedGameObjects(GameObject sourceInstance, GameObject targetInstance)
        {
            var addedObjects = PrefabUtility.GetAddedGameObjects(sourceInstance);

            // Collected across every added root so that cross-subtree references between
            // separate added roots (each cloned by its own Instantiate call) can be fixed
            // up after all clones exist.
            var sourceToClone = new Dictionary<Object, Object>();

            foreach (var added in addedObjects)
            {
                var sourceGO = added.instanceGameObject;
                if (sourceGO == null) continue;

                // Find the corresponding parent in the target instance
                Transform targetParent = FindCorrespondingTransform(
                    sourceInstance.transform, targetInstance.transform, sourceGO.transform.parent);

                if (targetParent == null)
                {
                    Debug.LogWarning(
                        $"[Color Variant Generator] Could not find parent for added GameObject '{sourceGO.name}' in target instance. Skipping.");
                    continue;
                }

                // Copy the added GameObject, preserving Prefab links if it's a nested Prefab instance
                GameObject copy;
                var nestedPrefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(sourceGO);
                if (nestedPrefabAsset != null && PrefabUtility.IsPartOfPrefabAsset(nestedPrefabAsset))
                {
                    // Nested Prefab: use InstantiatePrefab to maintain the Prefab link,
                    // then apply property overrides from the source instance
                    copy = PrefabUtility.InstantiatePrefab(nestedPrefabAsset, targetParent) as GameObject;
                    if (copy != null)
                    {
                        // Transfer property overrides from source to the new instance
                        var sourceMods = PrefabUtility.GetPropertyModifications(sourceGO);
                        if (sourceMods != null && sourceMods.Length > 0)
                        {
                            PrefabUtility.SetPropertyModifications(copy, sourceMods);
                        }

                        // Copy transform values
                        copy.transform.localPosition = sourceGO.transform.localPosition;
                        copy.transform.localRotation = sourceGO.transform.localRotation;
                        copy.transform.localScale = sourceGO.transform.localScale;
                    }
                }
                else
                {
                    // Plain GameObject (not a Prefab instance): deep copy with Instantiate
                    copy = Object.Instantiate(sourceGO, targetParent);
                }

                if (copy == null) continue;
                copy.name = sourceGO.name; // Remove "(Clone)" suffix
                copy.transform.SetSiblingIndex(sourceGO.transform.GetSiblingIndex());

                CollectSourceToCloneMapping(sourceGO.transform, copy.transform, sourceToClone);
            }

            if (sourceToClone.Count > 0)
                RemapClonedReferences(sourceToClone, sourceInstance, targetInstance);
        }

        /// <summary>
        /// Populates a map from source-side objects (GameObject / Transform / Components) to
        /// their clones on the target side by traversing both subtrees in lockstep.
        /// Relies on Object.Instantiate / InstantiatePrefab preserving component order and
        /// child order, which holds on prefab instances (added components appear at the end,
        /// so positional pairing stays valid up to the shorter length).
        /// </summary>
        private static void CollectSourceToCloneMapping(
            Transform source, Transform clone, Dictionary<Object, Object> map)
        {
            if (source == null || clone == null) return;

            map[source.gameObject] = clone.gameObject;
            map[source] = clone;

            var sourceComponents = source.GetComponents<Component>();
            var cloneComponents = clone.GetComponents<Component>();
            int componentCount = Math.Min(sourceComponents.Length, cloneComponents.Length);
            for (int i = 0; i < componentCount; i++)
            {
                var sc = sourceComponents[i];
                var cc = cloneComponents[i];
                if (sc == null || cc == null) continue;
                if (sc is Transform) continue; // already mapped via source→clone above
                if (!map.ContainsKey(sc))
                    map[sc] = cc;
            }

            int childCount = Math.Min(source.childCount, clone.childCount);
            for (int i = 0; i < childCount; i++)
            {
                CollectSourceToCloneMapping(source.GetChild(i), clone.GetChild(i), map);
            }
        }

        /// <summary>
        /// Rewrites ObjectReference properties on cloned components whose current value
        /// points into the source hierarchy instance. Two cases are handled:
        ///   1) reference is to another added-subtree object — remapped via
        ///      <paramref name="sourceToClone"/>
        ///   2) reference is to a base-prefab object on the source instance — remapped to
        ///      the corresponding object on the target instance via base-asset correspondence
        /// References to objects outside the source instance (project assets, unrelated
        /// scene objects) are left untouched so SaveAsPrefabAsset can handle them normally.
        /// </summary>
        private static void RemapClonedReferences(
            Dictionary<Object, Object> sourceToClone,
            GameObject sourceInstance,
            GameObject targetInstance)
        {
            var sourceInstanceIds = new HashSet<int>();
            CollectInstanceIds(sourceInstance.transform, sourceInstanceIds);

            var baseToTarget = BuildBaseAssetToTargetMapping(targetInstance);

            foreach (var kvp in sourceToClone)
            {
                if (!(kvp.Value is Component clone)) continue;
                if (clone == null) continue;
                // Transform cross-refs (m_Father / m_Children) are fully internal to the
                // cloned subtree and handled by Instantiate. Skip to avoid accidental writes.
                if (clone is Transform) continue;

                using var so = new SerializedObject(clone);
                var prop = so.GetIterator();
                bool changed = false;

                while (prop.NextVisible(enterChildren: true))
                {
                    if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;

                    var current = prop.objectReferenceValue;
                    if (current == null) continue;
                    if (!sourceInstanceIds.Contains(current.GetInstanceID())) continue;

                    Object remapped = null;
                    if (sourceToClone.TryGetValue(current, out var cloneRef))
                    {
                        remapped = cloneRef;
                    }
                    else
                    {
                        var baseAsset = PrefabUtility.GetCorrespondingObjectFromSource(current);
                        if (baseAsset != null && baseToTarget.TryGetValue(baseAsset, out var targetRef))
                            remapped = targetRef;
                    }

                    if (remapped != null && remapped != current)
                    {
                        prop.objectReferenceValue = remapped;
                        changed = true;
                    }
                }

                if (changed)
                    so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Applies removed GameObjects from the source instance to the target instance
        /// by destroying the corresponding objects in the target.
        /// </summary>
        public static void ApplyRemovedGameObjects(GameObject sourceInstance, GameObject targetInstance)
        {
            var removedObjects = PrefabUtility.GetRemovedGameObjects(sourceInstance);

            foreach (var removed in removedObjects)
            {
                if (removed.assetGameObject == null) continue;

                // Build the path of the removed object relative to the prefab root
                // Pass null as root to walk up to the hierarchy root (prefab asset root)
                string removedPath = GetRelativePath(null, removed.assetGameObject.transform);

                // Find the corresponding object in the target instance
                Transform targetTransform = targetInstance.transform.Find(removedPath);
                if (targetTransform != null)
                {
                    Object.DestroyImmediate(targetTransform.gameObject);
                }
                else
                {
                    Debug.LogWarning(
                        $"[Color Variant Generator] Could not find '{removedPath}' in target instance for removal. Skipping.");
                }
            }
        }

        /// <summary>
        /// Applies filtered PropertyModifications to a fresh target instance by remapping each
        /// modification's target (which points to the base Prefab asset object) to the
        /// corresponding scene object on the target instance, then writing the override value
        /// directly onto that object via SerializedObject.
        /// </summary>
        /// <remarks>
        /// Writing values via SerializedObject (rather than PrefabUtility.SetPropertyModifications)
        /// is required: SetPropertyModifications updates only the override bookkeeping and leaves
        /// the runtime state (gameObject.name / .activeSelf / .tag) at the base value. The
        /// subsequent SaveAsPrefabAsset recomputes overrides from the diff against the base, so
        /// any value still matching the base would be recorded as "no override".
        /// </remarks>
        public static void ApplyModifications(GameObject targetInstance, PropertyModification[] modifications)
        {
            if (modifications == null || modifications.Length == 0) return;

            var baseToTarget = BuildBaseAssetToTargetMapping(targetInstance);

            // Group modifications by their remapped target so we can batch SerializedObject writes.
            var modsByTarget = new Dictionary<Object, List<PropertyModification>>();

            foreach (var mod in modifications)
            {
                if (mod.target == null) continue;

                if (!baseToTarget.TryGetValue(mod.target, out var remappedTarget))
                    continue;

                if (!modsByTarget.TryGetValue(remappedTarget, out var list))
                {
                    list = new List<PropertyModification>();
                    modsByTarget[remappedTarget] = list;
                }
                list.Add(new PropertyModification
                {
                    target = remappedTarget,
                    propertyPath = mod.propertyPath,
                    value = mod.value,
                    objectReference = RemapObjectReference(mod.objectReference, baseToTarget)
                });
            }

            foreach (var kvp in modsByTarget)
            {
                var target = kvp.Key;
                using var so = new SerializedObject(target);
                bool anyWritten = false;

                foreach (var mod in kvp.Value)
                {
                    var prop = so.FindProperty(mod.propertyPath);
                    if (prop == null) continue;
                    if (WriteValueToProperty(prop, mod.value, mod.objectReference))
                        anyWritten = true;
                }

                if (anyWritten)
                    so.ApplyModifiedPropertiesWithoutUndo();

                // GameObject.SetActive is the authoritative way to flip active state — the
                // m_IsActive SerializedProperty write above sets the serialized value, but
                // the runtime activeSelf state is also read by some editor paths. Keep them
                // in sync.
                if (target is GameObject go)
                {
                    foreach (var mod in kvp.Value)
                    {
                        if (mod.propertyPath == "m_IsActive"
                            && bool.TryParse(NormalizeBoolString(mod.value), out bool active)
                            && go.activeSelf != active)
                        {
                            go.SetActive(active);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Writes a PropertyModification's serialized value onto a SerializedProperty.
        /// Returns true if the property was successfully updated.
        /// </summary>
        private static bool WriteValueToProperty(SerializedProperty prop, string value, Object objectReference)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.Character:
                    if (long.TryParse(value, out long l))
                    {
                        prop.longValue = l;
                        return true;
                    }
                    return false;
                case SerializedPropertyType.Boolean:
                    if (bool.TryParse(NormalizeBoolString(value), out bool b))
                    {
                        prop.boolValue = b;
                        return true;
                    }
                    return false;
                case SerializedPropertyType.Float:
                    if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float f))
                    {
                        prop.floatValue = f;
                        return true;
                    }
                    return false;
                case SerializedPropertyType.String:
                    prop.stringValue = value ?? string.Empty;
                    return true;
                case SerializedPropertyType.ObjectReference:
                    prop.objectReferenceValue = objectReference;
                    return true;
                case SerializedPropertyType.Enum:
                    // Use intValue (the underlying enum integer) rather than enumValueIndex
                    // (the ordinal into the enum names array). They diverge for non-sequential
                    // enums and are meaningless for [Flags] enums such as m_StaticEditorFlags.
                    if (int.TryParse(value, out int ei))
                    {
                        prop.intValue = ei;
                        return true;
                    }
                    return false;
                default:
                    // Composite / unsupported types — leave for future extension.
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
        /// to the corresponding objects in the fresh target instance. Used to remap
        /// PropertyModification.target, which Unity stores as a reference to the base asset
        /// object rather than the scene instance object.
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

        private static Object RemapObjectReference(Object reference, Dictionary<Object, Object> mapping)
        {
            if (reference == null) return null;
            return mapping.TryGetValue(reference, out var remapped) ? remapped : reference;
        }

        /// <summary>
        /// Finds the transform in targetRoot that corresponds to referenceTransform's position
        /// within sourceRoot's hierarchy. Matches by the shared base Prefab asset object first
        /// (rename-safe) and falls back to relative path for objects with no base correspondence.
        /// </summary>
        private static Transform FindCorrespondingTransform(
            Transform sourceRoot, Transform targetRoot, Transform referenceTransform)
        {
            if (referenceTransform == sourceRoot) return targetRoot;

            var referenceBase = PrefabUtility.GetCorrespondingObjectFromSource(referenceTransform);
            if (referenceBase != null)
            {
                var found = FindByBaseCorrespondence(targetRoot, referenceBase);
                if (found != null) return found;
            }

            string relativePath = GetRelativePath(sourceRoot, referenceTransform);
            if (string.IsNullOrEmpty(relativePath)) return targetRoot;

            return targetRoot.Find(relativePath);
        }

        private static Transform FindByBaseCorrespondence(Transform targetRoot, Transform referenceBase)
        {
            var rootBase = PrefabUtility.GetCorrespondingObjectFromSource(targetRoot);
            if (rootBase == referenceBase) return targetRoot;

            for (int i = 0; i < targetRoot.childCount; i++)
            {
                var child = targetRoot.GetChild(i);
                var found = FindByBaseCorrespondence(child, referenceBase);
                if (found != null) return found;
            }
            return null;
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
