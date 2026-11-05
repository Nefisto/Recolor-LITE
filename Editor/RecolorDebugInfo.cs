using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Nefisto.Recolor
{
    // Which edition of Recolor is in the project, and a report of the window's settings for a bug report:
    // the product, Unity and system, the image and its import settings, and the state of every add-on.
    internal static class RecolorDebugInfo
    {
        private const string ProAssembly = "Nefisto.Recolor.Pro.Editor";

        private const string BaseAssembly = "Nefisto.Recolor.Base.Editor";

        // True when this code is the copy of Recolor that Recolor Pro includes, used when Recolor (free)
        // isn't in the project.
        internal static bool BuiltIntoPro => typeof(RecolorWindow).Assembly.GetName().Name == BaseAssembly;

        // The version of Recolor in use: its package's, or the one copied into Pro.
        internal static string LiteVersion => BuiltIntoPro ? IncludedVersion() : VersionOf(typeof(RecolorWindow).Assembly);

        // Pro keeps the version of the Recolor it includes in IncludedBase.Version.
        private static string IncludedVersion () =>
            ProAssemblyLoaded?.GetType("Nefisto.Recolor.IncludedBase")?.GetField("Version", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetRawConstantValue() as string;

        internal static Assembly ProAssemblyLoaded =>
            AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == ProAssembly);

        internal static bool ProInstalled => ProAssemblyLoaded != null;

        internal static string ProVersion
        {
            get
            {
                var pro = ProAssemblyLoaded;

                return pro != null ? VersionOf(pro) : null;
            }
        }

        // The version in the package.json of the package that holds the assembly, or null when it isn't in one.
        private static string VersionOf (Assembly assembly) => PackageInfo.FindForAssembly(assembly)?.version;

        [Serializable]
        internal class Product
        {
            public string edition;
            public string recolorVersion;
            public string recolorSource;
            public string proVersion;
            public string unityVersion;
            public string os;
            public string graphics;
            public string colorSpace;
            public bool darkSkin;
        }

        [Serializable]
        internal class Image
        {
            public string path;
            public string importer;
            public int width;
            public int height;
            public string spriteMode;
            public int frames;
            public bool readable;
            public string compression;
            public int maxSize;
            public string filter;
            public bool sRGB;
            public bool alphaIsTransparency;
            public bool cropped;
            public Vector2Int cropOffset;
            public string readWarning;
        }

        [Serializable]
        internal class Settings
        {
            public Color fromColor;
            public Color toColor;
            public bool matchByHue;
            public float tolerance;
            public float feather;
            public float rgbTolerance;
            public float rgbFeather;
            public float minSaturation;
            public string area;
            public string tool;
            public bool contiguous;
            public bool diagonals;
            public float edgeStrength;
            public bool keepShading;
            public bool cleanEdges;
            public float mixTolerance;
            public int changedPixels;
            public List<RegionSeed> seeds;
            public List<Vector2Int> addedPixels;
            public List<Vector2Int> removedPixels;
            public string nameMode;
            public string nameStart;
            public string nameEnd;
            public string customName;
        }

        // The whole report as JSON. The window can be null, when Recolor isn't open: then only the product goes.
        internal static string Build (RecolorWindow window)
        {
            var product = new Product
            {
                edition = ProInstalled ? "Pro" : "Free",
                recolorVersion = LiteVersion ?? "Unknown",
                recolorSource = BuiltIntoPro ? "Built into Pro" : "Recolor package",
                proVersion = ProVersion,
                unityVersion = Application.unityVersion,
                os = SystemInfo.operatingSystem,
                graphics = SystemInfo.graphicsDeviceType.ToString(),
                colorSpace = PlayerSettings.colorSpace.ToString(),
                darkSkin = EditorGUIUtility.isProSkin
            };

            var parts = new List<(string name, string json)> { ("product", JsonUtility.ToJson(product, true)) };

            if (window == null)
                parts.Add(("window", "\"Recolor isn't open\""));
            else
            {
                parts.Add(("image", window.source != null ? JsonUtility.ToJson(ImageOf(window), true) : "null"));
                parts.Add(("settings", JsonUtility.ToJson(SettingsOf(window), true)));

                foreach (var addon in window.Addons)
                    if (addon != null)
                        parts.Add((addon.GetType().Name, addon.DebugState() ?? "null"));
            }

            var text = new StringBuilder("{\n");

            for (var i = 0; i < parts.Count; i++)
            {
                text.Append("    \"").Append(parts[i].name).Append("\": ");
                text.Append(parts[i].json.Replace("\n", "\n    "));
                text.Append(i < parts.Count - 1 ? ",\n" : "\n");
            }

            return text.Append('}').ToString();
        }

        private static Image ImageOf (RecolorWindow window)
        {
            var path = AssetDatabase.GetAssetPath(window.source);
            var importer = AssetImporter.GetAtPath(path);
            var textureImporter = importer as TextureImporter;

            return new Image
            {
                path = path,
                importer = importer != null ? importer.GetType().Name : null,
                width = window.source.width,
                height = window.source.height,
                spriteMode = textureImporter != null ? textureImporter.spriteImportMode.ToString() : null,
                frames = window.frames.Count,
                readable = textureImporter != null && textureImporter.isReadable,
                compression = textureImporter != null ? textureImporter.textureCompression.ToString() : null,
                maxSize = textureImporter != null ? textureImporter.maxTextureSize : 0,
                filter = textureImporter != null ? textureImporter.filterMode.ToString() : null,
                sRGB = textureImporter != null && textureImporter.sRGBTexture,
                alphaIsTransparency = textureImporter != null && textureImporter.alphaIsTransparency,
                cropped = window.cropped,
                cropOffset = window.cropOffset,
                readWarning = window.readWarning
            };
        }

        private static Settings SettingsOf (RecolorWindow window) => new()
        {
            fromColor = window.fromColor,
            toColor = window.toColor,
            matchByHue = window.matchByHue,
            tolerance = window.tolerance,
            feather = window.feather,
            rgbTolerance = window.rgbTolerance,
            rgbFeather = window.rgbFeather,
            minSaturation = window.minSaturation,
            area = window.area.ToString(),
            tool = window.tool.ToString(),
            contiguous = window.contiguous,
            diagonals = window.diagonals,
            edgeStrength = window.edgeStrength,
            keepShading = window.keepShading,
            cleanEdges = window.cleanEdges,
            mixTolerance = window.mixTolerance,
            changedPixels = window.changedPixels,
            seeds = window.seeds,
            addedPixels = window.addedPixels,
            removedPixels = window.removedPixels,
            nameMode = window.nameMode.ToString(),
            nameStart = window.nameStart,
            nameEnd = window.nameEnd,
            customName = window.customName
        };

        private static RecolorWindow OpenWindow => Resources.FindObjectsOfTypeAll<RecolorWindow>().FirstOrDefault();

        // What to do with the bug report folder, for the person sending it.
        private static string ReadMe => "RECOLOR BUG REPORT\n" +
                                        "==================\n\n" +
                                        $"To report the bug, send an email to {RecolorHelpWindow.ContactEmail} with:\n\n" +
                                        "1. debug-info.json, from this folder. It has your Recolor settings, the versions and your Unity and system info.\n" +
                                        "2. The image you used and its .meta file (it keeps the import settings), both copied to this folder. Add editing.png and result.png too, if they are here.\n" +
                                        "3. A short description: what you did, what happened, and what you expected to happen.\n\n" +
                                        "Anything else that helps is welcome, such as a screenshot of the Recolor window.\n\n" +
                                        "Thanks for helping make Recolor better!\n";

        // Asks for a folder and creates a bug report in a new folder inside it, then shows it, ready to attach
        // to an email: a READ-ME, the report, the image file as it is, the image as Recolor read it (or the
        // save point being edited) and the result.
        internal static void CreateReport () => CreateReport(OpenWindow);

        internal static void CreateReport (RecolorWindow window)
        {
            // Without an image the report can't show the bug, so the person can go open it first.
            if ((window == null || window.source == null) &&
                !EditorUtility.DisplayDialog("Recolor", "There's no image open in the Recolor window, so the report won't have it.\n\n" +
                                                        "For a bug with an image, open the image in Recolor, set it up as when the bug happened, then create the report.",
                    "Create anyway", "Cancel"))
                return;

            var parent = EditorUtility.SaveFolderPanel("Create bug report in", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "");

            if (string.IsNullOrEmpty(parent))
                return;

            var folder = Path.Combine(parent, $"Recolor bug report {DateTime.Now:yyyy-MM-dd HH-mm-ss}");

            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "READ-ME.txt"), ReadMe);
            File.WriteAllText(Path.Combine(folder, "debug-info.json"), Build(window));

            if (window != null && window.source != null)
            {
                var path = AssetDatabase.GetAssetPath(window.source);

                // With its .meta, so the import settings and the slicing come along.
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Copy(path, Path.Combine(folder, Path.GetFileName(path)));

                    if (File.Exists(path + ".meta"))
                        File.Copy(path + ".meta", Path.Combine(folder, Path.GetFileName(path) + ".meta"));
                }

                WritePng(window.original, Path.Combine(folder, "editing.png"));
                WritePng(window.result, Path.Combine(folder, "result.png"));
            }

            EditorUtility.RevealInFinder(Path.Combine(folder, "READ-ME.txt"));
        }

        private static void WritePng (Texture2D texture, string path)
        {
            if (texture == null || !texture.isReadable)
                return;

            try
            {
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Recolor: couldn't save {Path.GetFileName(path)} for the bug report. {exception.Message}");
            }
        }
    }
}
