#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AIToUGUI.Editor
{
    internal sealed class AIToUGUISvgSpriteImportResult
    {
        public int preparedCount;
        public int convertedCount;
        public string spritesRoot = string.Empty;
        public readonly List<string> warnings = new List<string>();
        public readonly List<string> errors = new List<string>();

        public void Merge(AIToUGUISvgSpriteImportResult other)
        {
            if (other == null)
            {
                return;
            }

            preparedCount += other.preparedCount;
            convertedCount += other.convertedCount;
            if (string.IsNullOrWhiteSpace(spritesRoot) && !string.IsNullOrWhiteSpace(other.spritesRoot))
            {
                spritesRoot = other.spritesRoot;
            }

            warnings.AddRange(other.warnings);
            errors.AddRange(other.errors);
        }
    }

    internal sealed class AIToUGUISvgSpriteImportSettings
    {
        public int targetResolution = 512;

        public int GetClampedResolution()
        {
            return Mathf.Clamp(targetResolution, 32, 4096);
        }
    }

    internal static class AIToUGUISvgSpriteAssetUtility
    {
        private static readonly Color32 BrowserSentinelColor = new Color32(255, 0, 255, 255);
        private static readonly Regex CssUrlRegex = new Regex("url\\((?<path>[^)]+)\\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NumberRegex = new Regex("-?\\d+(?:\\.\\d+)?", RegexOptions.Compiled);
        private static readonly string[] RasterExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".psd" };
        private const string VectorUtilsTypeName = "Unity.VectorGraphics.VectorUtils, Unity.VectorGraphics";
        private const string SvgImporterTypeName = "Unity.VectorGraphics.Editor.SVGImporter, Unity.VectorGraphics.Editor";
        private const string VectorMaterialAssetPath = "Packages/com.unity.vectorgraphics/Runtime/Materials/Unlit_Vector.mat";
        private const string VectorGradientMaterialAssetPath = "Packages/com.unity.vectorgraphics/Runtime/Materials/Unlit_VectorGradient.mat";
        private const string SvgRenderStampPrefix = "AIToUGUI:SVG";
        private const string SvgRenderStampVersion = "3";
        private const string SvgRenderBackendVectorGraphics = "uvg";
        private const string SvgRenderBackendBrowser = "browser";
        private static readonly string[] BrowserExecutableCandidates =
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
        };

        private readonly struct AIToUGUISourcePathContext
        {
            public AIToUGUISourcePathContext(string siteId, string siteRootAssetPath, string sourceRootAssetPath, string pageSourceRelativePath)
            {
                SiteId = siteId ?? string.Empty;
                SiteRootAssetPath = siteRootAssetPath ?? string.Empty;
                SourceRootAssetPath = sourceRootAssetPath ?? string.Empty;
                PageSourceRelativePath = pageSourceRelativePath ?? string.Empty;
            }

            public string SiteId { get; }
            public string SiteRootAssetPath { get; }
            public string SourceRootAssetPath { get; }
            public string PageSourceRelativePath { get; }
        }

        public static AIToUGUISvgSpriteImportResult PrepareAssetsForParsedBundle(
            AIToUGUIParsedBundle parsedBundle,
            bool forceRebuild = false,
            AIToUGUISvgSpriteImportSettings settings = null)
        {
            var result = new AIToUGUISvgSpriteImportResult();
            if (parsedBundle?.Pages == null)
            {
                return result;
            }

            var convertedAny = false;
            for (var i = 0; i < parsedBundle.Pages.Count; i++)
            {
                var pageResult = PrepareAssetsForCompiledPage(parsedBundle.Pages[i], forceRebuild, settings);
                convertedAny |= pageResult.convertedCount > 0;
                result.Merge(pageResult);
            }

            if (convertedAny)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return result;
        }

        public static AIToUGUISvgSpriteImportResult PrepareAssetsForCompiledPage(
            AIToUGUICompiledPage page,
            bool forceRebuild = false,
            AIToUGUISvgSpriteImportSettings settings = null)
        {
            var result = new AIToUGUISvgSpriteImportResult();
            if (page?.Root == null)
            {
                return result;
            }

            if (!TryResolveSourcePathContext(page, out var context))
            {
                result.warnings.Add($"[AIToUGUI] Unable to resolve source root for page '{page.PageId}'.");
                return result;
            }

            result.spritesRoot = BuildSpritesRootAssetPath(context.SiteId);
            var assetRefs = new List<AIToUGUIAssetReference>();
            CollectAssetRefs(page.Root, assetRefs);
            var preparedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < assetRefs.Count; i++)
            {
                var assetRef = assetRefs[i];
                if (assetRef == null)
                {
                    continue;
                }

                if (!TryPrepareAssetRef(page, context, assetRef, forceRebuild, result, preparedPaths, settings))
                {
                    continue;
                }

                result.preparedCount++;
            }

            return result;
        }

        public static bool TryResolveSpriteAssetPath(AIToUGUICompiledPage page, AIToUGUIAssetReference assetRef, out string spriteAssetPath)
        {
            spriteAssetPath = string.Empty;
            if (assetRef == null)
            {
                return false;
            }

            var explicitPath = NormalizeAssetPath(assetRef.logicalAssetPath);
            if (!TryResolveSourcePathContext(page, out var context) ||
                !TryResolveSourceAssetPath(page, context, assetRef, out var sourceAssetPath))
            {
                if (!string.IsNullOrWhiteSpace(explicitPath))
                {
                    spriteAssetPath = explicitPath;
                    return true;
                }

                return false;
            }

            var extension = Path.GetExtension(sourceAssetPath)?.ToLowerInvariant();
            if (string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase))
            {
                spriteAssetPath = BuildSpriteAssetPath(context, sourceAssetPath);
                return true;
            }

            if (IsRasterExtension(extension))
            {
                spriteAssetPath = !string.IsNullOrWhiteSpace(explicitPath) ? explicitPath : sourceAssetPath;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                spriteAssetPath = explicitPath;
                return true;
            }

            return false;
        }

        public static void LogResult(AIToUGUISvgSpriteImportResult result, string operationLabel)
        {
            if (result == null)
            {
                return;
            }

            var builder = new StringBuilder();
            builder.Append("[AIToUGUI] ")
                .Append(operationLabel)
                .Append(": prepared=")
                .Append(result.preparedCount)
                .Append(", converted=")
                .Append(result.convertedCount);

            if (!string.IsNullOrWhiteSpace(result.spritesRoot))
            {
                builder.Append(", spritesRoot=").Append(result.spritesRoot);
            }

            if (result.errors.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Errors:");
                for (var i = 0; i < result.errors.Count; i++)
                {
                    builder.Append("- ").AppendLine(result.errors[i]);
                }
            }

            if (result.warnings.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Warnings:");
                for (var i = 0; i < result.warnings.Count; i++)
                {
                    builder.Append("- ").AppendLine(result.warnings[i]);
                }
            }

            if (result.errors.Count > 0)
            {
                Debug.LogWarning(builder.ToString().TrimEnd());
                return;
            }

            Debug.Log(builder.ToString().TrimEnd());
        }

        private static bool TryPrepareAssetRef(
            AIToUGUICompiledPage page,
            AIToUGUISourcePathContext context,
            AIToUGUIAssetReference assetRef,
            bool forceRebuild,
            AIToUGUISvgSpriteImportResult result,
            HashSet<string> preparedPaths,
            AIToUGUISvgSpriteImportSettings settings)
        {
            if (!TryResolveSourceAssetPath(page, context, assetRef, out var sourceAssetPath))
            {
                return false;
            }

            var extension = Path.GetExtension(sourceAssetPath)?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(extension))
            {
                return false;
            }

            if (string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase))
            {
                var spriteAssetPath = BuildSpriteAssetPath(context, sourceAssetPath);
                assetRef.logicalAssetPath = spriteAssetPath;

                if (preparedPaths.Contains(spriteAssetPath))
                {
                    return true;
                }

                preparedPaths.Add(spriteAssetPath);
                EnsureAssetFolder(Path.GetDirectoryName(spriteAssetPath)?.Replace("\\", "/"));

                if (!TryGeneratePngSprite(sourceAssetPath, spriteAssetPath, assetRef, forceRebuild, settings, out var convertedThisRun, out var renderStamp, out var errorMessage))
                {
                    if (!string.IsNullOrWhiteSpace(errorMessage))
                    {
                        result.errors.Add($"Failed to convert '{sourceAssetPath}' -> '{spriteAssetPath}': {errorMessage}");
                    }

                    return false;
                }

                if (convertedThisRun)
                {
                    result.convertedCount++;
                    AssetDatabase.ImportAsset(spriteAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                }

                ApplySpriteImporter(spriteAssetPath, assetRef, sourceAssetPath, renderStamp);
                return true;
            }

            if (!IsRasterExtension(extension))
            {
                return false;
            }

            assetRef.logicalAssetPath = sourceAssetPath;
            if (preparedPaths.Contains(sourceAssetPath))
            {
                return true;
            }

            preparedPaths.Add(sourceAssetPath);
            ApplySpriteImporter(sourceAssetPath, assetRef, sourceAssetPath);
            return true;
        }

        private static bool TryResolveSourcePathContext(AIToUGUICompiledPage page, out AIToUGUISourcePathContext context)
        {
            context = default;
            if (page == null)
            {
                return false;
            }

            if (page.SourceSite != null)
            {
                var sourceRoot = AIToUGUISiteImportUtility.ResolveSourceRootFolder(page.SourceSite, page.SourcePage);
                var siteRoot = ResolveSiteRootFromSourceRoot(sourceRoot);
                var pageRelativePath = page.SourcePage != null ? page.SourcePage.sourceRelativePath : string.Empty;
                if (!string.IsNullOrWhiteSpace(sourceRoot) && !string.IsNullOrWhiteSpace(siteRoot))
                {
                    context = new AIToUGUISourcePathContext(page.SiteId, siteRoot, sourceRoot, pageRelativePath);
                    return true;
                }
            }

            var bundleJsonAssetPath = page.SourceBundle != null && page.SourceBundle.bundleJson != null
                ? AssetDatabase.GetAssetPath(page.SourceBundle.bundleJson)
                : page.SourceBundle != null ? page.SourceBundle.bundleJsonAssetPath : string.Empty;
            bundleJsonAssetPath = NormalizeAssetPath(bundleJsonAssetPath);
            if (string.IsNullOrWhiteSpace(bundleJsonAssetPath))
            {
                return false;
            }

            var siteRootAssetPath = NormalizeAssetPath(Path.GetDirectoryName(bundleJsonAssetPath)?.Replace("\\", "/"));
            if (string.IsNullOrWhiteSpace(siteRootAssetPath))
            {
                return false;
            }

            var sourceRootAssetPath = ResolveSourceRootForSitePackage(siteRootAssetPath);
            var pageRelative = ResolvePageSourceRelativePath(sourceRootAssetPath, page.PageId);
            context = new AIToUGUISourcePathContext(page.SiteId, siteRootAssetPath, sourceRootAssetPath, pageRelative);
            return true;
        }

        private static string ResolveSourceRootForSitePackage(string siteRootAssetPath)
        {
            if (string.IsNullOrWhiteSpace(siteRootAssetPath))
            {
                return string.Empty;
            }

            var nestedSource = NormalizeAssetPath($"{siteRootAssetPath}/source");
            var nestedManifest = NormalizeAssetPath($"{nestedSource}/site.json");
            if (!string.IsNullOrWhiteSpace(nestedSource) &&
                AssetDatabase.IsValidFolder(nestedSource) &&
                AssetDatabase.LoadAssetAtPath<TextAsset>(nestedManifest) != null)
            {
                return nestedSource;
            }

            return siteRootAssetPath;
        }

        private static string ResolvePageSourceRelativePath(string sourceRootAssetPath, string pageId)
        {
            if (string.IsNullOrWhiteSpace(sourceRootAssetPath) || string.IsNullOrWhiteSpace(pageId))
            {
                return string.Empty;
            }

            var manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>($"{sourceRootAssetPath}/site.json");
            if (!AIToUGUISiteImportUtility.TryLoadManifest(manifestAsset, out var manifest) || manifest.pages == null)
            {
                return string.Empty;
            }

            for (var i = 0; i < manifest.pages.Length; i++)
            {
                var page = manifest.pages[i];
                if (page != null && string.Equals(page.pageId, pageId, StringComparison.Ordinal))
                {
                    return page.html ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private static void CollectAssetRefs(AIToUGUICompiledNode node, List<AIToUGUIAssetReference> buffer)
        {
            if (node == null || buffer == null)
            {
                return;
            }

            if (node.AssetRefs != null)
            {
                for (var i = 0; i < node.AssetRefs.Count; i++)
                {
                    var assetRef = node.AssetRefs[i];
                    if (assetRef != null)
                    {
                        buffer.Add(assetRef);
                    }
                }
            }

            if (node.Children == null)
            {
                return;
            }

            for (var i = 0; i < node.Children.Count; i++)
            {
                CollectAssetRefs(node.Children[i], buffer);
            }
        }

        private static bool TryResolveSourceAssetPath(
            AIToUGUICompiledPage page,
            AIToUGUISourcePathContext context,
            AIToUGUIAssetReference assetRef,
            out string sourceAssetPath)
        {
            sourceAssetPath = string.Empty;
            var rawSource = ExtractRawAssetPath(assetRef != null ? assetRef.source : string.Empty);
            if (string.IsNullOrWhiteSpace(rawSource) ||
                rawSource.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                rawSource.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                rawSource.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                rawSource.StartsWith("#", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            rawSource = rawSource.Replace("\\", "/").Trim();
            if (rawSource.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                sourceAssetPath = NormalizeAssetPath(rawSource);
                return AssetDatabase.LoadMainAssetAtPath(sourceAssetPath) != null ||
                       File.Exists(AssetPathToAbsolutePath(sourceAssetPath));
            }

            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(context.SourceRootAssetPath))
            {
                AddCandidateFromRelativePath(candidates, context.SourceRootAssetPath, rawSource);
            }

            var pageDirectoryRelative = Path.GetDirectoryName(context.PageSourceRelativePath)?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(context.SourceRootAssetPath) && !string.IsNullOrWhiteSpace(pageDirectoryRelative))
            {
                var pageDirectoryAssetPath = CombineAssetPath(context.SourceRootAssetPath, pageDirectoryRelative);
                AddCandidateFromRelativePath(candidates, pageDirectoryAssetPath, rawSource);
            }

            if (!string.IsNullOrWhiteSpace(context.SiteRootAssetPath) &&
                rawSource.StartsWith("source/", StringComparison.OrdinalIgnoreCase))
            {
                AddCandidateFromRelativePath(candidates, context.SiteRootAssetPath, rawSource);
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (AssetDatabase.LoadMainAssetAtPath(candidate) != null || File.Exists(AssetPathToAbsolutePath(candidate)))
                {
                    sourceAssetPath = candidate;
                    return true;
                }
            }

            var fileName = Path.GetFileName(rawSource);
            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(context.SourceRootAssetPath))
            {
                return false;
            }

            var fileStem = Path.GetFileNameWithoutExtension(fileName);
            var guids = AssetDatabase.FindAssets(fileStem, new[] { context.SourceRootAssetPath });
            for (var i = 0; i < guids.Length; i++)
            {
                var assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (string.Equals(Path.GetFileName(assetPath), fileName, StringComparison.OrdinalIgnoreCase))
                {
                    sourceAssetPath = assetPath;
                    return true;
                }
            }

            return false;
        }

        private static void AddCandidateFromRelativePath(List<string> candidates, string baseAssetPath, string relativePath)
        {
            if (candidates == null || string.IsNullOrWhiteSpace(baseAssetPath) || string.IsNullOrWhiteSpace(relativePath))
            {
                return;
            }

            var baseAbsolutePath = AssetPathToAbsolutePath(baseAssetPath);
            if (string.IsNullOrWhiteSpace(baseAbsolutePath))
            {
                return;
            }

            var normalizedRelativePath = relativePath.Replace("\\", "/").TrimStart('/');
            var combinedAbsolutePath = Path.GetFullPath(Path.Combine(baseAbsolutePath, normalizedRelativePath.Replace("/", Path.DirectorySeparatorChar.ToString())));
            var candidateAssetPath = AbsolutePathToAssetPath(combinedAbsolutePath);
            if (string.IsNullOrWhiteSpace(candidateAssetPath))
            {
                return;
            }

            candidateAssetPath = NormalizeAssetPath(candidateAssetPath);
            if (!candidates.Contains(candidateAssetPath))
            {
                candidates.Add(candidateAssetPath);
            }
        }

        private static string BuildSpriteAssetPath(AIToUGUISourcePathContext context, string sourceAssetPath)
        {
            var normalizedSource = NormalizeAssetPath(sourceAssetPath);
            var normalizedSourceRoot = NormalizeAssetPath(context.SourceRootAssetPath);
            var relativePath = !string.IsNullOrWhiteSpace(normalizedSourceRoot) &&
                               normalizedSource.StartsWith(normalizedSourceRoot + "/", StringComparison.OrdinalIgnoreCase)
                ? normalizedSource.Substring(normalizedSourceRoot.Length + 1)
                : Path.GetFileName(normalizedSource);
            relativePath = NormalizeSpriteRelativePath(relativePath, normalizedSource);
            var spriteRelativePath = ChangeExtension(relativePath, ".png");
            return NormalizeAssetPath($"{BuildSpritesRootAssetPath(context.SiteId)}/{spriteRelativePath}");
        }

        private static string BuildSpritesRootAssetPath(string siteId)
        {
            var resolvedSiteId = string.IsNullOrWhiteSpace(siteId) ? "Site" : siteId.Trim();
            return NormalizeAssetPath(AIToUGUIGeneratedAssetPaths.GetSpritesRoot(resolvedSiteId));
        }

        private static string NormalizeSpriteRelativePath(string relativePath, string normalizedSourcePath)
        {
            var normalized = (relativePath ?? string.Empty).Replace("\\", "/").TrimStart('/');
            normalized = StripLeadingPathSegment(normalized, "source");
            normalized = StripLeadingPathSegment(normalized, "assets");
            return string.IsNullOrWhiteSpace(normalized) ? Path.GetFileName(normalizedSourcePath) : normalized;
        }

        private static string StripLeadingPathSegment(string value, string segment)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(segment))
            {
                return value ?? string.Empty;
            }

            var normalizedValue = value.Replace("\\", "/").TrimStart('/');
            var normalizedSegment = segment.Trim().Trim('/').Replace("\\", "/");
            if (normalizedValue.StartsWith(normalizedSegment + "/", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedValue.Substring(normalizedSegment.Length + 1);
            }

            return normalizedValue;
        }

        private static string ChangeExtension(string relativePath, string extension)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return $"sprite{extension}";
            }

            var directory = Path.GetDirectoryName(relativePath)?.Replace("\\", "/");
            var fileName = Path.GetFileNameWithoutExtension(relativePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return $"{fileName}{extension}";
            }

            return $"{directory}/{fileName}{extension}";
        }

        private static bool TryGeneratePngSprite(
            string svgAssetPath,
            string outputAssetPath,
            AIToUGUIAssetReference assetRef,
            bool forceRebuild,
            AIToUGUISvgSpriteImportSettings settings,
            out bool convertedThisRun,
            out string renderStamp,
            out string errorMessage)
        {
            convertedThisRun = false;
            renderStamp = string.Empty;
            errorMessage = string.Empty;

            var sourceAbsolutePath = AssetPathToAbsolutePath(svgAssetPath);
            var outputAbsolutePath = AssetPathToAbsolutePath(outputAssetPath);
            if (string.IsNullOrWhiteSpace(sourceAbsolutePath) || string.IsNullOrWhiteSpace(outputAbsolutePath))
            {
                errorMessage = "Unable to resolve absolute filesystem paths.";
                return false;
            }

            var rasterSize = ResolveRasterSize(sourceAbsolutePath, assetRef, settings);
            var useUnityVectorGraphics = IsUnityVectorGraphicsAvailable();
            renderStamp = BuildSvgRenderStamp(
                rasterSize,
                useUnityVectorGraphics ? SvgRenderBackendVectorGraphics : SvgRenderBackendBrowser);

            if (!forceRebuild && File.Exists(outputAbsolutePath))
            {
                var sourceTime = File.GetLastWriteTimeUtc(sourceAbsolutePath);
                var outputTime = File.GetLastWriteTimeUtc(outputAbsolutePath);
                if (outputTime >= sourceTime && HasMatchingSvgRenderStamp(outputAssetPath, renderStamp))
                {
                    return true;
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputAbsolutePath) ?? Path.GetTempPath());

            if (useUnityVectorGraphics)
            {
                if (TryRenderWithUnityVectorGraphics(svgAssetPath, outputAbsolutePath, rasterSize, out errorMessage))
                {
                    convertedThisRun = true;
                    return true;
                }

                errorMessage = $"Unity Vector Graphics render failed: {errorMessage}";
                return false;
            }

            var browserPath = ResolveBrowserExecutablePath();
            if (string.IsNullOrWhiteSpace(browserPath))
            {
                errorMessage = "Unity Vector Graphics render path was unavailable and no supported headless browser was found.";
                return false;
            }

            var tempHtmlPath = Path.Combine(Path.GetTempPath(), $"AIToUGUI_SvgSprite_{Guid.NewGuid():N}.html");
            try
            {
                File.WriteAllText(tempHtmlPath, BuildRasterHtml(sourceAbsolutePath), Encoding.UTF8);
                var startInfo = new ProcessStartInfo
                {
                    FileName = browserPath,
                    Arguments =
                        $"--headless=new --disable-gpu --hide-scrollbars --allow-file-access-from-files " +
                        $"--default-background-color=00000000 --window-size={rasterSize.x},{rasterSize.y} " +
                        $"--screenshot=\"{outputAbsolutePath}\" \"{new Uri(tempHtmlPath).AbsoluteUri}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    errorMessage = "Failed to launch the headless browser process.";
                    return false;
                }

                if (!process.WaitForExit(30000))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                    }

                    errorMessage = "SVG rasterization timed out after 30 seconds.";
                    return false;
                }

                if (!File.Exists(outputAbsolutePath) || new FileInfo(outputAbsolutePath).Length <= 0L)
                {
                    var stderr = process.StandardError.ReadToEnd().Trim();
                    errorMessage = string.IsNullOrWhiteSpace(stderr)
                        ? $"Headless browser exited with code {process.ExitCode}."
                        : stderr;
                    return false;
                }

                if (!TryNormalizeBrowserRasterOutput(outputAbsolutePath, rasterSize, out errorMessage))
                {
                    return false;
                }

                convertedThisRun = true;
                return true;
            }
            catch (Exception exception)
            {
                errorMessage = exception.Message;
                return false;
            }
            finally
            {
                if (File.Exists(tempHtmlPath))
                {
                    try
                    {
                        File.Delete(tempHtmlPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static bool IsUnityVectorGraphicsAvailable()
        {
            if (Type.GetType(VectorUtilsTypeName, false) == null)
            {
                return false;
            }

            return AssetDatabase.LoadAssetAtPath<Material>(VectorMaterialAssetPath) != null ||
                   AssetDatabase.LoadAssetAtPath<Material>(VectorGradientMaterialAssetPath) != null ||
                   AssetDatabase.LoadMainAssetAtPath(VectorMaterialAssetPath) as Material != null ||
                   AssetDatabase.LoadMainAssetAtPath(VectorGradientMaterialAssetPath) as Material != null;
        }

        private static string BuildSvgRenderStamp(Vector2Int rasterSize, string backend)
        {
            return $"{SvgRenderStampPrefix}:{SvgRenderStampVersion}:{backend}:{rasterSize.x}x{rasterSize.y}";
        }

        private static bool HasMatchingSvgRenderStamp(string outputAssetPath, string expectedRenderStamp)
        {
            if (string.IsNullOrWhiteSpace(outputAssetPath) || string.IsNullOrWhiteSpace(expectedRenderStamp))
            {
                return false;
            }

            var importer = AssetImporter.GetAtPath(outputAssetPath);
            return importer != null &&
                   string.Equals(importer.userData, expectedRenderStamp, StringComparison.Ordinal);
        }

        private static void ApplySpriteImporter(string assetPath, AIToUGUIAssetReference assetRef, string sourceAssetPath, string renderStamp = null)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            var desiredPpu = assetRef != null && assetRef.pixelsPerUnit > 0f ? assetRef.pixelsPerUnit : 100f;
            var desiredWrapMode = assetRef != null && assetRef.importMode == AIToUGUIAssetImportMode.Tile
                ? TextureWrapMode.Repeat
                : TextureWrapMode.Clamp;
            var desiredFilterMode = FilterMode.Bilinear;
            var desiredBorder = ResolveImportedBorder(assetPath, assetRef, sourceAssetPath);

            var changed = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                changed = true;
            }

            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                changed = true;
            }

            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                changed = true;
            }

            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                changed = true;
            }

            if (importer.isReadable)
            {
                importer.isReadable = false;
                changed = true;
            }

            if (importer.wrapMode != desiredWrapMode)
            {
                importer.wrapMode = desiredWrapMode;
                changed = true;
            }

            if (importer.filterMode != desiredFilterMode)
            {
                importer.filterMode = desiredFilterMode;
                changed = true;
            }

            if (!Mathf.Approximately(importer.spritePixelsPerUnit, desiredPpu))
            {
                importer.spritePixelsPerUnit = desiredPpu;
                changed = true;
            }

            if (importer.spriteBorder != desiredBorder)
            {
                importer.spriteBorder = desiredBorder;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(renderStamp) &&
                !string.Equals(importer.userData, renderStamp, StringComparison.Ordinal))
            {
                importer.userData = renderStamp;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
            }
        }

        private static Vector2Int ResolveRasterSize(string svgAbsolutePath, AIToUGUIAssetReference assetRef, AIToUGUISvgSpriteImportSettings settings)
        {
            var baseSize = ReadSvgIntrinsicSize(svgAbsolutePath, assetRef);
            var width = Mathf.Max(1, baseSize.x);
            var height = Mathf.Max(1, baseSize.y);
            var maxDimension = 4096;

            if (IsApproximatelySquare(width, height))
            {
                var targetSquareSize = settings != null
                    ? settings.GetClampedResolution()
                    : Mathf.Max(width, height);
                var clampedSquareSize = Mathf.Clamp(targetSquareSize, 32, maxDimension);
                return new Vector2Int(clampedSquareSize, clampedSquareSize);
            }

            if (width <= maxDimension && height <= maxDimension)
            {
                return new Vector2Int(width, height);
            }

            var scale = Mathf.Min((float)maxDimension / width, (float)maxDimension / height);
            width = Mathf.Clamp(Mathf.RoundToInt(width * scale), 1, maxDimension);
            height = Mathf.Clamp(Mathf.RoundToInt(height * scale), 1, maxDimension);
            return new Vector2Int(width, height);
        }

        private static Vector2Int ReadSvgIntrinsicSize(string svgAbsolutePath, AIToUGUIAssetReference assetRef)
        {
            var fallbackWidth = assetRef != null && assetRef.preferredWidth > 0f ? Mathf.CeilToInt(assetRef.preferredWidth) : 256;
            var fallbackHeight = assetRef != null && assetRef.preferredHeight > 0f ? Mathf.CeilToInt(assetRef.preferredHeight) : 256;

            try
            {
                var document = XDocument.Load(svgAbsolutePath);
                var root = document.Root;
                if (root == null)
                {
                    return new Vector2Int(fallbackWidth, fallbackHeight);
                }

                var width = ParseSvgScalar(root.Attribute("width")?.Value);
                var height = ParseSvgScalar(root.Attribute("height")?.Value);
                if ((width <= 0f || height <= 0f) &&
                    TryParseViewBox(root.Attribute("viewBox")?.Value, out var viewBoxWidth, out var viewBoxHeight))
                {
                    if (width <= 0f)
                    {
                        width = viewBoxWidth;
                    }

                    if (height <= 0f)
                    {
                        height = viewBoxHeight;
                    }
                }

                width = width > 0f ? width : fallbackWidth;
                height = height > 0f ? height : fallbackHeight;
                return new Vector2Int(Mathf.CeilToInt(width), Mathf.CeilToInt(height));
            }
            catch
            {
                return new Vector2Int(fallbackWidth, fallbackHeight);
            }
        }

        private static bool TryParseViewBox(string value, out float width, out float height)
        {
            width = 0f;
            height = 0f;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var matches = NumberRegex.Matches(value);
            if (matches.Count < 4)
            {
                return false;
            }

            if (!float.TryParse(matches[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out width) ||
                !float.TryParse(matches[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out height))
            {
                width = 0f;
                height = 0f;
                return false;
            }

            return width > 0f && height > 0f;
        }

        private static float ParseSvgScalar(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOf('%') >= 0)
            {
                return 0f;
            }

            var match = NumberRegex.Match(value);
            if (!match.Success)
            {
                return 0f;
            }

            return float.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0f;
        }

        private static string BuildRasterHtml(string svgAbsolutePath)
        {
            var svgUri = new Uri(svgAbsolutePath).AbsoluteUri;
            var sentinelCss = $"rgb({BrowserSentinelColor.r},{BrowserSentinelColor.g},{BrowserSentinelColor.b})";
            return
                "<!DOCTYPE html>\n" +
                "<html>\n" +
                "<head>\n" +
                "<meta charset=\"utf-8\" />\n" +
                "<style>\n" +
                "html, body { margin: 0; background: transparent; overflow: hidden; }\n" +
                ".svg-stage { position: relative; width: 100vw; height: 100vh; }\n" +
                "img { display: block; width: 100vw; height: 100vh; object-fit: fill; }\n" +
                $".edge {{ position: absolute; background: {sentinelCss}; pointer-events: none; }}\n" +
                ".edge-top { left: 0; top: 0; width: 100vw; height: 1px; }\n" +
                ".edge-left { left: 0; top: 0; width: 1px; height: 100vh; }\n" +
                ".edge-right { right: 0; top: 0; width: 1px; height: 100vh; }\n" +
                ".edge-bottom { left: 0; bottom: 0; width: 100vw; height: 1px; }\n" +
                "</style>\n" +
                "</head>\n" +
                "<body><div class=\"svg-stage\">" +
                $"<img src=\"{svgUri}\" />" +
                "<div class=\"edge edge-top\"></div>" +
                "<div class=\"edge edge-left\"></div>" +
                "<div class=\"edge edge-right\"></div>" +
                "<div class=\"edge edge-bottom\"></div>" +
                "</div></body>\n" +
                "</html>\n";
        }

        private static bool TryNormalizeBrowserRasterOutput(string outputAbsolutePath, Vector2Int rasterSize, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(outputAbsolutePath) || !File.Exists(outputAbsolutePath))
            {
                errorMessage = "Rasterized browser output does not exist.";
                return false;
            }

            try
            {
                var pngBytes = File.ReadAllBytes(outputAbsolutePath);
                var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                source.hideFlags = HideFlags.HideAndDontSave;
                if (!source.LoadImage(pngBytes, false))
                {
                    UnityEngine.Object.DestroyImmediate(source);
                    errorMessage = "Failed to decode browser raster output.";
                    return false;
                }

                try
                {
                    var viewportRect = DetectHeadlessViewportRect(source);
                    if (viewportRect.width <= 0 || viewportRect.height <= 0)
                    {
                        viewportRect = new RectInt(0, 0, source.width, source.height);
                    }

                    var contentRect = InsetRect(viewportRect, 1);
                    if (contentRect.width <= 0 || contentRect.height <= 0)
                    {
                        contentRect = viewportRect;
                    }

                    if (contentRect.width == source.width &&
                        contentRect.height == source.height &&
                        source.width == rasterSize.x &&
                        source.height == rasterSize.y)
                    {
                        return true;
                    }

                    var normalized = new Texture2D(rasterSize.x, rasterSize.y, TextureFormat.RGBA32, false, false);
                    normalized.hideFlags = HideFlags.HideAndDontSave;
                    try
                    {
                        ScaleTextureRegion(source, contentRect, normalized);
                        StripEdgeSentinelArtifacts(normalized, 4);
                        var normalizedBytes = normalized.EncodeToPNG();
                        if (normalizedBytes == null || normalizedBytes.Length == 0)
                        {
                            errorMessage = "Normalized browser raster output produced an empty PNG payload.";
                            return false;
                        }

                        File.WriteAllBytes(outputAbsolutePath, normalizedBytes);
                        return true;
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(normalized);
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(source);
                }
            }
            catch (Exception exception)
            {
                errorMessage = $"Failed to normalize browser raster output: {exception.Message}";
                return false;
            }
        }

        private static RectInt DetectHeadlessViewportRect(Texture2D texture)
        {
            if (texture == null)
            {
                return new RectInt(0, 0, 0, 0);
            }

            var width = texture.width;
            var height = texture.height;
            if (width <= 0 || height <= 0)
            {
                return new RectInt(0, 0, 0, 0);
            }

            var sentinelBounds = FindSentinelBounds(texture);
            if (sentinelBounds.width > 0 && sentinelBounds.height > 0)
            {
                return sentinelBounds;
            }

            return new RectInt(0, 0, width, height);
        }

        private static RectInt FindSentinelBounds(Texture2D texture)
        {
            if (texture == null)
            {
                return new RectInt(0, 0, 0, 0);
            }

            var width = texture.width;
            var height = texture.height;
            var maxX = -1;
            var maxY = -1;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (!MatchesOpaqueRgb(texture.GetPixel(x, y), BrowserSentinelColor))
                    {
                        continue;
                    }

                    if (x > maxX)
                    {
                        maxX = x;
                    }

                    if (y > maxY)
                    {
                        maxY = y;
                    }
                }
            }

            if (maxX < 0 || maxY < 0)
            {
                return new RectInt(0, 0, 0, 0);
            }

            return new RectInt(0, 0, maxX + 1, maxY + 1);
        }

        private static RectInt InsetRect(RectInt rect, int inset)
        {
            if (inset <= 0)
            {
                return rect;
            }

            var width = Mathf.Max(0, rect.width - inset * 2);
            var height = Mathf.Max(0, rect.height - inset * 2);
            return new RectInt(rect.x + inset, rect.y + inset, width, height);
        }

        private static bool MatchesOpaqueRgb(Color pixel, Color32 sentinel)
        {
            if (pixel.a <= 0.001f)
            {
                return false;
            }

            var color32 = (Color32)pixel;
            return color32.r == sentinel.r && color32.g == sentinel.g && color32.b == sentinel.b;
        }

        private static void ScaleTextureRegion(Texture2D source, RectInt sourceRect, Texture2D destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            var destinationPixels = new Color[destination.width * destination.height];
            var minSourceX = sourceRect.x;
            var minSourceY = sourceRect.y;
            var maxSourceX = sourceRect.xMax - 1;
            var maxSourceY = sourceRect.yMax - 1;

            for (var y = 0; y < destination.height; y++)
            {
                var sampleY = destination.height > 1
                    ? y / (float)(destination.height - 1)
                    : 0f;
                var sourceY = Mathf.Lerp(minSourceY, maxSourceY, sampleY);
                var normalizedSourceY = source.height > 1
                    ? Mathf.Clamp01((sourceY + 0.5f) / source.height)
                    : 0f;

                for (var x = 0; x < destination.width; x++)
                {
                    var sampleX = destination.width > 1
                        ? x / (float)(destination.width - 1)
                        : 0f;
                    var sourceX = Mathf.Lerp(minSourceX, maxSourceX, sampleX);
                    var normalizedSourceX = source.width > 1
                        ? Mathf.Clamp01((sourceX + 0.5f) / source.width)
                        : 0f;
                    destinationPixels[y * destination.width + x] = source.GetPixelBilinear(normalizedSourceX, normalizedSourceY);
                }
            }

            destination.SetPixels(destinationPixels);
            destination.Apply(false, false);
        }

        private static void StripEdgeSentinelArtifacts(Texture2D texture, int maxDepth)
        {
            if (texture == null || maxDepth <= 0)
            {
                return;
            }

            var width = texture.width;
            var height = texture.height;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var pixels = texture.GetPixels32();
            var changed = false;
            var clampedDepth = Mathf.Min(maxDepth, Mathf.Min(width, height));
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (!IsWithinEdgeDepth(x, y, width, height, clampedDepth))
                    {
                        continue;
                    }

                    var index = y * width + x;
                    var pixel = pixels[index];
                    if (!IsSentinelArtifact(pixel))
                    {
                        continue;
                    }

                    pixels[index] = new Color32(0, 0, 0, 0);
                    changed = true;
                }
            }

            if (!changed)
            {
                return;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
        }

        private static bool IsWithinEdgeDepth(int x, int y, int width, int height, int depth)
        {
            return x < depth || y < depth || x >= width - depth || y >= height - depth;
        }

        private static bool IsSentinelArtifact(Color32 pixel)
        {
            if (pixel.a < 8)
            {
                return false;
            }

            return Mathf.Abs(pixel.r - BrowserSentinelColor.r) <= 40 &&
                   Mathf.Abs(pixel.g - BrowserSentinelColor.g) <= 24 &&
                   Mathf.Abs(pixel.b - BrowserSentinelColor.b) <= 40;
        }

        private static bool TryRenderWithUnityVectorGraphics(
            string svgAssetPath,
            string outputAbsolutePath,
            Vector2Int rasterSize,
            out string errorMessage)
        {
            errorMessage = string.Empty;

            try
            {
                AssetDatabase.ImportAsset(svgAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var sourceSprite = AssetDatabase.LoadAssetAtPath<Sprite>(svgAssetPath);
                if (sourceSprite == null)
                {
                    errorMessage = "SVG source sprite was not imported by Unity Vector Graphics.";
                    return false;
                }

                var vectorUtilsType = Type.GetType(VectorUtilsTypeName, false);
                if (vectorUtilsType == null)
                {
                    errorMessage = "Unity Vector Graphics runtime type was not found.";
                    return false;
                }

                var renderMethod = vectorUtilsType.GetMethod(
                    "RenderSpriteToTexture2D",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Sprite), typeof(int), typeof(int), typeof(Material), typeof(int), typeof(bool) },
                    null);
                var renderArguments = renderMethod != null
                    ? new object[] { sourceSprite, rasterSize.x, rasterSize.y, null, 4, true }
                    : null;
                if (renderMethod == null)
                {
                    renderMethod = vectorUtilsType.GetMethod(
                        "RenderSpriteToTexture2D",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(Sprite), typeof(int), typeof(int), typeof(Material), typeof(int) },
                        null);
                    if (renderMethod == null)
                    {
                        errorMessage = "Unity Vector Graphics render method was not found.";
                        return false;
                    }

                    renderArguments = new object[] { sourceSprite, rasterSize.x, rasterSize.y, null, 4 };
                }

                if (!TryCreateUnityVectorGraphicsMaterial(sourceSprite, out var material, out var errorReason))
                {
                    errorMessage = errorReason;
                    return false;
                }

                try
                {
                    renderArguments[3] = material;
                    var rendered = renderMethod.Invoke(null, renderArguments) as Texture2D;
                    if (rendered == null)
                    {
                        errorMessage = "Unity Vector Graphics returned no texture.";
                        return false;
                    }

                    try
                    {
                        var pngBytes = rendered.EncodeToPNG();
                        if (pngBytes == null || pngBytes.Length == 0)
                        {
                            errorMessage = "Unity Vector Graphics produced an empty PNG payload.";
                            return false;
                        }

                        File.WriteAllBytes(outputAbsolutePath, pngBytes);
                        return true;
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(rendered);
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }
            }
            catch (TargetInvocationException exception)
            {
                errorMessage = exception.InnerException != null
                    ? exception.InnerException.Message
                    : exception.Message;
                return false;
            }
            catch (Exception exception)
            {
                errorMessage = exception.Message;
                return false;
            }
        }

        private static bool TryCreateUnityVectorGraphicsMaterial(Sprite sourceSprite, out Material material, out string errorMessage)
        {
            material = null;
            errorMessage = string.Empty;

            var svgImporterType = Type.GetType(SvgImporterTypeName, false);
            if (svgImporterType != null)
            {
                var spriteMaterialMethod = svgImporterType.GetMethod(
                    "CreateSVGSpriteMaterial",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Sprite) },
                    null);
                if (spriteMaterialMethod != null)
                {
                    material = spriteMaterialMethod.Invoke(null, new object[] { sourceSprite }) as Material;
                    if (material != null)
                    {
                        material.hideFlags = HideFlags.HideAndDontSave;
                        return true;
                    }
                }
            }

            var materialPath = sourceSprite != null && sourceSprite.texture != null
                ? VectorGradientMaterialAssetPath
                : VectorMaterialAssetPath;
            var baseMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath) ??
                               (AssetDatabase.LoadMainAssetAtPath(materialPath) as Material);
            if (baseMaterial == null)
            {
                errorMessage = $"Unity Vector Graphics material could not be loaded at '{materialPath}'.";
                return false;
            }

            material = new Material(baseMaterial)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            return true;
        }

        private static Vector4 ResolveImportedBorder(string assetPath, AIToUGUIAssetReference assetRef, string sourceAssetPath)
        {
            if (assetRef == null || assetRef.sliceBorder == Vector4.zero)
            {
                return Vector4.zero;
            }

            var normalizedSourceAssetPath = NormalizeAssetPath(sourceAssetPath);
            if (string.IsNullOrWhiteSpace(normalizedSourceAssetPath))
            {
                return assetRef.sliceBorder;
            }

            var intrinsicSize = ReadSvgIntrinsicSize(AssetPathToAbsolutePath(normalizedSourceAssetPath), assetRef);
            if (intrinsicSize.x <= 0 || intrinsicSize.y <= 0)
            {
                return assetRef.sliceBorder;
            }

            var outputAbsolutePath = AssetPathToAbsolutePath(assetPath);
            if (!TryReadPngSize(outputAbsolutePath, out var outputWidth, out var outputHeight) ||
                outputWidth <= 0 || outputHeight <= 0)
            {
                return assetRef.sliceBorder;
            }

            var scaleX = (float)outputWidth / intrinsicSize.x;
            var scaleY = (float)outputHeight / intrinsicSize.y;
            return new Vector4(
                assetRef.sliceBorder.x * scaleX,
                assetRef.sliceBorder.y * scaleY,
                assetRef.sliceBorder.z * scaleX,
                assetRef.sliceBorder.w * scaleY);
        }

        private static bool TryReadPngSize(string absolutePath, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            {
                return false;
            }

            try
            {
                using var stream = File.OpenRead(absolutePath);
                using var reader = new BinaryReader(stream);
                if (stream.Length < 24)
                {
                    return false;
                }

                var signature = reader.ReadBytes(8);
                if (signature.Length != 8 ||
                    signature[0] != 137 || signature[1] != 80 || signature[2] != 78 || signature[3] != 71)
                {
                    return false;
                }

                reader.ReadBytes(4); // chunk length
                var chunkType = reader.ReadBytes(4);
                if (chunkType.Length != 4 ||
                    chunkType[0] != (byte)'I' ||
                    chunkType[1] != (byte)'H' ||
                    chunkType[2] != (byte)'D' ||
                    chunkType[3] != (byte)'R')
                {
                    return false;
                }

                width = ReadPngInt32BigEndian(reader);
                height = ReadPngInt32BigEndian(reader);
                return width > 0 && height > 0;
            }
            catch
            {
                width = 0;
                height = 0;
                return false;
            }
        }

        private static int ReadPngInt32BigEndian(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(4);
            if (bytes.Length < 4)
            {
                return 0;
            }

            return (bytes[0] << 24) |
                   (bytes[1] << 16) |
                   (bytes[2] << 8) |
                   bytes[3];
        }

        private static bool IsApproximatelySquare(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            var ratio = (float)Mathf.Max(width, height) / Mathf.Max(1, Mathf.Min(width, height));
            return ratio <= 1.05f;
        }

        private static string ResolveBrowserExecutablePath()
        {
            for (var i = 0; i < BrowserExecutableCandidates.Length; i++)
            {
                if (File.Exists(BrowserExecutableCandidates[i]))
                {
                    return BrowserExecutableCandidates[i];
                }
            }

            return string.Empty;
        }

        private static bool IsRasterExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                return false;
            }

            for (var i = 0; i < RasterExtensions.Length; i++)
            {
                if (string.Equals(extension, RasterExtensions[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ExtractRawAssetPath(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            var match = CssUrlRegex.Match(source);
            var value = match.Success ? match.Groups["path"].Value : source;
            value = value.Trim().Trim('\'', '"');

            var queryIndex = value.IndexOfAny(new[] { '?', '#' });
            return queryIndex >= 0 ? value.Substring(0, queryIndex).Trim() : value;
        }

        private static string ResolveSiteRootFromSourceRoot(string sourceRootAssetPath)
        {
            var normalized = NormalizeAssetPath(sourceRootAssetPath);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            if (normalized.EndsWith("/source", StringComparison.OrdinalIgnoreCase))
            {
                var candidate = NormalizeAssetPath(Path.GetDirectoryName(normalized)?.Replace("\\", "/"));
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate;
                }
            }

            return normalized;
        }

        private static string CombineAssetPath(string assetFolderPath, string relativePath)
        {
            var folderAbsolutePath = AssetPathToAbsolutePath(assetFolderPath);
            if (string.IsNullOrWhiteSpace(folderAbsolutePath))
            {
                return string.Empty;
            }

            var combinedAbsolutePath = Path.GetFullPath(Path.Combine(folderAbsolutePath, relativePath.Replace("/", Path.DirectorySeparatorChar.ToString())));
            return NormalizeAssetPath(AbsolutePathToAssetPath(combinedAbsolutePath));
        }

        private static string AssetPathToAbsolutePath(string assetPath)
        {
            var normalized = NormalizeAssetPath(assetPath);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            var projectRoot = GetProjectRootAbsolutePath();
            return string.IsNullOrWhiteSpace(projectRoot)
                ? string.Empty
                : Path.GetFullPath(Path.Combine(projectRoot, normalized.Replace("/", Path.DirectorySeparatorChar.ToString())));
        }

        private static string AbsolutePathToAssetPath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return string.Empty;
            }

            var projectRoot = GetProjectRootAbsolutePath();
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return string.Empty;
            }

            var normalizedProjectRoot = Path.GetFullPath(projectRoot).Replace("\\", "/").TrimEnd('/');
            var normalizedAbsolutePath = Path.GetFullPath(absolutePath).Replace("\\", "/");
            if (!normalizedAbsolutePath.StartsWith(normalizedProjectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return normalizedAbsolutePath.Substring(normalizedProjectRoot.Length + 1);
        }

        private static string GetProjectRootAbsolutePath()
        {
            return Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace("\\", "/").TrimEnd('/');
        }

        private static void EnsureAssetFolder(string folderAssetPath)
        {
            if (string.IsNullOrWhiteSpace(folderAssetPath) || AssetDatabase.IsValidFolder(folderAssetPath))
            {
                return;
            }

            var normalized = NormalizeAssetPath(folderAssetPath);
            var parent = Path.GetDirectoryName(normalized)?.Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureAssetFolder(parent);
            }

            var folderName = Path.GetFileName(normalized);
            if (!string.IsNullOrWhiteSpace(parent) && !string.IsNullOrWhiteSpace(folderName) && AssetDatabase.IsValidFolder(parent))
            {
                AssetDatabase.CreateFolder(parent, folderName);
            }
        }

        private static bool AssignIfDifferent<T>(ref T current, T next)
        {
            if (EqualityComparer<T>.Default.Equals(current, next))
            {
                return false;
            }

            current = next;
            return true;
        }

        private static bool AssignIfDifferent(ref bool current, bool next)
        {
            if (current == next)
            {
                return false;
            }

            current = next;
            return true;
        }

        private static bool AssignIfDifferent(ref float current, float next)
        {
            if (Mathf.Approximately(current, next))
            {
                return false;
            }

            current = next;
            return true;
        }

        private static bool AssignIfDifferent(ref Vector4 current, Vector4 next)
        {
            if (current == next)
            {
                return false;
            }

            current = next;
            return true;
        }
    }
}

#endif
