using System.Collections.Generic;
using UnityEngine;

namespace Kanameliser.ColorVariantGenerator
{
    /// <summary>
    /// Scans a prefab or scene instance for all Renderer components and their material slots.
    /// </summary>
    internal static class PrefabScanner
    {
        /// <summary>
        /// Scans all Renderers under the given root and returns their material slot information.
        /// </summary>
        public static List<ScannedMaterialSlot> ScanRenderers(GameObject root)
        {
            var result = new List<ScannedMaterialSlot>();
            if (root == null) return result;

            // includeInactive=true to catch renderers on disabled GameObjects
            var renderers = root.GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;

                string path = GetRelativePathFromRoot(renderer.transform, root.transform);
                string objectName = renderer.gameObject.name;
                string rendererType = renderer.GetType().Name;
                int depth = string.IsNullOrEmpty(path) ? 0 : path.Split('/').Length;

                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var slot = new ScannedMaterialSlot
                    {
                        identifier = new MaterialSlotIdentifier
                        {
                            rendererPath = path,
                            slotIndex = i,
                            rendererType = rendererType,
                            objectName = objectName,
                            hierarchyDepth = depth
                        },
                        baseMaterial = materials[i]
                    };
                    result.Add(slot);
                }
            }

            return result;
        }

        /// <summary>
        /// Gets the relative path from a root transform to a child transform.
        /// </summary>
        public static string GetRelativePathFromRoot(Transform transform, Transform root)
        {
            if (transform == null || root == null) return "";
            if (transform == root) return "";

            var parts = new List<string>();
            var current = transform;

            while (current != null && current != root)
            {
                parts.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", parts);
        }
    }
}
