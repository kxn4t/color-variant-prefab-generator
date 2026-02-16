using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kanameliser.ColorVariantGenerator
{
    /// <summary>
    /// Identifies a specific material slot on a specific Renderer within a prefab.
    /// </summary>
    [Serializable]
    internal class MaterialSlotIdentifier
    {
        /// <summary>Relative path from prefab root to the Renderer object</summary>
        public string rendererPath;

        /// <summary>Material slot index (0-based)</summary>
        public int slotIndex;

        /// <summary>Renderer type name for display ("SkinnedMeshRenderer" or "MeshRenderer")</summary>
        public string rendererType;

        /// <summary>Renderer object name (last segment of path, used for matching)</summary>
        public string objectName;

        /// <summary>Hierarchy depth from root (used for matching)</summary>
        public int hierarchyDepth;

        /// <summary>Display name for UI (e.g., "Hat/0")</summary>
        public string DisplayName => string.IsNullOrEmpty(rendererPath)
            ? $"(root)/{slotIndex}"
            : $"{objectName}/{slotIndex}";

        public override bool Equals(object obj)
        {
            if (obj is MaterialSlotIdentifier other)
            {
                return rendererPath == other.rendererPath && slotIndex == other.slotIndex;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return (rendererPath ?? "").GetHashCode() ^ slotIndex.GetHashCode();
        }

        /// <summary>Creates a unique lookup key for dictionary-based matching.</summary>
        public string GetLookupKey() => $"{rendererPath}|{slotIndex}";
    }

    /// <summary>
    /// A material override for a single slot in a color variation.
    /// </summary>
    [Serializable]
    internal class MaterialOverride
    {
        public MaterialSlotIdentifier slot;

        /// <summary>The replacement material. null means "keep base material" (no override).</summary>
        public Material overrideMaterial;
    }

    /// <summary>
    /// Analysis result of a material override extracted from an existing Prefab Variant.
    /// </summary>
    [Serializable]
    internal class MaterialOverrideInfo
    {
        public MaterialSlotIdentifier slot;
        public Material baseMaterial;
        public Material overrideMaterial;
    }

    /// <summary>
    /// Analysis result for a single color variant Prefab Variant.
    /// </summary>
    /// <remarks>
    /// Used by <see cref="VariantAnalyzer.AnalyzeVariant"/>.
    /// Not currently used by the built-in UI.
    /// </remarks>
    [Serializable]
    internal class VariantAnalysisResult
    {
        public string variantName;
        public GameObject basePrefab;
        public List<MaterialOverrideInfo> overrides = new List<MaterialOverrideInfo>();
    }

    /// <summary>
    /// Result of a Renderer matching operation between source and target prefabs.
    /// </summary>
    [Serializable]
    internal class RendererMatchResult
    {
        public MaterialSlotIdentifier sourceSlot;
        public MaterialSlotIdentifier targetSlot;
        public int matchPriority;
        public Material overrideMaterial;

        /// <summary>The target's current (base) material, for UI display.</summary>
        public Material targetBaseMaterial;
    }

    /// <summary>
    /// Result of generating a single Prefab Variant.
    /// </summary>
    internal class GenerationResult
    {
        public bool success;
        public string path;
        public string variantName;
        public string errorMessage;
    }

    /// <summary>
    /// Scanned material slot information including the current base material reference.
    /// </summary>
    [Serializable]
    internal class ScannedMaterialSlot
    {
        public MaterialSlotIdentifier identifier;
        public Material baseMaterial;
    }
}
