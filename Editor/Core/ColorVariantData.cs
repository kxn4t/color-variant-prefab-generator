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

        /// <summary>
        /// Live Renderer reference, used for rename-safe equality in the runtime
        /// dictionaries that store overrides (CreatorWindow's _overrides etc.).
        /// Not serialized — identifiers rebuilt across domain reloads come back
        /// through PrefabScanner.
        /// </summary>
        [System.NonSerialized]
        public Renderer renderer;

        /// <summary>Display name for UI (e.g., "Hat/0")</summary>
        public string DisplayName => string.IsNullOrEmpty(rendererPath)
            ? $"(root)/{slotIndex}"
            : $"{objectName}/{slotIndex}";

        public override bool Equals(object obj)
        {
            if (!(obj is MaterialSlotIdentifier other)) return false;
            if (slotIndex != other.slotIndex) return false;

            // Prefer C# reference equality on the Renderer when both identifiers carry
            // one. This survives Hierarchy renames/reparents that would invalidate
            // path-based equality. Identifiers without a Renderer (legacy or cross-prefab
            // metadata) fall back to path. Mixed-mode is intentionally treated as unequal
            // to keep Equals/GetHashCode consistent.
            bool hasRenderer = renderer != null;
            bool otherHasRenderer = other.renderer != null;
            if (hasRenderer != otherHasRenderer) return false;

            return hasRenderer
                ? ReferenceEquals(renderer, other.renderer)
                : rendererPath == other.rendererPath;
        }

        public override int GetHashCode()
        {
            int slotHash = slotIndex.GetHashCode();
            return renderer != null
                ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(renderer) ^ slotHash
                : (rendererPath ?? "").GetHashCode() ^ slotHash;
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

    /// <summary>
    /// Creator window generation mode.
    /// </summary>
    internal enum CreatorMode
    {
        /// <summary>Preserves structural changes (added/removed/renamed GameObjects) alongside material overrides.</summary>
        Standard = 0,

        /// <summary>Material-only overrides (current behavior). Ignores all structural changes on the hierarchy instance.</summary>
        Strict = 1
    }

    /// <summary>
    /// Options controlling which existing-object property changes to include in Standard mode generation.
    /// Only applies to objects that already exist in the base Prefab — added GameObjects always include all changes.
    /// </summary>
    internal class StandardModeOptions
    {
        /// <summary>
        /// When true, the hierarchy instance is saved (via a duplicate) as a Prefab Variant so
        /// every override Unity recognizes is captured: Transform property changes, component
        /// property changes, component add/remove, etc.
        /// When false, the filtered path runs and transfers:
        ///   - GameObject add/remove
        ///   - GameObject-level property overrides on existing objects (m_Name, m_IsActive,
        ///     m_Layer, m_TagString, m_StaticEditorFlags, and any other non-Transform/Component
        ///     properties on the GameObject itself)
        /// while excluding Transform property changes, Component property changes, and
        /// component add/remove.
        /// Material slot modifications are always excluded here and applied by the dedicated
        /// material override system on top of either path.
        /// </summary>
        public bool includePropertyChanges;
    }

    /// <summary>
    /// Request data for Standard mode Prefab Variant generation.
    /// </summary>
    internal class StandardGenerationRequest
    {
        public GameObject basePrefabAsset;
        public GameObject hierarchyInstance;
        public List<MaterialOverride> materialOverrides;
        public StandardModeOptions options;
        public string variantName;
        public string outputPath;
        public string namingTemplate;
    }

    /// <summary>
    /// Summary of structural changes detected on a hierarchy instance relative to its base Prefab.
    /// Used for UI display in Standard mode.
    /// </summary>
    internal class StructuralChangeSummary
    {
        /// <summary>Paths of GameObjects added to the instance (not present in base Prefab).</summary>
        public List<string> addedGameObjects = new List<string>();

        /// <summary>Paths of GameObjects removed from the instance (present in base but deleted/deactivated).</summary>
        public List<string> removedGameObjects = new List<string>();

        /// <summary>Paths of GameObjects whose names were changed.</summary>
        public List<string> renamedGameObjects = new List<string>();

        /// <summary>"path::propertyPath" entries for GameObject-level property changes on existing GameObjects.</summary>
        public List<string> changedGameObjectProperties = new List<string>();

        /// <summary>"path::ComponentType" entries for components added to existing GameObjects.</summary>
        public List<string> addedComponents = new List<string>();

        /// <summary>"path::ComponentType" entries for components removed from existing GameObjects.</summary>
        public List<string> removedComponents = new List<string>();

        /// <summary>
        /// Whether any GameObject-level structural changes (added/removed/renamed GameObjects,
        /// or property changes on existing GameObjects themselves) exist.
        /// These are transferred by both Standard mode paths.
        /// Component-level changes are tracked separately via <see cref="HasComponentChanges"/>
        /// because the filtered path (includePropertyChanges = false) does NOT transfer them,
        /// and conflating the two would let a "components only" diff bypass the empty-changes warning.
        /// </summary>
        public bool HasStructuralChanges =>
            addedGameObjects.Count > 0 || removedGameObjects.Count > 0
            || renamedGameObjects.Count > 0 || changedGameObjectProperties.Count > 0;

        /// <summary>
        /// Whether any component-level changes (added or removed components on existing GameObjects) exist.
        /// Only the native path (includePropertyChanges = true) transfers these.
        /// </summary>
        public bool HasComponentChanges =>
            addedComponents.Count > 0 || removedComponents.Count > 0;
    }
}
