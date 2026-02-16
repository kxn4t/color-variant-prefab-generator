using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kanameliser.ColorVariantGenerator
{
    /// <summary>
    /// Shared UI utilities used across editor windows.
    /// </summary>
    internal static class EditorUIUtility
    {
        private const int PreviewLoadInitialDelayMs = 100;
        private const int PreviewLoadRetryIntervalMs = 200;
        private const int PreviewLoadMaxAttempts = 20;

        // ── Naming ──────────────────────────────────────
        public const string DefaultNamingTemplate = "{BaseName}_{VariantName}";

        // ── UI Symbols ──────────────────────────────────
        public const string Ellipsis = "\u2026";
        public const string Arrow = "\u2192";
        public const string Cross = "\u00d7";
        public const string DropdownArrow = "\u25bc";
        public const string Pencil = "\u270e";
        public const string Warning = "\u26a0";
        public const string HorizontalRule = "\u2500\u2500\u2500";
        public const string EmDash = "\u2014";
        public const string Refresh = "\u21bb";

        /// <summary>
        /// Deduplicates a list of variant names by appending _2, _3, etc. to duplicates.
        /// The first occurrence keeps its original name.
        /// </summary>
        public static List<string> DeduplicateVariantNames(List<string> names)
        {
            // First pass: count occurrences to identify duplicates
            var totalCount = new Dictionary<string, int>();
            foreach (var name in names)
            {
                totalCount[name] = totalCount.TryGetValue(name, out int c) ? c + 1 : 1;
            }

            // Second pass: assign suffixes (_1, _2, ...) to all occurrences of duplicated names
            var result = new List<string>();
            var seenCount = new Dictionary<string, int>();
            foreach (var name in names)
            {
                seenCount[name] = seenCount.TryGetValue(name, out int c) ? c + 1 : 1;
                result.Add(totalCount[name] > 1 ? $"{name}_{seenCount[name]}" : name);
            }
            return result;
        }

        /// <summary>
        /// Resolves a file name from a naming template by replacing placeholders.
        /// </summary>
        public static string ResolveFileName(string template, string baseName, string variantName)
        {
            if (baseName != null && baseName.EndsWith("_Base", StringComparison.OrdinalIgnoreCase))
                baseName = baseName.Substring(0, baseName.Length - 5);

            return template.Replace("{BaseName}", baseName).Replace("{VariantName}", variantName);
        }

        /// <summary>
        /// Normalizes path separators to forward slashes for Unity asset paths.
        /// </summary>
        public static string NormalizePath(string path)
        {
            return path?.Replace('\\', '/');
        }

        /// <summary>
        /// Checks if the current drag operation contains a valid project folder.
        /// </summary>
        public static bool TryGetDraggedFolderPath(out string path)
        {
            path = null;
            if (DragAndDrop.objectReferences.Length == 0 ||
                DragAndDrop.objectReferences[0] is not DefaultAsset) return false;
            path = AssetDatabase.GetAssetPath(DragAndDrop.objectReferences[0]);
            return AssetDatabase.IsValidFolder(path);
        }

        /// <summary>
        /// Converts an absolute file system path to a project-relative path (Assets/...).
        /// Returns the original path if it's not under the project's Assets folder.
        /// </summary>
        public static string ToProjectRelativePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return absolutePath;
            string dataPath = Application.dataPath;
            if (absolutePath.StartsWith(dataPath))
            {
                return "Assets" + absolutePath.Substring(dataPath.Length);
            }
            return absolutePath;
        }

        /// <summary>
        /// Sets a material preview image asynchronously, retrying if the preview is not yet available.
        /// </summary>
        public static void SetMaterialPreview(Image imageElement, Material material)
        {
            if (material == null) { imageElement.image = null; return; }
            var preview = AssetPreview.GetAssetPreview(material);
            if (preview != null)
            {
                imageElement.image = preview;
            }
            else
            {
                // Preview not ready yet (Unity generates them asynchronously).
                // Poll periodically until available or max attempts reached.
                AssetPreview.SetPreviewTextureCacheSize(256);
                int attempts = 0;
                imageElement.schedule.Execute(() =>
                {
                    attempts++;
                    if (material == null) return;
                    var delayedPreview = AssetPreview.GetAssetPreview(material);
                    if (delayedPreview != null)
                    {
                        imageElement.image = delayedPreview;
                    }
                }).StartingIn(PreviewLoadInitialDelayMs)
                  .Every(PreviewLoadRetryIntervalMs)
                  .Until(() => imageElement.image != null || material == null || attempts >= PreviewLoadMaxAttempts);
            }
        }

        /// <summary>
        /// Returns true if the path is a valid Unity asset path (starts with "Assets/" or "Packages/").
        /// </summary>
        public static bool IsValidOutputPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string normalized = NormalizePath(path);
            return normalized.StartsWith("Assets/") || normalized == "Assets"
                || normalized.StartsWith("Packages/") || normalized == "Packages";
        }

        /// <summary>
        /// Registers DragUpdated + DragPerform callbacks on an element to accept folder drops
        /// and set the value on the given TextField.
        /// </summary>
        public static void RegisterFolderDrop(VisualElement target, TextField pathField)
        {
            target.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (TryGetDraggedFolderPath(out _))
                    DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            });

            target.RegisterCallback<DragPerformEvent>(evt =>
            {
                if (TryGetDraggedFolderPath(out var folderPath))
                {
                    DragAndDrop.AcceptDrag();
                    pathField.value = folderPath;
                }
            });
        }

        // ── Output Section Helpers ───────────────────────

        /// <summary>
        /// Creates the standard Output Path row (label + TextField + browse button + folder D&amp;D).
        /// </summary>
        public static VisualElement CreateOutputPathRow(
            out TextField outputPathField, Action onValueChanged, Func<string> getDefaultPath)
        {
            var row = new VisualElement();
            row.AddToClassList("output-row");

            var label = new Label("common.outputPath");
            label.AddToClassList("output-label");
            label.AddToClassList("ndmf-tr");
            row.Add(label);

            var field = new TextField();
            field.AddToClassList("output-field");
            field.RegisterValueChangedCallback(_ => onValueChanged?.Invoke());
            row.Add(field);

            var capturedField = field;
            var browseBtn = new Button(() => BrowseOutputPath(capturedField, getDefaultPath))
            {
                text = Ellipsis
            };
            browseBtn.AddToClassList("browser-browse-button");
            row.Add(browseBtn);

            RegisterFolderDrop(row, field);

            outputPathField = field;
            return row;
        }

        /// <summary>
        /// Creates the standard Naming Template row (label + TextField).
        /// </summary>
        public static VisualElement CreateNamingTemplateRow(
            out TextField namingTemplateField, Action onValueChanged)
        {
            var row = new VisualElement();
            row.AddToClassList("output-row");

            var label = new Label("common.naming");
            label.AddToClassList("output-label");
            label.AddToClassList("ndmf-tr");
            row.Add(label);

            var field = new TextField { value = DefaultNamingTemplate };
            field.AddToClassList("output-field");
            field.RegisterValueChangedCallback(_ => onValueChanged?.Invoke());
            row.Add(field);

            namingTemplateField = field;
            return row;
        }

        /// <summary>
        /// Opens a folder selection dialog and sets the value on the given TextField.
        /// </summary>
        public static void BrowseOutputPath(TextField pathField, Func<string> getDefaultPath)
        {
            string defaultPath = pathField.value;
            if (string.IsNullOrEmpty(defaultPath))
            {
                defaultPath = getDefaultPath?.Invoke() ?? "Assets";
            }

            string selectedPath = EditorUtility.OpenFolderPanel(Localization.S("common.selectOutputFolder"), defaultPath, "");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                string relativePath = ToProjectRelativePath(selectedPath);
                if (!IsValidOutputPath(relativePath))
                {
                    EditorUtility.DisplayDialog(Localization.S("common.error"),
                        Localization.S("common.error.outputFolderInvalid"), "OK");
                    return;
                }
                pathField.value = relativePath;
            }
        }

        /// <summary>
        /// Resolves and validates the output path. Returns the resolved path, or null if invalid
        /// (with an error dialog shown to the user).
        /// </summary>
        public static string ResolveAndValidateOutputPath(string outputFieldValue, Func<string> fallbackPath)
        {
            string outputPath = outputFieldValue;
            if (string.IsNullOrEmpty(outputPath))
            {
                outputPath = fallbackPath?.Invoke() ?? "";
            }

            if (!IsValidOutputPath(outputPath))
            {
                EditorUtility.DisplayDialog(Localization.S("common.error"),
                    Localization.S("common.error.outputPathInvalid"), "OK");
                return null;
            }

            return outputPath;
        }

        /// <summary>
        /// Checks if a single output file already exists. Returns true if it's safe to proceed
        /// (file doesn't exist or user confirmed overwrite).
        /// </summary>
        public static bool ConfirmSingleFileOverwrite(string outputPath, string namingTemplate, string baseName, string variantName)
        {
            string fileName = ResolveFileName(namingTemplate, baseName, variantName);
            string fullPath = NormalizePath(Path.Combine(outputPath, fileName + ".prefab"));

            if (File.Exists(fullPath))
            {
                return EditorUtility.DisplayDialog(
                    Localization.S("common.fileExists"),
                    Localization.S("common.fileExists.message", fileName),
                    Localization.S("common.overwrite"), Localization.S("common.cancel"));
            }

            return true;
        }
    }
}
