using System;
using System.Collections.Generic;
using System.Linq;
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

            // Build set of instance IDs for objects that have actual overrides (visible in Inspector)
            var objectOverrides = PrefabUtility.GetObjectOverrides(hierarchyInstance, true);
            var overriddenInstanceIds = new HashSet<int>();
            foreach (var objOverride in objectOverrides)
            {
                if (objOverride.instanceObject != null)
                    overriddenInstanceIds.Add(objOverride.instanceObject.GetInstanceID());
            }

            var filtered = new List<PropertyModification>();

            foreach (var mod in allModifications)
            {
                if (mod.target == null) continue;

                if (ShouldIncludeModification(mod, hierarchyInstance, addedInstanceIds, overriddenInstanceIds, options))
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
            HashSet<int> overriddenInstanceIds,
            StandardModeOptions options)
        {
            // Always exclude material modifications (handled by material override system)
            if (mod.propertyPath.StartsWith("m_Materials"))
                return false;

            // Added GameObjects: always include all modifications
            if (IsAddedObject(mod.target, addedInstanceIds))
                return true;

            // Only include modifications on objects that have actual overrides
            if (!overriddenInstanceIds.Contains(mod.target.GetInstanceID()))
                return false;

            // Always include: name changes (structural rename)
            if (mod.target is GameObject && mod.propertyPath == "m_Name")
                return true;

            // Always include: active state changes (structural enable/disable)
            if (mod.target is GameObject && mod.propertyPath == "m_IsActive")
                return true;

            // Skip root Transform — placing a prefab in the scene always creates
            // position/rotation overrides on the root, which are not meaningful.
            if (mod.target is Transform t && t == hierarchyInstance.transform)
                return false;

            // Conditionally include: Transform and component property changes
            if (mod.target is Transform || mod.target is Component)
                return options.includePropertyChanges;

            // Include anything else that doesn't fall into the above categories
            return true;
        }

        /// <summary>
        /// Copies all added GameObjects from the source instance to the target instance,
        /// preserving hierarchy structure and all component data.
        /// </summary>
        public static void CopyAddedGameObjects(GameObject sourceInstance, GameObject targetInstance)
        {
            var addedObjects = PrefabUtility.GetAddedGameObjects(sourceInstance);

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
                string removedPath = GetAssetRelativePath(removed.assetGameObject.transform);

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
        /// Applies filtered PropertyModifications to a target instance by remapping object references.
        /// </summary>
        /// <param name="sourceInstance">The original hierarchy instance where modifications were detected.</param>
        /// <param name="targetInstance">The fresh instance to apply modifications to.</param>
        /// <param name="modifications">Pre-filtered modifications to apply.</param>
        public static void ApplyModifications(
            GameObject sourceInstance, GameObject targetInstance, PropertyModification[] modifications)
        {
            if (modifications == null || modifications.Length == 0) return;

            // Get the base modifications already on the target (from InstantiatePrefab)
            var baseModifications = PrefabUtility.GetPropertyModifications(targetInstance);
            var baseMods = baseModifications != null
                ? new List<PropertyModification>(baseModifications)
                : new List<PropertyModification>();

            // Build a mapping from source objects to corresponding target objects
            var objectMapping = BuildObjectMapping(sourceInstance, targetInstance);

            foreach (var mod in modifications)
            {
                if (mod.target == null) continue;

                // Remap the target reference from source to target instance
                Object remappedTarget;
                if (!objectMapping.TryGetValue(mod.target, out remappedTarget))
                {
                    // Object might be on an added GameObject that was already copied
                    // Try to find by path
                    remappedTarget = FindRemappedTarget(sourceInstance, targetInstance, mod.target);
                    if (remappedTarget == null) continue;
                }

                var remappedMod = new PropertyModification
                {
                    target = remappedTarget,
                    propertyPath = mod.propertyPath,
                    value = mod.value,
                    objectReference = RemapObjectReference(mod.objectReference, objectMapping)
                };

                // Replace existing modification or add new one
                bool replaced = false;
                for (int i = 0; i < baseMods.Count; i++)
                {
                    if (baseMods[i].target == remappedTarget && baseMods[i].propertyPath == mod.propertyPath)
                    {
                        baseMods[i] = remappedMod;
                        replaced = true;
                        break;
                    }
                }
                if (!replaced)
                {
                    baseMods.Add(remappedMod);
                }
            }

            PrefabUtility.SetPropertyModifications(targetInstance, baseMods.ToArray());
        }

        /// <summary>
        /// Builds a mapping from objects in the source instance to corresponding objects
        /// in the target instance, based on hierarchy path.
        /// </summary>
        private static Dictionary<Object, Object> BuildObjectMapping(
            GameObject sourceInstance, GameObject targetInstance)
        {
            var mapping = new Dictionary<Object, Object>();

            // Map GameObjects by path
            MapTransformHierarchy(sourceInstance.transform, targetInstance.transform, mapping);

            return mapping;
        }

        private static void MapTransformHierarchy(
            Transform sourceRoot, Transform targetRoot, Dictionary<Object, Object> mapping)
        {
            MapObjectAndComponents(sourceRoot, targetRoot, mapping);

            // Build a name-to-child lookup for target's direct children.
            // This avoids index-based matching which breaks when added/removed
            // objects shift the child order between source and target.
            var targetChildByName = new Dictionary<string, Transform>();
            for (int i = 0; i < targetRoot.childCount; i++)
            {
                var child = targetRoot.GetChild(i);
                // First occurrence wins — duplicate names are rare in prefab hierarchies
                if (!targetChildByName.ContainsKey(child.name))
                    targetChildByName[child.name] = child;
            }

            for (int i = 0; i < sourceRoot.childCount; i++)
            {
                var sourceChild = sourceRoot.GetChild(i);
                if (targetChildByName.TryGetValue(sourceChild.name, out var targetChild))
                {
                    MapTransformHierarchy(sourceChild, targetChild, mapping);
                }
                // else: added object — no corresponding target, skip
            }
        }

        private static void MapObjectAndComponents(
            Transform source, Transform target, Dictionary<Object, Object> mapping)
        {
            mapping[source.gameObject] = target.gameObject;
            mapping[source] = target;

            // Map components by type and order within each type
            var sourceComponents = source.GetComponents<Component>();
            var targetComponents = target.GetComponents<Component>();

            var targetByType = new Dictionary<Type, List<Component>>();
            foreach (var comp in targetComponents)
            {
                if (comp == null) continue;
                var type = comp.GetType();
                if (!targetByType.ContainsKey(type))
                    targetByType[type] = new List<Component>();
                targetByType[type].Add(comp);
            }

            // Track how many components of each type we've matched so far
            var typeIndex = new Dictionary<Type, int>();
            foreach (var comp in sourceComponents)
            {
                if (comp == null) continue;
                var type = comp.GetType();

                if (!targetByType.TryGetValue(type, out var targetList)) continue;

                if (!typeIndex.TryGetValue(type, out int idx))
                    idx = 0;

                if (idx < targetList.Count)
                {
                    mapping[comp] = targetList[idx];
                    typeIndex[type] = idx + 1;
                }
            }
        }

        private static Object FindRemappedTarget(
            GameObject sourceInstance, GameObject targetInstance, Object sourceTarget)
        {
            var targetGO = GetGameObject(sourceTarget);
            if (targetGO == null) return null;

            string path = GetRelativePath(sourceInstance.transform, targetGO.transform);
            Transform targetTransform;
            if (string.IsNullOrEmpty(path))
            {
                targetTransform = targetInstance.transform;
            }
            else
            {
                targetTransform = targetInstance.transform.Find(path);
            }

            if (targetTransform == null) return null;

            if (sourceTarget is GameObject)
                return targetTransform.gameObject;

            if (sourceTarget is Transform)
                return targetTransform;

            if (sourceTarget is Component sourceComp)
            {
                // Find matching component by type and index
                var sourceComps = targetGO.GetComponents(sourceComp.GetType());
                int sourceIndex = Array.IndexOf(sourceComps, sourceComp);
                if (sourceIndex < 0) sourceIndex = 0;

                var targetComps = targetTransform.GetComponents(sourceComp.GetType());
                if (sourceIndex < targetComps.Length)
                    return targetComps[sourceIndex];
            }

            return null;
        }

        private static Object RemapObjectReference(Object reference, Dictionary<Object, Object> mapping)
        {
            if (reference == null) return null;
            Object remapped;
            return mapping.TryGetValue(reference, out remapped) ? remapped : reference;
        }

        /// <summary>
        /// Finds the transform in targetRoot that corresponds to referenceTransform's position
        /// within sourceRoot's hierarchy.
        /// </summary>
        private static Transform FindCorrespondingTransform(
            Transform sourceRoot, Transform targetRoot, Transform referenceTransform)
        {
            if (referenceTransform == sourceRoot) return targetRoot;

            string relativePath = GetRelativePath(sourceRoot, referenceTransform);
            if (string.IsNullOrEmpty(relativePath)) return targetRoot;

            return targetRoot.Find(relativePath);
        }

        /// <summary>
        /// Gets the relative path from root to target transform.
        /// Returns empty string if target is the root itself.
        /// </summary>
        private static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root) return "";

            var parts = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            if (current != root) return target.name; // Fallback if not a descendant

            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>
        /// Gets the relative path of an asset GameObject within its prefab hierarchy.
        /// </summary>
        private static string GetAssetRelativePath(Transform assetTransform)
        {
            var parts = new List<string>();
            var current = assetTransform;

            // Walk up to root (parent == null for root of prefab asset)
            while (current != null && current.parent != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }

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

        private static GameObject GetGameObject(Object obj)
        {
            if (obj is GameObject go) return go;
            if (obj is Component comp) return comp.gameObject;
            return null;
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
