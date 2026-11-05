using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Nefisto.Recolor
{
    // Moves every pixel close to one colour over to another, keeping how far each one was from the first:
    // a darker or paler shade of the source lands as the same darker or paler shade of the target. The
    // shift happens in HSV, so the hue turns and the saturation and value keep their offsets.
    public class RecolorWindow : EditorWindow, IHasCustomMenu
    {
        internal const float PreviewPadding = 8f;
        internal const float PreviewTitleHeight = 16f;
        internal const float WheelSize = 64f;
        internal const float SettingsShare = 0.5f;
        internal const float MinSettingsWidth = 320f;
        internal const float MinPreviewWidth = 160f;
        internal const float SplitterWidth = 6f;
        internal const string SettingsWidthKey = "Nefisto.Recolor.SettingsWidth";
        internal const string Suffix = "_recolored";
        internal const int EdgeRadius = 2;
        internal const float ExactFit = 2f / 255f;
        internal const int CommonColorCount = 16;
        internal const byte ShadeAlpha = 150;
        internal const double SlowRefreshSeconds = 0.05;
        internal const float AreaControlsWidth = 360f;
        internal const double DragRefreshSeconds = 0.1;

        // Below this saturation, or this brightness, the From colour counts as a grey with no hue.
        internal const float GreySaturation = 0.1f;
        internal const float GreyValue = 0.05f;
        internal const int LoupePixels = 11;
        internal const float LoupeCell = 10f;
        internal const float LoupeStripHeight = 18f;

        // What a click on the original image does in a clicked region: select a whole part, or paint pixels.
        internal enum SelectTool
        {
            Part,
            Pixel
        }

        internal enum AreaMode
        {
            WholeImage,
            ClickedRegion
        }

        // How Save as new image names the file. Suffix: the image's name, then the user's text, which starts
        // as _recolored. Prefix: the user's text, then _recolored. Custom name: the user's text only.
        internal enum NameMode
        {
            Suffix,
            Prefix,

            [InspectorName("Custom name")]
            CustomName
        }

        [SerializeField]
        internal Texture2D source;

        [SerializeField]
        internal Color fromColor = Color.red;

        [SerializeField]
        internal Color toColor = Color.blue;

        // A From colour with a hue is matched by hue, in degrees. A grey, white or black one has no hue, so
        // it is matched by how close the colours are in RGB, 0 to 100. Each way keeps its own numbers.
        [SerializeField]
        internal float tolerance = 30f;

        [SerializeField]
        internal float feather = 10f;

        [SerializeField]
        internal float rgbTolerance = 20f;

        [SerializeField]
        internal float rgbFeather = 10f;

        [SerializeField]
        internal float minSaturation = 0.1f;

        [SerializeField]
        internal AreaMode area = AreaMode.WholeImage;

        [SerializeField]
        internal float edgeStrength = 25f;

        // The clicked region also grows to the pixels touching at a corner.
        [SerializeField]
        internal bool diagonals;

        // Off, a click takes every pixel of its frame close to the clicked one, joined to it or not.
        [SerializeField]
        internal bool contiguous;

        // The pixels the regions grow from, set by clicks on the original image.
        [SerializeField]
        internal List<RegionSeed> seeds = new();

        // Pixels painted into or out of the selection by hand, on top of the clicked parts.
        [SerializeField]
        internal List<Vector2Int> addedPixels = new();

        [SerializeField]
        internal List<Vector2Int> removedPixels = new();

        [SerializeField]
        internal SelectTool tool = SelectTool.Part;



        // Every shade moves only as far as fits, so none merge; off, the From colour lands on To exactly.
        [SerializeField]
        internal bool keepShading = true;

        [SerializeField]
        internal bool cleanEdges = true;

        [SerializeField]
        internal float mixTolerance = 16f;

        // The name of a new image. Each mode keeps its own text, so switching back and forth loses nothing.
        [SerializeField]
        internal NameMode nameMode = NameMode.Suffix;

        // Typed in Prefix mode, before _recolored.
        [SerializeField]
        internal string nameStart = "";

        // Typed in Suffix mode, after the image's name.
        [SerializeField]
        internal string nameEnd = Suffix;

        [SerializeField]
        internal string customName = "";

        // The Pro features, and any other add-on found in the project, one of each, kept with the window.
        [SerializeReference]
        private List<RecolorAddon> addons = new();

        internal Texture2D original;

        // The image as it was loaded, to go back to it from a save point.
        internal byte[] basePng;

        // Set when the colours read may be off, such as from a compressed or shrunk texture.
        internal string readWarning;

        // The import settings can be changed to read the image at full size and without compression.
        internal bool canFixImport;

        // An image packed by its own importer, such as Aseprite, is cut down to its sprites. This is where
        // the cut starts in the imported texture; sprite rects move by it.
        internal bool cropped;
        internal Vector2Int cropOffset;
        internal Texture2D loadedSource;
        internal Texture2D result;
        internal Texture2D regionShade;
        internal readonly List<RectInt> frames = new();

        // The pivot of each frame, in pixels from its bottom left corner.
        internal readonly List<Vector2> framePivots = new();
        internal Color32[] lastPixels;
        internal bool[] lastRegion;
        internal bool matchByHue = true;
        internal double lastRefreshTime;
        internal double lastRefreshSeconds;
        internal int strokeGroup = -1;
        internal bool strokeStartsNew;
        internal float[] lastWeights;
        internal int changedPixels;
        internal Color landedColor;
        internal bool dirty = true;
        internal static Texture2D wheelTexture;
        internal static GUIStyle loupeLabel;
        internal static GUIStyle shortcutLabel;
        internal static GUIStyle sectionStyle;
        private static GUIStyle changedStyle;
        internal Vector2 optionsScroll;
        internal float settingsWidth;
        internal float splitterGrab;

        // The grey behind notes in the previews, such as the keys and the count of changed pixels.
        // Lighter than the header band, so a note stands out from it, and nearly opaque over the image.
        private static Color NoteColour => EditorGUIUtility.isProSkin ? new Color(0.32f, 0.32f, 0.32f, 0.94f) : new Color(0.93f, 0.93f, 0.93f, 0.94f);

        // The keys show in the original image when it is wide enough, otherwise under the settings.
        private bool keysInPreview;
        internal readonly List<int> nearFull = new();
        internal readonly List<int> nearOther = new();
        internal readonly List<int> pureAll = new();

        // The pixels of the image being edited, read once, and the colours they use. Most of the work is
        // done once per colour, and a sprite has far fewer colours than pixels. Colours are kept opaque:
        // the matching and the mixing only look at red, green and blue.
        internal Color32[] pixelCache;
        internal int[] colourOf;
        internal Color32[] colours;
        internal Dictionary<int, int> colourIndex;
        internal float[] colourS;
        internal float[] colourV;

        // What a colour of a pure set or a neighbourhood is, by colour, for the clean edges.
        internal bool[] isPureFull;
        internal bool[] isPureOther;
        internal int[] nearStamp;
        internal int stamp;

        // Clean edges results that depend only on the colour of the pixel, worked out once per colour.
        internal bool[] threeDone;
        internal bool[] threeFound;
        internal float[] threeMiss;
        internal Color[] threeMixed;
        internal bool[] pairDone;
        internal bool[] pairFound;
        internal Color[] pairMixed;
        internal Color[] movedColour;
        internal bool[] movedDone;

        [MenuItem("Tools/Recolor/Open", priority = 0)]
        public static void Open () => GetWindow<RecolorWindow>("Recolor");

        private void OnEnable ()
        {
            minSize = new Vector2(MinSettingsWidth + MinPreviewWidth + SplitterWidth, 360f);
            wantsMouseMove = true;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.modifierKeysChanged += Repaint;
            EditorApplication.update += UpdateAddons;
            SetUpAddons();
        }

        private void OnDisable ()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.modifierKeysChanged -= Repaint;
            EditorApplication.update -= UpdateAddons;
            EditorApplication.update -= ReloadAfterImport;

            foreach (var addon in addons)
                addon.OnDisable();

            ReleasePreviews();
        }

        internal void OnUndoRedo ()
        {
            if (source != loadedSource)
                LoadOriginal();
            else
                foreach (var addon in addons)
                    addon.OnUndoRedo();

            dirty = true;
            Repaint();
        }

        private void OnGUI ()
        {
            // Undo only keeps an entry when a field below really changed.
            Undo.RecordObject(this, "Change recolor settings");

            // Esc clears the selection, unless it is ending the edit of a text field.
            var current = Event.current;

            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape && !EditorGUIUtility.editingTextField &&
                area == AreaMode.ClickedRegion && HasSelection())
            {
                ClearSelection();
                current.Use();
                Repaint();
            }

            if (settingsWidth <= 0f)
                settingsWidth = EditorPrefs.GetFloat(SettingsWidthKey, position.width * SettingsShare);

            // Each column keeps its minimum, also when the window shrinks.
            settingsWidth = Mathf.Clamp(settingsWidth, MinSettingsWidth, Mathf.Max(MinSettingsWidth, position.width - MinPreviewWidth - SplitterWidth));

            var ready = false;

            using (new EditorGUILayout.HorizontalScope())
            {
                // Settings on the left, in a box that scrolls on its own.
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(settingsWidth), GUILayout.ExpandHeight(true)))
                {
                    optionsScroll = EditorGUILayout.BeginScrollView(optionsScroll);
                    ready = DrawOptions();
                    EditorGUILayout.EndScrollView();

                    // Always in full at the bottom, outside the scroll: what the add-ons keep there, then
                    // the shortcuts when the original image has no room for them.
                    if (ready)
                        foreach (var addon in addons)
                            addon.DrawFooter();

                    DrawShortcuts();
                }

                DrawSplitter(GUILayoutUtility.GetRect(SplitterWidth, SplitterWidth, GUILayout.Width(SplitterWidth), GUILayout.ExpandHeight(true)));

                // Before on top, after below, on the right.
                var area = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

                if (ready)
                    DrawPreviews(area);
            }
        }

        // Dragging the line between the columns sets how wide the settings are.
        internal void DrawSplitter (Rect splitter)
        {
            var id = GUIUtility.GetControlID(FocusType.Passive);
            var current = Event.current;

            EditorGUIUtility.AddCursorRect(splitter, MouseCursor.ResizeHorizontal);

            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown when current.button == 0 && splitter.Contains(current.mousePosition):
                    GUIUtility.hotControl = id;
                    splitterGrab = current.mousePosition.x - settingsWidth;
                    current.Use();

                    break;

                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    settingsWidth = current.mousePosition.x - splitterGrab;
                    current.Use();
                    Repaint();

                    break;

                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    EditorPrefs.SetFloat(SettingsWidthKey, settingsWidth);
                    current.Use();

                    break;

                case EventType.Repaint:
                    var line = EditorGUIUtility.isProSkin ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.6f, 0.6f, 0.6f);

                    EditorGUI.DrawRect(new Rect(splitter.center.x - 0.5f, splitter.y, 1f, splitter.height), line);

                    break;
            }
        }

        internal bool DrawOptions ()
        {
            EditorGUI.BeginChangeCheck();

            using (new EditorGUILayout.HorizontalScope())
            {
                // One line high, so a texture shows as a name and not as a big thumbnail.
                source = (Texture2D) EditorGUILayout.ObjectField("Image", source, typeof(Texture2D), false, GUILayout.Height(EditorGUIUtility.singleLineHeight));

                if (GUILayout.Button(new GUIContent("Use selection", "Uses the image selected in the Project window: a texture, a sprite, or a file such as an Aseprite or PSD file."), GUILayout.Width(100f)))
                    source = TextureOf(Selection.activeObject) ?? source;
            }

            if (!string.IsNullOrEmpty(readWarning) && source != null)
            {
                EditorGUILayout.HelpBox(readWarning, MessageType.Warning);

                if (canFixImport && GUILayout.Button(new GUIContent("Import at full size",
                        "Changes the image's import settings: Max Size large enough for the whole image, and no compression.")) &&
                    EditorUtility.DisplayDialog("Recolor", $"Change the import settings of '{source.name}' to full size and no compression?", "Change", "Cancel"))
                    ImportAtFullSize();
            }

            if (EditorGUI.EndChangeCheck())
            {
                seeds.Clear();
                addedPixels.Clear();
                removedPixels.Clear();

                foreach (var addon in addons)
                    addon.OnNewImage();

                LoadOriginal();
            }

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();

            DrawFromRow();
            DrawToRow();

            foreach (var addon in addons)
                addon.AfterColours();

            EditorGUILayout.Space();

            if (UsesHue())
            {
                tolerance = EditorGUILayout.Slider(new GUIContent("Tolerance (degrees)",
                    "How far around the hue wheel still counts. Every shade of the colour, light or dark, is taken."), tolerance, 0f, 180f);
                feather = EditorGUILayout.Slider(new GUIContent("Feather (degrees)", "Past the tolerance, how far the change fades out."), feather, 0f, 90f);
                minSaturation = EditorGUILayout.Slider(new GUIContent("Min saturation", "Greys have no hue, so the ones below this stay as they are."), minSaturation, 0f, 1f);
            }
            else
            {
                rgbTolerance = EditorGUILayout.Slider(new GUIContent("Tolerance",
                    "The From colour is a grey, white or black, which has no hue, so the colours close to it in RGB are taken. 0 to 100, where 100 is black to white."), rgbTolerance, 0f, 100f);
                rgbFeather = EditorGUILayout.Slider(new GUIContent("Feather", "Past the tolerance, how far the change fades out."), rgbFeather, 0f, 50f);
                EditorGUILayout.LabelField("The From colour has no hue, so colours are matched by how close they are in RGB.", EditorStyles.wordWrappedMiniLabel);
            }

            foreach (var addon in addons)
                addon.DrawAfterMatching();

            cleanEdges = EditorGUILayout.Toggle(new GUIContent("Clean edges",
                "Keeps anti-aliased edges and fades smooth.\n\n" +
                "With anti-aliasing, the pixels between a red cape and its black outline are dark reds: part red, part black. Changed as they are, they would leave a reddish fringe around a blue cape.\n\n" +
                "Clean edges finds how much of each colour such a pixel holds, say 60% red and 40% black, and changes only the red part: it becomes 60% blue and 40% black, as smooth as the original edge.\n\n" +
                "Pixel art with hard edges has few such pixels, so it changes little there."), cleanEdges);

            if (cleanEdges)
                mixTolerance = EditorGUILayout.Slider(new GUIContent("Mix tolerance",
                    "How far off the line between two colours a pixel can be and still count as their mix. Raise it when loose pixels of the old colour are left in a fade or along an edge; lower it when colours that are not a mix get caught."), mixTolerance, 1f, 48f);

            if (EditorGUI.EndChangeCheck())
                dirty = true;

            if (source != null && original == null)
                LoadOriginal();

            if (original == null)
            {
                EditorGUILayout.HelpBox("Pick an image: a texture or sprite, from a PNG, an Aseprite or PSD file, or any image Unity imports.", MessageType.Info);

                return false;
            }

            if (dirty && RefreshNow())
                Refresh();

            if (area == AreaMode.ClickedRegion)
            {
                DrawRegionControls();

                foreach (var addon in addons)
                    addon.DrawAfterRegion();
            }


            // When the lightest or darkest shade would pass the limit, Keep shading moves every shade only
            // as far as fits, and this shows where the From colour went. The option shows only then, or
            // while it is off, so it can be turned back on.
            var heldBack = keepShading && !Approximately(landedColor, Target());

            if (changedPixels > 0 && (heldBack || !keepShading))
            {
                EditorGUI.BeginChangeCheck();
                keepShading = EditorGUILayout.Toggle(new GUIContent("Keep shading",
                    "On, every shade moves by the same amount and none passes the limit, so no two shades merge; the From colour then lands near the To colour. Off, the From colour lands on the To colour exactly, and the shades past the limit are cut there, so some of them merge."), keepShading);

                if (EditorGUI.EndChangeCheck())
                    dirty = true;

                if (heldBack)
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.ColorField(new GUIContent("From lands on",
                            "The To colour would push the lightest or darkest shades past the limit and merge them, so every shade moved only as far as fits. A slightly lighter or darker To colour moves the From colour closer to it, or Keep shading can be turned off."), landedColor);
            }

            DrawFileName();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save as new image"))
                    Save(NewPath());

                if (GUILayout.Button("Overwrite original") &&
                    EditorUtility.DisplayDialog("Recolor", $"Overwrite '{source.name}'? Undo won't bring it back.", "Overwrite", "Cancel"))
                    Save(AssetDatabase.GetAssetPath(source));
            }

            foreach (var addon in addons)
                addon.DrawSections();

            return true;
        }

        // The field ends where the To field does, so the two line up beside the wheel.
        internal void DrawRegionControls ()
        {
            // What to click, Clear and the keys are all in the header of the original image.
            if (!HasSelection())
                return;

            foreach (var addon in addons)
                addon.DrawRegionControls();
        }

        internal static string RemoveKey () => Application.platform == RuntimePlatform.OSXEditor ? "Cmd" : "Ctrl";

        internal void ClearSelection ()
        {
            seeds.Clear();
            addedPixels.Clear();
            removedPixels.Clear();
            dirty = true;
            SelectionChanged(true);
        }

        internal bool HasSelection () => seeds.Count > 0 || addedPixels.Count > 0 || removedPixels.Count > 0;

        // Starts the selection from every pixel the From colour matches.
        internal void SelectEveryMatch ()
        {
            seeds.Clear();
            addedPixels.Clear();
            removedPixels.Clear();
            seeds.Add(RegionSeed.EveryMatch());
            dirty = true;
            SelectionChanged(true);
        }

        internal string SelectionSummary ()
        {
            var count = seeds.Count(seed => !seed.remove && !seed.everyMatch);
            var parts = seeds.Any(seed => seed.everyMatch)
                ? count == 0 ? "Every match" : count == 1 ? "Every match and 1 part" : $"Every match and {count} parts"
                : count == 1 ? "1 part" : $"{count} parts";

            if (addedPixels.Count == 0 && removedPixels.Count == 0)
                return parts + " selected";

            return $"{parts}, {Pixels(addedPixels.Count)} added, {removedPixels.Count} removed";
        }

        internal static string Pixels (int count) => count == 1 ? "1 pixel" : $"{count} pixels";

        internal void DrawFromRow ()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                fromColor = EditorGUILayout.ColorField(new GUIContent("From", "The colour to replace. A click on the original image picks it; a zoom around the cursor shows the pixels, and holding Alt shows only the colour."), fromColor);
                GUILayout.Space(WheelSize + 4f);
            }
        }

        // The colour field sits centred beside the wheel.
        internal void DrawToRow ()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Height(WheelSize)))
                {
                    GUILayout.FlexibleSpace();
                    toColor = EditorGUILayout.ColorField(new GUIContent("To", "Where that colour goes. Its shades follow."), toColor);
                    GUILayout.FlexibleSpace();
                }

                DrawWheel(GUILayoutUtility.GetRect(WheelSize, WheelSize, GUILayout.Width(WheelSize), GUILayout.Height(WheelSize)));
            }
        }

        internal void DrawPreviews (Rect area)
        {
            var half = (area.height - PreviewPadding * 3f) * 0.5f;

            if (half < PreviewPadding * 2f || area.width < PreviewPadding * 4f)
                return;

            var before = new Rect(area.x + PreviewPadding, area.y + PreviewPadding, area.width - PreviewPadding * 2f, half);
            var after = new Rect(before.x, before.yMax + PreviewPadding, before.width, half);

            // The original image takes its whole frame under the title, whatever the header holds, and the
            // header is drawn over it: the image keeps its size when the area changes.
            var image = Inset(before);

            GUI.Box(before, GUIContent.none, EditorStyles.helpBox);

            if (image.height > 0f)
            {
                EditorGUI.DrawTextureTransparent(image, original, ScaleMode.ScaleToFit);

                if (this.area == AreaMode.ClickedRegion && regionShade != null && Event.current.type == EventType.Repaint)
                    GUI.DrawTexture(image, regionShade, ScaleMode.ScaleToFit, true);
            }

            var header = DrawOriginalHeader(before);

            DrawResultPreview(after);

            // Clicks on the header are its own, not picks on the image under it.
            if (image.height > 0f)
                HandlePicking(image, header);
        }

        // The title, the area controls and the keys of the original image, over its top. Returns where they
        // are, so clicks there don't reach the image.
        private Rect[] DrawOriginalHeader (Rect frame)
        {
            var title = addons.Select(addon => addon.OriginalTitle()).FirstOrDefault(each => each != null) ?? "Original image";
            var rowHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            var controlsWidth = Mathf.Min(frame.width - 12f, AreaControlsWidth);
            var addonRows = addons.Where(addon => addon.OriginalHeaderHeight > 0f).ToList();
            var addonHeight = addonRows.Sum(addon => addon.OriginalHeaderHeight + EditorGUIUtility.standardVerticalSpacing);
            var controls = new Rect(frame.x + 6f, frame.y + PreviewTitleHeight + 2f, controlsWidth, AreaRows() * rowHeight + addonHeight);
            var band = HeaderBand(frame, Mathf.Max(TitleWidth(title), controlsWidth + 12f), controls.yMax + 2f - frame.y);

            DrawBand(band);
            GUI.Label(new Rect(frame.x + 6f, frame.y + 2f, frame.width - 12f, PreviewTitleHeight), title, EditorStyles.miniBoldLabel);

            var labelWidth = EditorGUIUtility.labelWidth;

            EditorGUIUtility.labelWidth = 92f;
            DrawAreaControls(controls);
            EditorGUIUtility.labelWidth = labelWidth;

            // The add-on rows, under the area controls.
            var y = controls.y + AreaRows() * rowHeight;

            foreach (var addon in addonRows)
            {
                addon.DrawOriginalHeader(new Rect(controls.x, y, controls.width, addon.OriginalHeaderHeight));
                y += addon.OriginalHeaderHeight + EditorGUIUtility.standardVerticalSpacing;
            }

            var keys = DrawKeysBox(frame, controls.xMax);

            return keys.width > 0f ? new[] { band, keys } : new[] { band };
        }

        // How many rows the area controls take: the area, and for a clicked region its edge strength, the
        // way a click selects and, while contiguous, the directions it grows through.
        private int AreaRows () => area != AreaMode.ClickedRegion ? 1 : tool == SelectTool.Part && contiguous ? 4 : 3;

        // The area controls, in the header of the original image, where the clicks they set happen. Drawn
        // at set places, row under row, not by the automatic layout: the previews are drawn in their own
        // rects, and layout calls there would not match between the passes of a frame.
        private void DrawAreaControls (Rect controls)
        {
            var rowHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            var row = new Rect(controls.x, controls.y, controls.width, EditorGUIUtility.singleLineHeight);

            EditorGUI.BeginChangeCheck();

            var wasArea = area;
            var areaRow = row;

            // In a clicked region the row also holds one button, in one place: Every match while nothing
            // is selected, Clear once something is.
            if (area == AreaMode.ClickedRegion)
            {
                const float buttonWidth = 80f;

                areaRow.width -= buttonWidth + 4f;

                var button = new Rect(areaRow.xMax + 4f, row.y, buttonWidth, row.height);

                if (HasSelection())
                {
                    if (GUI.Button(button, new GUIContent("Clear", "Clears the selection. Esc does the same."), EditorStyles.miniButton))
                        ClearSelection();
                }
                else if (GUI.Button(button, new GUIContent("Every match", "Selects every pixel the From colour matches, as the whole image would change, to take out what shouldn't be in it."), EditorStyles.miniButton))
                {
                    SelectEveryMatch();
                }
            }

            area = (AreaMode) EditorGUI.EnumPopup(areaRow, new GUIContent("Area",
                "Whole image changes the colour everywhere. Clicked region changes only the part you click on the original image, stopping where the colour changes sharply. Switching to it with nothing selected selects what the whole image changed, to take out what shouldn't be in it."), area);

            // From the whole image to a clicked region, with nothing selected yet: start from every
            // pixel the whole image changed, so the ones that shouldn't change can be taken out.
            if (wasArea == AreaMode.WholeImage && area == AreaMode.ClickedRegion && !HasSelection())
                SelectEveryMatch();

            if (area == AreaMode.ClickedRegion)
            {
                row.y += rowHeight;
                edgeStrength = EditorGUI.Slider(row, new GUIContent("Edge strength",
                    "How sharp a change between two neighbouring pixels has to be to stop the region, 0 to 100. Lower it when the region leaks into a part next to it; raise it when it stops inside the part, at a step of its own shading."), edgeStrength, 1f, 100f);

                row.y += rowHeight;

                var buttons = EditorGUI.PrefixLabel(row, new GUIContent("Selection",
                    "How the next click selects. It doesn't change what is already selected.\n\n" +
                    "Non-contiguous takes every pixel of the frame close to the clicked one, joined or not; the edge strength is then how far from the clicked pixel they can be. Contiguous grows from the clicked pixel to its neighbours. Single pixel takes only the clicked pixel, and dragging keeps going."));
                var way = tool == SelectTool.Pixel ? 2 : contiguous ? 1 : 0;

                way = GUI.Toolbar(buttons, way, new[] { "Non-contiguous", "Contiguous", "Single pixel" }, EditorStyles.miniButton);
                tool = way == 2 ? SelectTool.Pixel : SelectTool.Part;
                contiguous = way == 1 || (way == 2 && contiguous);

                if (tool == SelectTool.Part && contiguous)
                {
                    row.y += rowHeight;
                    buttons = EditorGUI.PrefixLabel(row, new GUIContent("Grow through",
                        "4 directions grows only to the pixels beside, above and below. 8 directions also crosses corners, so thin diagonal lines are taken whole, but the region can slip through a diagonal gap in an outline."));
                    diagonals = GUI.Toolbar(buttons, diagonals ? 1 : 0, new[] { "4 directions", "8 directions" }, EditorStyles.miniButton) == 1;
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                dirty = true;
                Repaint();
            }
        }

        // The result in its frame. Add-ons can put controls in rows under the title, and show something
        // else in place of the result, such as an animation.
        private void DrawResultPreview (Rect frame)
        {
            GUI.Box(frame, GUIContent.none, EditorStyles.helpBox);

            // The title and the add-on rows share one darker band, as wide as the widest of them.
            var top = frame.y + PreviewTitleHeight + 2f;
            var rows = addons.Where(addon => addon.ResultHeaderHeight > 0f).ToList();
            var headerHeight = rows.Sum(addon => addon.ResultHeaderHeight + 2f);
            var headerWidth = rows.Select(addon => addon.ResultHeaderWidth > 0f ? addon.ResultHeaderWidth : frame.width)
                .DefaultIfEmpty(0f)
                .Max();

            DrawHeaderBand(frame, Mathf.Max(TitleWidth("Preview"), headerWidth + 12f), top + headerHeight - frame.y);
            GUI.Label(new Rect(frame.x + 6f, frame.y + 2f, frame.width - 12f, PreviewTitleHeight), "Preview", EditorStyles.miniBoldLabel);

            // How much changed, in the top right corner, on a grey note like the keys.
            changedStyle ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };

            var changed = new GUIContent($"{changedPixels} of {original.width * original.height} pixels changed");
            var changedWidth = changedStyle.CalcSize(changed).x + 8f;
            var note = new Rect(frame.xMax - 6f - changedWidth, frame.y + 3f, changedWidth, PreviewTitleHeight - 2f);

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(note, NoteColour);

            GUI.Label(note, changed, changedStyle);

            foreach (var addon in addons)
            {
                var height = addon.ResultHeaderHeight;

                if (height <= 0f)
                    continue;

                addon.DrawResultHeader(new Rect(frame.x + 6f, top, frame.width - 12f, height));
                top += height + 2f;
            }

            var content = new Rect(frame.x + 4f, top, frame.width - 8f, frame.yMax - 4f - top);

            if (content.height <= 0f)
                return;

            if (!addons.Any(addon => addon.DrawResult(content)))
                EditorGUI.DrawTextureTransparent(content, result, ScaleMode.ScaleToFit);
        }

        // An image with a small label over it, such as one side of the preview split in two.
        internal static void DrawLabelledImage (Rect area, Texture2D texture, string label)
        {
            GUI.Label(new Rect(area.x + 2f, area.y, area.width - 4f, PreviewTitleHeight), label, EditorStyles.miniLabel);
            EditorGUI.DrawTextureTransparent(new Rect(area.x, area.y + PreviewTitleHeight, area.width, area.height - PreviewTitleHeight), texture, ScaleMode.ScaleToFit);
        }

        // The colour the result goes to: the To colour, or what an add-on asks for, such as a step of a
        // transition while its sheet is made.
        internal Color Target ()
        {
            foreach (var addon in addons)
                if (addon.TargetColour(out var colour))
                    return colour;

            return toColor;
        }

        // While a slider is dragged or pixels are painted, an image slow to redo is redone only a few
        // times a second, and once more when the mouse is let go. A quick one is always redone.
        internal bool RefreshNow ()
        {
            var dragging = GUIUtility.hotControl != 0 || strokeGroup >= 0;

            if (!dragging || lastRefreshSeconds < SlowRefreshSeconds || EditorApplication.timeSinceStartup - lastRefreshTime >= DragRefreshSeconds)
                return true;

            Repaint();

            return false;
        }

        internal void Refresh ()
        {
            var started = EditorApplication.timeSinceStartup;

            RefreshNowFully();

            lastRefreshTime = EditorApplication.timeSinceStartup;
            lastRefreshSeconds = lastRefreshTime - started;
        }

        internal void RefreshNowFully ()
        {
            foreach (var addon in addons)
                addon.BeforeRecolor();

            Recolor();
        }

        // What a click on the original image does, with each key. The keys of a clicked region only while
        // it is on.
        private (string keys, string action)[] Keys ()
        {
            var keys = new List<(string, string)> { ("Click", "Pick the From colour"), ("Hold Alt", "Hide the zoom") };

            if (area == AreaMode.ClickedRegion)
                keys.AddRange(new[]
                {
                    ("Shift + click", "Add to the selection"),
                    (RemoveKey() + " + click", "Remove from it"),
                    ("Drag", "Keep going (Single pixel)"),
                    ("Esc", "Clear the selection")
                });

            return keys.ToArray();
        }

        private static GUIStyle KeyStyle ()
        {
            shortcutLabel ??= new GUIStyle(EditorStyles.miniLabel);
            shortcutLabel.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.62f, 0.62f, 0.62f) : new Color(0.35f, 0.35f, 0.35f);

            return shortcutLabel;
        }

        // At the bottom of the settings, only while the original image is too narrow to hold the keys.
        internal void DrawShortcuts ()
        {
            if (keysInPreview)
                return;

            // A thin line sets the summary apart from the settings that scroll above it.
            var line = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));

            EditorGUI.DrawRect(line, EditorGUIUtility.isProSkin ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.6f, 0.6f, 0.6f));
            EditorGUILayout.LabelField("On the original image", EditorStyles.boldLabel);

            foreach (var (keys, action) in Keys())
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(keys, KeyStyle(), GUILayout.Width(90f));
                    EditorGUILayout.LabelField(action, KeyStyle());
                }
        }

        // The keys in a grey box at the top right of the original image, like a note. Left out, and shown
        // under the settings instead, when the frame is too narrow to hold them beside the area controls.
        // Returns where the box is, or an empty rect when it is left out.
        private Rect DrawKeysBox (Rect frame, float controlsRight)
        {
            const float keysWidth = 74f;
            const float actionWidth = 124f;
            const float lineHeight = 14f;
            const float padding = 4f;

            var keys = Keys();
            var box = new Rect(frame.xMax - 6f - keysWidth - actionWidth - padding * 2f, frame.y + 4f,
                keysWidth + actionWidth + padding * 2f, keys.Length * lineHeight + padding * 2f);

            keysInPreview = box.x >= controlsRight + 8f;

            if (!keysInPreview)
                return Rect.zero;

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(box, NoteColour);

                for (var i = 0; i < keys.Length; i++)
                {
                    var y = box.y + padding + i * lineHeight;

                    GUI.Label(new Rect(box.x + padding, y, keysWidth, lineHeight), keys[i].keys, KeyStyle());
                    GUI.Label(new Rect(box.x + padding + keysWidth, y, actionWidth, lineHeight), keys[i].action, KeyStyle());
                }
            }

            return box;
        }

        // A bold title with an arrow that opens and closes the section under it. A click on the title works too.
        internal static bool Section (string title, string tooltip, bool open)
        {
            EditorGUILayout.Space();

            sectionStyle ??= new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };

            return EditorGUILayout.Foldout(open, new GUIContent(title, tooltip), true, sectionStyle);
        }


        // A framed image with its title in the top left corner, on a strip of its own above the image.
        internal static void DrawPreview (Rect frame, Texture2D texture, string title)
        {
            GUI.Box(frame, GUIContent.none, EditorStyles.helpBox);
            DrawHeaderBand(frame, TitleWidth(title), PreviewTitleHeight + 2f);
            GUI.Label(new Rect(frame.x + 6f, frame.y + 2f, frame.width - 12f, PreviewTitleHeight), title, EditorStyles.miniBoldLabel);
            EditorGUI.DrawTextureTransparent(Inset(frame), texture, ScaleMode.ScaleToFit);
        }

        // A darker band at the top left of a frame, behind its title, as wide as what it holds.
        private static void DrawHeaderBand (Rect frame, float width, float height) => DrawBand(HeaderBand(frame, width, height));

        private static Rect HeaderBand (Rect frame, float width, float height)
            => new(frame.x + 1f, frame.y + 1f, Mathf.Min(width, frame.width - 2f), height - 1f);

        // Nearly opaque, as it can sit over the image.
        private static void DrawBand (Rect band)
        {
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(band, EditorGUIUtility.isProSkin ? new Color(0.16f, 0.16f, 0.16f, 0.92f) : new Color(0.76f, 0.76f, 0.76f, 0.92f));
        }

        // The title's width, with the room on both sides of it.
        private static float TitleWidth (string title) => EditorStyles.miniBoldLabel.CalcSize(new GUIContent(title)).x + 12f;

        // The image area of a frame, below its title.
        internal static Rect Inset (Rect frame)
            => new(frame.x + 4f, frame.y + PreviewTitleHeight + 2f, frame.width - 8f, frame.height - PreviewTitleHeight - 6f);

        // Which of count images plays now, at a number of frames per second. Ping-pong goes back to the
        // start after the end instead of jumping there, without repeating the two ends.
        internal static int PlayIndex (int count, float fps, bool pingPong)
        {
            if (count <= 1)
                return 0;

            var frame = (int) (EditorApplication.timeSinceStartup * fps);

            if (!pingPong)
                return frame % count;

            var cycle = frame % (count * 2 - 2);

            return cycle < count ? cycle : count * 2 - 2 - cycle;
        }

        private static Texture2D checkerTexture;

        // A grey checkerboard, 8 points a square, to show clear pixels.
        internal static Texture2D Checker ()
        {
            if (checkerTexture != null)
                return checkerTexture;

            checkerTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave };

            var light = new Color(0.8f, 0.8f, 0.8f);
            var dark = new Color(0.6f, 0.6f, 0.6f);

            checkerTexture.SetPixels(new[] { light, dark, dark, light });
            checkerTexture.Apply();

            return checkerTexture;
        }

        // A click on the original image sets the From colour.
        internal void HandlePicking (Rect previewArea, Rect[] blocked = null)
        {
            var current = Event.current;
            var image = FitRect(previewArea, original);
            var painting = area == AreaMode.ClickedRegion && tool == SelectTool.Pixel;

            EditorGUIUtility.AddCursorRect(image, painting && EditorGUI.actionKey ? MouseCursor.ArrowMinus : MouseCursor.ArrowPlus);

            // A whole stroke is one step of undo.
            if (current.type == EventType.MouseUp && strokeGroup >= 0)
            {
                Undo.CollapseUndoOperations(strokeGroup);
                strokeGroup = -1;
            }

            // Repaint on every move so the loupe follows the cursor and goes away when it leaves the image.
            if (current.type == EventType.MouseMove)
                Repaint();

            // Over the header: its controls take the click, and the zoom stays out of their way.
            if (blocked != null && blocked.Any(rect => rect.Contains(current.mousePosition)))
                return;

            if (!TryGetPixel(image, current.mousePosition, out var hovered, out var coords))
                return;

            // Single pixel: a click starts a new selection with that pixel, Shift adds and the remove key takes
            // away, and dragging keeps doing the same.
            if (painting && (current.type == EventType.MouseDown || current.type == EventType.MouseDrag) && current.button == 0)
            {
                if (current.type == EventType.MouseDown)
                {
                    Undo.IncrementCurrentGroup();
                    strokeGroup = Undo.GetCurrentGroup();
                    strokeStartsNew = !current.shift && !EditorGUI.actionKey;

                    if (strokeStartsNew && hovered.a > 0f)
                    {
                        hovered.a = 1f;
                        fromColor = hovered;
                        seeds.Clear();
                        addedPixels.Clear();
                        removedPixels.Clear();
                        SelectionChanged(true);

                        if (lastRegion != null)
                            Array.Clear(lastRegion, 0, lastRegion.Length);
                    }
                }

                if (Paint(coords, !strokeStartsNew && EditorGUI.actionKey))
                    dirty = true;

                current.Use();
                Repaint();
            }

            if (!painting && current.type == EventType.MouseDown && current.button == 0)
            {
                if (hovered.a > 0f)
                {
                    var region = area == AreaMode.ClickedRegion;

                    if (region && current.shift)
                    {
                        seeds.Add(NewSeed(coords));
                    }
                    else if (region && EditorGUI.actionKey)
                    {
                        seeds.Add(NewSeed(coords, true));
                    }
                    else
                    {
                        hovered.a = 1f;
                        fromColor = hovered;
                        seeds.Clear();
                        seeds.Add(NewSeed(coords));
                        addedPixels.Clear();
                        removedPixels.Clear();
                    }

                    SelectionChanged(!region || (!current.shift && !EditorGUI.actionKey));
                    dirty = true;
                }

                current.Use();
                Repaint();
            }

            if (current.type != EventType.Repaint)
                return;

            // A magnified view of the pixels around the cursor; holding Alt shows only a swatch of the colour.
            if (!current.alt)
            {
                DrawLoupe(current.mousePosition, coords, hovered);

                return;
            }

            var swatch = new Rect(current.mousePosition.x + 14f, current.mousePosition.y + 14f, 26f, 26f);

            EditorGUI.DrawRect(swatch, Color.black);
            EditorGUI.DrawRect(new Rect(swatch.x + 1f, swatch.y + 1f, swatch.width - 2f, swatch.height - 2f), Color.white);
            EditorGUI.DrawRect(new Rect(swatch.x + 2f, swatch.y + 2f, swatch.width - 4f, swatch.height - 4f), new Color(hovered.r, hovered.g, hovered.b, 1f));
        }

        // Puts one pixel into the selection, or takes it out. Only what changes the selection is kept, so
        // going over pixels already in (or out) adds nothing. Clear pixels are left alone.
        internal bool Paint (Vector2Int pixel, bool erase)
        {
            if (lastPixels == null || !IsInside(pixel))
                return false;

            var index = pixel.y * original.width + pixel.x;

            if (lastPixels[index].a == 0)
                return false;

            var inRegion = lastRegion != null && lastRegion[index];
            var changed = false;

            if (erase)
            {
                changed |= addedPixels.Remove(pixel);

                if (inRegion && !removedPixels.Contains(pixel))
                {
                    removedPixels.Add(pixel);
                    changed = true;
                }
            }
            else
            {
                changed |= removedPixels.Remove(pixel);

                if (!inRegion && !addedPixels.Contains(pixel))
                {
                    addedPixels.Add(pixel);
                    changed = true;
                }
            }

            // Keeps the rest of the stroke from taking the pixel twice before the next recolour.
            if (lastRegion != null)
                lastRegion[index] = !erase;

            return changed;
        }

        // A magnified view of the pixels around the cursor, with the one a click would take framed in the
        // middle and its colour below.
        internal void DrawLoupe (Vector2 mouse, Vector2Int coords, Color hovered)
        {
            var size = LoupePixels * LoupeCell;
            var box = new Rect(mouse.x + 16f, mouse.y + 16f, size + 4f, size + LoupeStripHeight + 6f);

            // Flip to the other side of the cursor near the edges of the window.
            if (box.xMax > position.width)
                box.x = mouse.x - 16f - box.width;

            if (box.yMax > position.height)
                box.y = mouse.y - 16f - box.height;

            box.x = Mathf.Max(0f, box.x);
            box.y = Mathf.Max(0f, box.y);

            var view = new Rect(box.x + 2f, box.y + 2f, size, size);

            EditorGUI.DrawRect(box, Color.black);
            EditorGUI.DrawRect(view, new Color(0.22f, 0.22f, 0.22f));

            // Only the part inside the image is drawn, so nothing wraps around from the other side.
            var radius = LoupePixels / 2;
            var xMin = Mathf.Max(0, coords.x - radius);
            var yMin = Mathf.Max(0, coords.y - radius);
            var xMax = Mathf.Min(original.width, coords.x + radius + 1);
            var yMax = Mathf.Min(original.height, coords.y + radius + 1);

            // Texture rows go up and GUI rows go down, so the top of the view is the highest row.
            var drawn = new Rect(
                view.x + (xMin - (coords.x - radius)) * LoupeCell,
                view.y + (coords.y + radius + 1 - yMax) * LoupeCell,
                (xMax - xMin) * LoupeCell,
                (yMax - yMin) * LoupeCell);
            var uv = new Rect((float) xMin / original.width, (float) yMin / original.height,
                (float) (xMax - xMin) / original.width, (float) (yMax - yMin) / original.height);

            GUI.DrawTextureWithTexCoords(drawn, original, uv, true);

            if (area == AreaMode.ClickedRegion && regionShade != null)
                GUI.DrawTextureWithTexCoords(drawn, regionShade, uv, true);

            var centre = new Rect(view.x + radius * LoupeCell, view.y + radius * LoupeCell, LoupeCell, LoupeCell);

            DrawFrame(centre, 2f, Color.black);
            DrawFrame(new Rect(centre.x + 1f, centre.y + 1f, centre.width - 2f, centre.height - 2f), 1f, Color.white);

            var strip = new Rect(view.x, view.yMax + 2f, size, LoupeStripHeight);
            var opaque = new Color(hovered.r, hovered.g, hovered.b, 1f);

            EditorGUI.DrawRect(strip, opaque);

            loupeLabel ??= new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
            loupeLabel.normal.textColor = opaque.grayscale > 0.5f ? Color.black : Color.white;

            GUI.Label(strip, hovered.a > 0f ? "#" + ColorUtility.ToHtmlStringRGB(hovered) : "Clear", loupeLabel);
        }

        internal static void DrawFrame (Rect rect, float thickness, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        // A wheel for the To colour: the angle is the hue and the distance from the centre is the saturation.
        // It keeps the brightness of the To colour.
        internal void DrawWheel (Rect wheel)
        {
            var id = GUIUtility.GetControlID(FocusType.Passive, wheel);
            var current = Event.current;
            var center = wheel.center;
            var radius = wheel.width * 0.5f;

            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown when current.button == 0 && Vector2.Distance(current.mousePosition, center) <= radius:
                    GUIUtility.hotControl = id;
                    PickFromWheel(current.mousePosition, center, radius);
                    current.Use();

                    break;

                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    PickFromWheel(current.mousePosition, center, radius);
                    current.Use();

                    break;

                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    current.Use();

                    break;

                case EventType.Repaint:
                    GUI.DrawTexture(wheel, WheelTexture());

                    // A small square marks where the To colour sits on the wheel.
                    Color.RGBToHSV(toColor, out var h, out var s, out _);

                    var angle = h * Mathf.PI * 2f;
                    var mark = center + new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle)) * s * radius;

                    EditorGUI.DrawRect(new Rect(mark.x - 4f, mark.y - 4f, 8f, 8f), Color.black);
                    EditorGUI.DrawRect(new Rect(mark.x - 3f, mark.y - 3f, 6f, 6f), Color.white);
                    EditorGUI.DrawRect(new Rect(mark.x - 2f, mark.y - 2f, 4f, 4f), new Color(toColor.r, toColor.g, toColor.b, 1f));

                    break;
            }

            EditorGUIUtility.AddCursorRect(wheel, MouseCursor.ArrowPlus);
        }

        internal void PickFromWheel (Vector2 mouse, Vector2 center, float radius)
        {
            // GUI y grows down, the angle on the wheel grows counter-clockwise.
            var offset = new Vector2(mouse.x - center.x, center.y - mouse.y);
            var hue = Mathf.Repeat(Mathf.Atan2(offset.y, offset.x) / (Mathf.PI * 2f), 1f);
            var saturation = Mathf.Clamp01(offset.magnitude / radius);

            Color.RGBToHSV(toColor, out _, out _, out var value);

            // A black To would stay black whatever the wheel says, so it is lifted to full brightness.
            if (value < 0.05f)
                value = 1f;

            var picked = Color.HSVToRGB(hue, saturation, value);

            picked.a = toColor.a;
            toColor = picked;
            dirty = true;
            Repaint();
        }

        internal static Texture2D WheelTexture ()
        {
            if (wheelTexture != null)
                return wheelTexture;

            const int size = 128;

            wheelTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[size * size];
            var half = size * 0.5f;

            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = (x + 0.5f - half) / half;
                var dy = (y + 0.5f - half) / half;
                var distance = Mathf.Sqrt(dx * dx + dy * dy);
                var color = Color.HSVToRGB(Mathf.Repeat(Mathf.Atan2(dy, dx) / (Mathf.PI * 2f), 1f), Mathf.Clamp01(distance), 1f);

                // Soft one-pixel rim so the circle edge is not jagged.
                color.a = Mathf.Clamp01((1f - distance) * half);
                pixels[y * size + x] = color;
            }

            wheelTexture.SetPixels(pixels);
            wheelTexture.Apply();

            return wheelTexture;
        }

        // Where ScaleToFit draws the texture inside the area.
        internal static Rect FitRect (Rect area, Texture texture)
        {
            var aspect = (float) texture.width / texture.height;
            var width = area.width;
            var height = width / aspect;

            if (height > area.height)
            {
                height = area.height;
                width = height * aspect;
            }

            return new Rect(area.x + (area.width - width) * 0.5f, area.y + (area.height - height) * 0.5f, width, height);
        }

        internal bool TryGetPixel (Rect image, Vector2 mouse, out Color color, out Vector2Int coords)
        {
            color = default;
            coords = default;

            if (!image.Contains(mouse))
                return false;

            // GUI y grows down, texture y grows up.
            var x = Mathf.Clamp(Mathf.FloorToInt((mouse.x - image.x) / image.width * original.width), 0, original.width - 1);
            var y = Mathf.Clamp(Mathf.FloorToInt((1f - (mouse.y - image.y) / image.height) * original.height), 0, original.height - 1);

            color = original.GetPixel(x, y);
            coords = new Vector2Int(x, y);

            return true;
        }

        // The file is read from disk so the import settings don't need Read/Write on.
        internal void LoadOriginal ()
        {
            ReleasePreviews();
            loadedSource = source;

            if (source == null)
                return;

            var path = AssetDatabase.GetAssetPath(source);
            var texture = ReadImage(path);

            if (texture == null)
            {
                Debug.LogWarning($"[Recolor] Could not read {path}.");

                return;
            }

            original = texture;
            basePng = original.EncodeToPNG();
            pixelCache = null;
            result = new Texture2D(original.width, original.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            dirty = true;

            LoadFrames(path);

            // After a script reload an add-on can bring back the step being edited, not only the file.
            foreach (var addon in addons)
                addon.OnImageLoaded();
        }

        // PNG and JPG are read from the file, so their import settings don't matter. Anything else Unity
        // imports, such as Aseprite, PSD or TGA, is read from the imported texture.
        internal Texture2D ReadImage (string path)
        {
            readWarning = null;
            canFixImport = false;
            cropped = false;
            cropOffset = Vector2Int.zero;

            var extension = Path.GetExtension(path).ToLowerInvariant();
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };

            if (extension is ".png" or ".jpg" or ".jpeg")
            {
                if (File.Exists(path) && texture.LoadImage(File.ReadAllBytes(path)))
                    return texture;

                DestroyImmediate(texture);

                return null;
            }

            DestroyImmediate(texture);

            var imported = ReadImported(source);

            if (imported == null)
                return null;

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                // A large image is shrunk to the Max Size when it is imported.
                importer.GetSourceTextureWidthAndHeight(out var fileWidth, out var fileHeight);

                if (fileWidth > imported.width || fileHeight > imported.height)
                    readWarning = $"This image is {fileWidth} x {fileHeight}, but it is imported at {imported.width} x {imported.height}, the Max Size of its import settings. Recolor works on the imported size." +
                                  (readWarning != null ? " It is also compressed, so its colours are slightly off." : "");

                canFixImport = readWarning != null;

                return imported;
            }

            return CropToSprites(imported, path);
        }

        // Cuts a texture packed by its own importer down to the area of its sprites, leaving out the padding
        // and the empty space the packing adds.
        internal Texture2D CropToSprites (Texture2D texture, string path)
        {
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Where(sprite => sprite.texture == source).ToArray();

            if (sprites.Length == 0)
                return texture;

            var xMin = Mathf.Max(0, Mathf.FloorToInt(sprites.Min(sprite => sprite.rect.xMin)));
            var yMin = Mathf.Max(0, Mathf.FloorToInt(sprites.Min(sprite => sprite.rect.yMin)));
            var xMax = Mathf.Min(texture.width, Mathf.CeilToInt(sprites.Max(sprite => sprite.rect.xMax)));
            var yMax = Mathf.Min(texture.height, Mathf.CeilToInt(sprites.Max(sprite => sprite.rect.yMax)));

            if (xMax <= xMin || yMax <= yMin || (xMin == 0 && yMin == 0 && xMax == texture.width && yMax == texture.height))
                return texture;

            var croppedTexture = new Texture2D(xMax - xMin, yMax - yMin, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };

            croppedTexture.SetPixels(texture.GetPixels(xMin, yMin, xMax - xMin, yMax - yMin));
            croppedTexture.Apply();
            DestroyImmediate(texture);

            cropped = true;
            cropOffset = new Vector2Int(xMin, yMin);

            return croppedTexture;
        }

        internal void ImportAtFullSize ()
        {
            var path = AssetDatabase.GetAssetPath(source);

            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                return;

            importer.GetSourceTextureWidthAndHeight(out var width, out var height);

            var size = 32;

            while (size < Mathf.Max(width, height) && size < 16384)
                size *= 2;

            importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, size);
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            // Read at another size, so it is a new start.
            foreach (var addon in addons)
                addon.OnNewImage();

            LoadOriginal();
        }

        // The pixels of an imported texture, through the GPU, so it doesn't need Read/Write on. A compressed
        // texture comes back with the small colour changes of its compression.
        internal Texture2D ReadImported (Texture2D imported)
        {
            if (imported == null)
                return null;

            var width = imported.width;
            var height = imported.height;
            var copy = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };

            if (GraphicsFormatUtility.IsCompressedFormat(imported.graphicsFormat))
                readWarning = "This image is imported compressed, so its colours are slightly off. Set its Compression to None in the import settings for exact colours.";

            if (imported.isReadable && !GraphicsFormatUtility.IsCompressedFormat(imported.graphicsFormat))
            {
                copy.SetPixels32(imported.GetPixels32());
                copy.Apply();

                return copy;
            }

            var srgb = GraphicsFormatUtility.IsSRGBFormat(imported.graphicsFormat);
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            var active = RenderTexture.active;

            try
            {
                Graphics.Blit(imported, target);
                RenderTexture.active = target;
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                copy.Apply();
            }
            finally
            {
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
            }

            return copy;
        }

        // The texture behind what is selected: a texture, a sprite, or the main asset of a file that holds
        // one, such as an Aseprite or PSD file.
        internal static Texture2D TextureOf (UnityEngine.Object selected)
        {
            switch (selected)
            {
                case null:
                    return null;
                case Texture2D texture:
                    return texture;
                case Sprite sprite:
                    return sprite.texture;
            }

            var path = AssetDatabase.GetAssetPath(selected);

            if (string.IsNullOrEmpty(path))
                return null;

            var assets = AssetDatabase.LoadAllAssetsAtPath(path);

            return assets.OfType<Texture2D>().FirstOrDefault() ?? assets.OfType<Sprite>().FirstOrDefault()?.texture;
        }

        // The rects of the sliced sprites, in the pixels of the file. The imported texture can be smaller
        // than the file when its max size is lower, so the rects are scaled back up.
        internal void LoadFrames (string path)
        {
            frames.Clear();
            framePivots.Clear();

            // In name order, with numbers read as numbers, so walk_10 comes after walk_9.
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
                .OrderBy(sprite => NamePrefix(sprite.name), StringComparer.OrdinalIgnoreCase)
                .ThenBy(sprite => NameNumber(sprite.name))
                .ToArray();

            if (sprites.Length < 2 || sprites[0].texture == null)
                return;

            // A file read at its own size can be larger than the imported texture; a cut one is moved instead.
            var scaleX = cropped ? 1f : (float) original.width / sprites[0].texture.width;
            var scaleY = cropped ? 1f : (float) original.height / sprites[0].texture.height;

            foreach (var sprite in sprites)
            {
                var rect = Shifted(sprite.rect);
                var xMin = Mathf.Clamp(Mathf.FloorToInt(rect.xMin * scaleX), 0, original.width);
                var yMin = Mathf.Clamp(Mathf.FloorToInt(rect.yMin * scaleY), 0, original.height);
                var xMax = Mathf.Clamp(Mathf.CeilToInt(rect.xMax * scaleX), 0, original.width);
                var yMax = Mathf.Clamp(Mathf.CeilToInt(rect.yMax * scaleY), 0, original.height);

                if (xMax <= xMin || yMax <= yMin)
                    continue;

                frames.Add(new RectInt(xMin, yMin, xMax - xMin, yMax - yMin));
                framePivots.Add(new Vector2(sprite.pivot.x * scaleX, sprite.pivot.y * scaleY));
            }
        }

        internal static string NamePrefix (string name) => name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');

        internal static long NameNumber (string name)
            => long.TryParse(name.Substring(NamePrefix(name).Length), out var number) ? number : -1;

        // Reads the pixels of the image once and lists its colours.
        internal void CachePixels ()
        {
            if (pixelCache != null && pixelCache.Length == original.width * original.height)
                return;

            pixelCache = original.GetPixels32();
            colourOf = new int[pixelCache.Length];
            colourIndex = new Dictionary<int, int>();

            var found = new List<Color32>();

            for (var i = 0; i < pixelCache.Length; i++)
            {
                var pixel = pixelCache[i];
                var key = pixel.r | pixel.g << 8 | pixel.b << 16;

                if (!colourIndex.TryGetValue(key, out var index))
                {
                    index = found.Count;
                    colourIndex[key] = index;
                    found.Add(new Color32(pixel.r, pixel.g, pixel.b, 255));
                }

                colourOf[i] = index;
            }

            colours = found.ToArray();
            colourS = new float[colours.Length];
            colourV = new float[colours.Length];

            for (var k = 0; k < colours.Length; k++)
                Color.RGBToHSV(colours[k], out _, out colourS[k], out colourV[k]);

            isPureFull = new bool[colours.Length];
            isPureOther = new bool[colours.Length];
            nearStamp = new int[colours.Length];
            threeDone = new bool[colours.Length];
            threeFound = new bool[colours.Length];
            threeMiss = new float[colours.Length];
            threeMixed = new Color[colours.Length];
            pairDone = new bool[colours.Length];
            pairFound = new bool[colours.Length];
            pairMixed = new Color[colours.Length];
            movedColour = new Color[colours.Length];
            movedDone = new bool[colours.Length];
        }

        internal int IndexOf (Color32 colour) => colourIndex.TryGetValue(colour.r | colour.g << 8 | colour.b << 16, out var index) ? index : -1;

        internal void Recolor ()
        {
            dirty = false;
            matchByHue = UsesHue();
            changedPixels = 0;

            CachePixels();

            var pixels = pixelCache;
            var colourCount = colours.Length;
            var weights = new float[pixels.Length];
            var minS = 1f;
            var maxS = 0f;
            var minV = 1f;
            var maxV = 0f;

            Color.RGBToHSV(fromColor, out var fromH, out var fromS, out var fromV);
            Color.RGBToHSV(Target(), out var toH, out var toS, out var toV);

            // Going to white, black or a grey keeps the hue, so the shades that keep some colour don't turn red.
            if (HasNoHue(toS, toV))
                toH = fromH;

            // How much each colour matches the From colour, then each pixel takes its colour's.
            var colourWeight = new float[colourCount];

            for (var k = 0; k < colourCount; k++)
                colourWeight[k] = Weight(colours[k], fromH);

            for (var i = 0; i < pixels.Length; i++)
                weights[i] = colourWeight[colourOf[i]];

            lastPixels = pixels;
            lastWeights = weights;

            // In a clicked region only its pixels change, plus a one-pixel ring around it where its
            // anti-aliased edge sits. The pure colours for clean edges are then found in the whole image.
            var active = weights;
            bool[] nearRegion = null;

            if (area == AreaMode.ClickedRegion)
            {
                var region = FindRegion(pixels, weights, out var painted);

                lastRegion = (bool[]) region.Clone();
                nearRegion = Grow(region);
                active = new float[weights.Length];

                // A pixel painted in by hand changes in full, whatever its colour.
                for (var i = 0; i < weights.Length; i++)
                    active[i] = region[i] ? painted[i] ? 1f : weights[i] : 0f;

                BuildShade(region);
            }

            for (var i = 0; i < pixels.Length; i++)
            {
                if (active[i] <= 0f)
                    continue;

                var k = colourOf[i];

                minS = Mathf.Min(minS, colourS[k]);
                maxS = Mathf.Max(maxS, colourS[k]);
                minV = Mathf.Min(minV, colourV[k]);
                maxV = Mathf.Max(maxV, colourV[k]);
            }

            var shiftS = toS - fromS;
            var shiftV = toV - fromV;

            // Clamping each pixel on its own would merge the shades past the limit, flattening the shading.
            if (keepShading && minS <= maxS)
            {
                shiftS = Mathf.Clamp(shiftS, -minS, 1f - maxS);
                shiftV = Mathf.Clamp(shiftV, -minV, 1f - maxV);
            }

            landedColor = Color.HSVToRGB(toH, Mathf.Clamp01(fromS + shiftS), Mathf.Clamp01(fromV + shiftV));

            Color Move (Color pixel)
            {
                Color.RGBToHSV(pixel, out var h, out var s, out var v);

                var moved = Color.HSVToRGB(Mathf.Repeat(h + toH - fromH, 1f), Mathf.Clamp01(s + shiftS), Mathf.Clamp01(v + shiftV));

                moved.a = pixel.a;

                return moved;
            }

            // Every result kept per colour starts over: it depends on the settings.
            Array.Clear(movedDone, 0, colourCount);
            Array.Clear(threeDone, 0, colourCount);
            Array.Clear(pairDone, 0, colourCount);
            Array.Clear(isPureFull, 0, colourCount);
            Array.Clear(isPureOther, 0, colourCount);

            Color MovedColour (int k)
            {
                if (!movedDone[k])
                {
                    movedColour[k] = Move(colours[k]);
                    movedDone[k] = true;
                }

                return movedColour[k];
            }

            // The colour a pixel ends on depends only on its colour and how much it changes, which is its
            // colour's weight, or in full for a pixel painted in by hand. Its transparency stays.
            var partOut = new Color32[colourCount];
            var fullOut = new Color32[colourCount];
            var partDone = new bool[colourCount];
            var fullDone = new bool[colourCount];

            Color32 Changed (int k, float weight)
            {
                var full = weight >= 1f;

                if (full ? !fullDone[k] : !partDone[k])
                {
                    var changed = (Color32) Color.Lerp(colours[k], MovedColour(k), weight);

                    if (full)
                    {
                        fullOut[k] = changed;
                        fullDone[k] = true;
                    }
                    else
                    {
                        partOut[k] = changed;
                        partDone[k] = true;
                    }
                }

                return full ? fullOut[k] : partOut[k];
            }

            var output = new Color32[pixels.Length];
            var pureFull = new int[0];
            var pureOther = new int[0];

            if (cleanEdges)
            {
                FindPureColors(pixels, colourWeight, out pureFull, out pureOther);

                foreach (var k in pureFull)
                    isPureFull[k] = true;

                foreach (var k in pureOther)
                    isPureOther[k] = true;
            }

            for (var i = 0; i < pixels.Length; i++)
            {
                Color pixel = pixels[i];
                var weight = active[i];

                output[i] = pixels[i];

                if (nearRegion != null && !nearRegion[i])
                    continue;

                var colour = colourOf[i];

                // A pure colour is never a mix, so most pixels skip the clean edges check at once.
                if (cleanEdges && pixel.a > 0f && !isPureFull[colour] && !isPureOther[colour] && TryUnmix(pixels, i, pureFull, pureOther, Move, out var mixed))
                {
                    mixed.a = pixel.a;
                    output[i] = mixed;
                    changedPixels++;

                    continue;
                }

                if (weight <= 0f)
                    continue;

                var changed = Changed(colour, weight);

                changed.a = pixels[i].a;
                output[i] = changed;
                changedPixels++;
            }

            result.SetPixels32(output);
            result.Apply();
        }

        internal bool IsInside (Vector2Int point) => point.x >= 0 && point.y >= 0 && point.x < original.width && point.y < original.height;

        // The sprite a pixel belongs to, so a region never spills into the next frame. The whole image
        // when it is not sliced.
        internal RectInt FrameOf (Vector2Int point)
        {
            foreach (var frame in frames)
                if (frame.Contains(point))
                    return frame;

            return new RectInt(0, 0, original.width, original.height);
        }

        // Every region grown from the clicked pixels.
        internal bool[] FindRegion (Color32[] pixels, float[] weights, out bool[] painted)
        {
            var region = new bool[pixels.Length];

            // Each click grows on its own, so a click inside a part already taken still reaches what its
            // own way reaches. Clicks apply in order: a removing one takes away what it reaches, and a later
            // adding one can bring pixels back. One scratch mark is reused and cleared through what it reached.
            var mark = new bool[pixels.Length];
            var reached = new List<int>();

            foreach (var seed in seeds)
            {
                // Every pixel the From colour matches, wherever it is, as the whole image would change.
                if (seed.everyMatch)
                {
                    for (var i = 0; i < pixels.Length; i++)
                        if (weights[i] > 0f && pixels[i].a > 0)
                            region[i] = !seed.remove;

                    continue;
                }

                if (!IsInside(seed.point))
                    continue;

                reached.Clear();
                Flood(seed, pixels, weights, FrameOf(seed.point), mark, reached);

                foreach (var index in reached)
                {
                    region[index] = !seed.remove;
                    mark[index] = false;
                }
            }

            // Then the pixels painted in and out by hand.
            painted = new bool[pixels.Length];

            foreach (var pixel in addedPixels)
            {
                if (!IsInside(pixel))
                    continue;

                var index = pixel.y * original.width + pixel.x;

                if (pixels[index].a == 0)
                    continue;

                region[index] = true;
                painted[index] = true;
            }

            foreach (var pixel in removedPixels)
            {
                if (!IsInside(pixel))
                    continue;

                var index = pixel.y * original.width + pixel.x;

                region[index] = false;
                painted[index] = false;
            }

            return region;
        }

        // Grows from a pixel to its neighbours while they match the From colour and the step from one to
        // the next stays below the edge strength. Smooth shading moves in small steps and is crossed; an
        // outline or a sharp change of shade stops it. Marks what it reaches and, when asked, lists it.
        // Beside, above and below first; the four corners after them are used only with diagonals on.
        internal static readonly int[] NeighbourX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        internal static readonly int[] NeighbourY = { 0, 0, 1, -1, 1, -1, 1, -1 };

        // A click made now grows the way the options say. A removing click takes what it reaches away.
        internal RegionSeed NewSeed (Vector2Int point, bool remove = false) => new(point, contiguous, diagonals, remove);

        internal void Flood (RegionSeed seed, Color32[] pixels, float[] weights, RectInt bounds, bool[] mark, List<int> reached)
            => Flood(seed.point.y * original.width + seed.point.x, seed.contiguous, seed.diagonals, pixels, weights, bounds, mark, reached);

        internal void Flood (int start, bool contiguous, bool diagonals, Color32[] pixels, float[] weights, RectInt bounds, bool[] mark, List<int> reached)
        {
            if (mark[start] || weights[start] <= 0f || pixels[start].a == 0)
                return;

            var width = original.width;

            // Same scale as the RGB tolerance: distance where 100 is black to white.
            var limit = edgeStrength / 100f * Mathf.Sqrt(3f) * 255f;
            var limitSquared = limit * limit;
            var queue = new Queue<int>();
            var neighbours = diagonals ? 8 : 4;

            mark[start] = true;
            reached?.Add(start);

            if (!contiguous)
            {
                // Every pixel of the frame close enough to the clicked one, wherever it is.
                var clicked = pixels[start];

                for (var y = bounds.yMin; y < bounds.yMax; y++)
                    for (var x = bounds.xMin; x < bounds.xMax; x++)
                    {
                        var index = y * width + x;

                        if (mark[index] || weights[index] <= 0f || pixels[index].a == 0)
                            continue;

                        float r = pixels[index].r - clicked.r;
                        float g = pixels[index].g - clicked.g;
                        float b = pixels[index].b - clicked.b;

                        if (r * r + g * g + b * b > limitSquared)
                            continue;

                        mark[index] = true;
                        reached?.Add(index);
                    }

                return;
            }

            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                var x = index % width;
                var y = index / width;

                for (var n = 0; n < neighbours; n++)
                {
                    var nx = x + NeighbourX[n];
                    var ny = y + NeighbourY[n];

                    if (nx < bounds.xMin || ny < bounds.yMin || nx >= bounds.xMax || ny >= bounds.yMax)
                        continue;

                    var next = ny * width + nx;

                    if (mark[next] || weights[next] <= 0f || pixels[next].a == 0)
                        continue;

                    float r = pixels[next].r - pixels[index].r;
                    float g = pixels[next].g - pixels[index].g;
                    float b = pixels[next].b - pixels[index].b;

                    if (r * r + g * g + b * b > limitSquared)
                        continue;

                    mark[next] = true;
                    reached?.Add(next);
                    queue.Enqueue(next);
                }
            }
        }

        // The region plus every pixel touching it, corners included.
        internal bool[] Grow (bool[] region)
        {
            var width = original.width;
            var height = original.height;
            var grown = new bool[region.Length];

            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    if (!region[y * width + x])
                        continue;

                    for (var ny = Mathf.Max(0, y - 1); ny <= Mathf.Min(height - 1, y + 1); ny++)
                        for (var nx = Mathf.Max(0, x - 1); nx <= Mathf.Min(width - 1, x + 1); nx++)
                            grown[ny * width + nx] = true;
                }

            return grown;
        }

        // A dark veil over everything outside the region, so the selected part stands out in its own
        // colours. Nothing is dimmed while no part is selected.
        internal void BuildShade (bool[] region)
        {
            var width = original.width;
            var height = original.height;

            if (regionShade == null || regionShade.width != width || regionShade.height != height)
            {
                if (regionShade != null)
                    DestroyImmediate(regionShade);

                regionShade = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            }

            var any = Array.IndexOf(region, true) >= 0;
            var veil = new Color32(0, 0, 0, any ? ShadeAlpha : (byte) 0);
            var shade = new Color32[region.Length];

            for (var i = 0; i < region.Length; i++)
                shade[i] = region[i] ? default : veil;

            regionShade.SetPixels32(shade);
            regionShade.Apply();
        }

        // The flat colours the image is painted with, changed or left alone: the ends a wide fade runs
        // between, too far from its middle to be found among the neighbours. A fade repeats its middle
        // colours too, so a common colour sitting right on the line between two purer ones is dropped;
        // the check is strict, so a shade painted on purpose next to a mix is kept.
        internal void FindPureColors (Color32[] pixels, float[] colourWeight, out int[] full, out int[] other)
        {
            var colourCount = colours.Length;
            var counts = new int[colourCount];
            var firstSeen = new int[colourCount];
            var minimum = Mathf.Max(4, pixels.Length / 1000);
            var strict = Mathf.Max(4f, mixTolerance / 3f) / 255f;
            var fullList = new List<Color32>();
            var otherList = new List<Color32>();
            var fullIndices = new List<int>();
            var otherIndices = new List<int>();

            // Colours fading in or out are no flat area; the rest are counted, in the order first met.
            for (var i = 0; i < pixels.Length; i++)
            {
                var k = colourOf[i];
                var weight = colourWeight[k];

                if (pixels[i].a == 0 || (weight > 0f && weight < 1f))
                    continue;

                if (counts[k] == 0)
                    firstSeen[k] = i;

                counts[k]++;
            }

            var common = Enumerable.Range(0, colourCount)
                .Where(k => counts[k] >= minimum)
                .OrderByDescending(k => counts[k])
                .ThenBy(k => firstSeen[k])
                .Take(CommonColorCount * 4);

            foreach (var k in common)
            {
                if (IsMixOf(colours[k], fullList, otherList, strict))
                    continue;

                var changed = colourWeight[k] >= 1f;
                var list = changed ? fullList : otherList;

                if (list.Count >= CommonColorCount)
                    continue;

                list.Add(colours[k]);
                (changed ? fullIndices : otherIndices).Add(k);
            }

            full = fullIndices.ToArray();
            other = otherIndices.ToArray();
        }

        internal static bool IsMixOf (Color color, List<Color32> fullList, List<Color32> otherList, float strict)
        {
            Color unusedFull = default, unusedOther = default;
            var amount = 0f;

            foreach (var candidateFull in fullList)
                foreach (var candidateOther in otherList)
                    Fit(color, candidateFull, candidateOther, ref strict, ref unusedFull, ref unusedOther, ref amount);

            return amount > 0f;
        }

        // A pixel can be a mix of a pure changed colour and a pure colour left alone. The pair is looked
        // for among its neighbours first, then across the whole image for the middle of a wide fade. Both
        // ends have to be pure: a neighbour that is itself a mix would explain any colour as a mix. What
        // depends only on the pixel's colour is worked out once for that colour.
        internal bool TryUnmix (Color32[] pixels, int index, int[] pureFull, int[] pureOther, Func<Color, Color> move, out Color mixed)
        {
            var width = original.width;
            var height = original.height;
            var x = index % width;
            var y = index / width;
            var colour = colourOf[index];
            Color pixel = pixels[index];
            var start = mixTolerance / 255f;
            var best = start;

            Color full = default, other = default;
            var amount = 0f;

            mixed = default;

            // A pure colour is a flat area of its own, not a mix.
            if (isPureFull[colour] || isPureOther[colour])
                return false;

            nearFull.Clear();
            nearOther.Clear();

            // A new mark for this pixel's neighbours; the marks start over before the count runs out.
            if (++stamp == int.MaxValue)
            {
                Array.Clear(nearStamp, 0, nearStamp.Length);
                stamp = 1;
            }

            for (var ny = Mathf.Max(0, y - EdgeRadius); ny <= Mathf.Min(height - 1, y + EdgeRadius); ny++)
                for (var nx = Mathf.Max(0, x - EdgeRadius); nx <= Mathf.Min(width - 1, x + EdgeRadius); nx++)
                {
                    var neighbour = colourOf[ny * width + nx];

                    if (nearStamp[neighbour] == stamp)
                        continue;

                    if (isPureFull[neighbour])
                        nearFull.Add(neighbour);
                    else if (isPureOther[neighbour])
                        nearOther.Add(neighbour);
                    else
                        continue;

                    nearStamp[neighbour] = stamp;
                }

            foreach (var candidateFull in nearFull)
                foreach (var candidateOther in nearOther)
                    Fit(pixel, colours[candidateFull], colours[candidateOther], ref best, ref full, ref other, ref amount);

            // Where three colours meet, a pixel is a mix of all three: no pair explains it, or only
            // roughly, and three colours fit it much closer. The closest blend of three is the same for
            // any limit, so it is found once per colour at the widest one and checked against this one.
            var threeLimit = amount > 0f ? best * 0.5f : best;

            if (amount <= 0f || best > ExactFit)
            {
                if (!threeDone[colour])
                {
                    threeFound[colour] = TryUnmixThree(pixel, pureFull, pureOther, start, move, out threeMixed[colour], out threeMiss[colour]);
                    threeDone[colour] = true;
                }

                if (threeFound[colour] && threeMiss[colour] < threeLimit)
                {
                    mixed = threeMixed[colour];

                    return true;
                }
            }

            if (amount > 0f)
            {
                mixed = Color.Lerp(other, move(full), amount);

                return true;
            }

            // Nothing nearby: the pair for the middle of a wide fade, across the whole image.
            if (!pairDone[colour])
            {
                foreach (var candidateFull in pureFull)
                    foreach (var candidateOther in pureOther)
                        Fit(pixel, colours[candidateFull], colours[candidateOther], ref best, ref full, ref other, ref amount);

                pairFound[colour] = amount > 0f;
                pairMixed[colour] = amount > 0f ? Color.Lerp(other, move(full), amount) : default;
                pairDone[colour] = true;
            }

            mixed = pairMixed[colour];

            return pairFound[colour];
        }

        // Tries every three pure colours of the image, at least one of them changed, and keeps the blend
        // of the three that lands closest to the pixel. Only the changed ones take the new colour. The
        // whole image is searched, since at the tip of a thin band the third colour can be a few pixels
        // away.
        internal bool TryUnmixThree (Color pixel, int[] pureFull, int[] pureOther, float limit, Func<Color, Color> move, out Color mixed, out float closest)
        {
            var best = limit;
            var found = false;

            mixed = default;
            pureAll.Clear();
            pureAll.AddRange(pureFull);
            pureAll.AddRange(pureOther);

            var fullCount = pureFull.Length;

            for (var a = 0; a < pureAll.Count; a++)
                for (var b = a + 1; b < pureAll.Count; b++)
                    for (var c = b + 1; c < pureAll.Count; c++)
                    {
                        var fullA = a < fullCount;
                        var fullB = b < fullCount;
                        var fullC = c < fullCount;
                        Color colourA = colours[pureAll[a]], colourB = colours[pureAll[b]], colourC = colours[pureAll[c]];

                        if (!fullA || !Blend(pixel, colourA, colourB, colourC, out var shareA, out var shareB, out var shareC, out var miss))
                            continue;

                        var changedShare = shareA + (fullB ? shareB : 0f) + (fullC ? shareC : 0f);

                        if (miss >= best || changedShare <= 0.02f || changedShare >= 0.98f)
                            continue;

                        best = miss;
                        found = true;
                        mixed = move(colourA) * shareA
                            + (fullB ? move(colourB) : colourB) * shareB
                            + (fullC ? move(colourC) : colourC) * shareC;
                    }

            closest = best;

            return found;
        }

        // The shares of three colours whose blend comes closest to the pixel, all inside the triangle.
        internal static bool Blend (Color pixel, Color a, Color b, Color c, out float shareA, out float shareB, out float shareC, out float miss)
        {
            var u = (Vector3) (Vector4) (a - c);
            var v = (Vector3) (Vector4) (b - c);
            var w = (Vector3) (Vector4) (pixel - c);
            var uu = Vector3.Dot(u, u);
            var uv = Vector3.Dot(u, v);
            var vv = Vector3.Dot(v, v);
            var determinant = uu * vv - uv * uv;

            shareA = shareB = shareC = miss = 0f;

            if (determinant < 0.000001f)
                return false;

            shareA = (Vector3.Dot(w, u) * vv - Vector3.Dot(w, v) * uv) / determinant;
            shareB = (Vector3.Dot(w, v) * uu - Vector3.Dot(w, u) * uv) / determinant;
            shareC = 1f - shareA - shareB;

            if (shareA < -0.02f || shareB < -0.02f || shareC < -0.02f)
                return false;

            miss = (w - u * shareA - v * shareB).magnitude;
            shareA = Mathf.Max(0f, shareA);
            shareB = Mathf.Max(0f, shareB);
            shareC = Mathf.Max(0f, shareC);

            var total = shareA + shareB + shareC;

            shareA /= total;
            shareB /= total;
            shareC /= total;

            return true;
        }

        internal static void Fit (Color pixel, Color candidateFull, Color candidateOther, ref float best, ref Color full, ref Color other, ref float amount)
        {
            var line = (Vector3) (Vector4) (candidateFull - candidateOther);
            var length = line.sqrMagnitude;

            if (length < 0.0001f)
                return;

            var offset = (Vector3) (Vector4) (pixel - candidateOther);
            var t = Vector3.Dot(offset, line) / length;

            if (t <= 0.02f || t >= 0.98f)
                return;

            var miss = (offset - line * t).magnitude;

            if (miss >= best)
                return;

            best = miss;
            full = candidateFull;
            other = candidateOther;
            amount = t;
        }

        // 1 inside the tolerance, fading to 0 across the feather.
        internal float Weight (Color pixel, float fromHue)
        {
            float distance;
            var hue = matchByHue;
            var limit = hue ? tolerance : rgbTolerance;
            var fade = hue ? feather : rgbFeather;

            if (hue)
            {
                Color.RGBToHSV(pixel, out var h, out var s, out _);

                if (s < minSaturation)
                    return 0f;

                distance = Mathf.Abs(Mathf.DeltaAngle(h * 360f, fromHue * 360f));
            }
            else
            {
                var r = pixel.r - fromColor.r;
                var g = pixel.g - fromColor.g;
                var b = pixel.b - fromColor.b;

                distance = Mathf.Sqrt(r * r + g * g + b * b) / Mathf.Sqrt(3f) * 100f;
            }

            if (distance <= limit)
                return 1f;

            return fade > 0f ? 1f - Mathf.Clamp01((distance - limit) / fade) : 0f;
        }

        internal bool UsesHue ()
        {
            Color.RGBToHSV(fromColor, out _, out var saturation, out var value);

            return saturation >= GreySaturation && value >= GreyValue;
        }

        internal static bool HasNoHue (float saturation, float value) => saturation < 0.01f || value < 0.01f;

        internal static bool Approximately (Color a, Color b)
            => Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

        // The modes in the File name menu, in the order of NameMode.
        private static readonly GUIContent[] NameModeItems =
        {
            new("Suffix"),
            new("Prefix"),
            new("Custom name")
        };

        private static GUIStyle nameMenuStyle;
        private static GUIStyle hintStyle;
        private const string FileNameControl = "Nefisto.Recolor.FileName";
        private bool focusFileName;

        // The image the File name settings were loaded for. Another image loads its own.
        [NonSerialized]
        private string fileNameGuid;

        // The File name settings were set from elsewhere, such as a bug report: they stay for this image,
        // instead of being replaced by the ones kept for it.
        internal void KeepFileName () => fileNameGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));

        // Each image keeps its own File name, so another image doesn't take this one's: it gets what was
        // set for it before, or the default.
        private void LoadFileName (string guid)
        {
            fileNameGuid = guid;

            var entry = string.IsNullOrEmpty(guid) ? null : RecolorFileNames.Find(guid);

            nameMode = entry?.mode ?? NameMode.Suffix;
            nameStart = entry?.nameStart ?? "";
            nameEnd = entry?.nameEnd ?? Suffix;
            customName = entry?.customName ?? "";
        }

        // A File name row: the label, then the small menu button that picks the mode. Returns the room left
        // for the fields. Picking a mode with text sets focus, to put the cursor in its field.
        internal static Rect FileNameRow (GUIContent label, ref NameMode mode, ref bool focus)
            => NameMenu(EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), label), ref mode, ref focus);

        // The small menu button at the start of rect, and the room left after it.
        internal static Rect NameMenu (Rect rect, ref NameMode mode, ref bool focus)
        {
            nameMenuStyle ??= new GUIStyle(GUI.skin.GetStyle("PaneOptions")) { imagePosition = ImagePosition.ImageOnly };

            var button = new Rect(rect.x, rect.y + nameMenuStyle.margin.top, nameMenuStyle.fixedWidth + nameMenuStyle.margin.right, rect.height);
            var picked = (NameMode) EditorGUI.Popup(button, GUIContent.none, (int) mode, NameModeItems, nameMenuStyle);

            if (picked != mode)
            {
                mode = picked;
                focus = true;
            }

            return new Rect(button.xMax + 2f, rect.y, rect.xMax - button.xMax - 2f, rect.height);
        }

        // The label, a small menu button to pick the mode, and the text of that mode. An existing file is
        // never replaced: Unity adds a number to the name instead, and a line under the field says so.
        private void DrawFileName ()
        {
            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));

            if (guid != fileNameGuid)
                LoadFileName(guid);

            EditorGUI.BeginChangeCheck();

            var field = FileNameRow(new GUIContent("File name",
                "How Save as new image names the new file. Pick how with the menu button.\n\n" +
                "Suffix: the image's name, then your text (_recolored to start with).\n" +
                "Prefix: your text, then _recolored.\n" +
                "Custom name: your text only."), ref nameMode, ref focusFileName);

            switch (nameMode)
            {
                case NameMode.Prefix:
                {
                    var (text, kept) = SplitField(field, Suffix, false);
                    nameStart = TextWithHint(text, nameStart, "e.g. villain", FileNameControl, ref focusFileName, this);
                    KeptField(kept, Suffix);
                    break;
                }
                case NameMode.CustomName:
                    customName = TextWithHint(field, customName, "Name, e.g. hero_blue", FileNameControl, ref focusFileName, this);
                    break;
                default:
                {
                    var (text, kept) = SplitField(field, SourceName(), true);
                    KeptField(kept, SourceName());
                    nameEnd = TextWithHint(text, nameEnd, "e.g. _blue", FileNameControl, ref focusFileName, this);
                    break;
                }
            }

            if (EditorGUI.EndChangeCheck())
                RecolorFileNames.Set(guid, nameMode, nameStart, nameEnd, customName);

            // Only when the name is taken: Unity adds a number to it, so the image is saved under another name.
            var wanted = NewName() + ".png";
            var saved = Path.GetFileName(NewPath());

            if (saved != wanted)
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUIUtility.labelWidth + 2f);
                    GUILayout.Label($"{wanted} already exists: it saves as {saved}", EditorStyles.miniLabel);
                }
        }

        // For a suffix or a prefix, the field splits in two: the part that stays, which can't be edited, and
        // the user's text beside it. The part that stays takes the room it needs, up to half the field.
        internal static (Rect text, Rect kept) SplitField (Rect field, string kept, bool keptFirst)
        {
            const float gap = 2f;
            var needed = EditorStyles.textField.CalcSize(new GUIContent(kept)).x;
            var keptWidth = Mathf.Clamp(needed, 40f, (field.width - gap) * 0.5f);
            var textWidth = field.width - keptWidth - gap;

            return keptFirst
                ? (new Rect(field.x + keptWidth + gap, field.y, textWidth, field.height), new Rect(field.x, field.y, keptWidth, field.height))
                : (new Rect(field.x, field.y, textWidth, field.height), new Rect(field.x + textWidth + gap, field.y, keptWidth, field.height));
        }

        internal static void KeptField (Rect rect, string kept)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUI.TextField(rect, kept);
        }

        internal string SourceName () => Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(source));

        // A text field that shows a grey hint while it's empty. With focus set, it takes the cursor.
        internal static string TextWithHint (Rect rect, string text, string hint, string control, ref bool focus, EditorWindow owner)
        {
            GUI.SetNextControlName(control);
            text = EditorGUI.TextField(rect, text);

            if (focus)
            {
                focus = false;
                EditorGUI.FocusTextInControl(control);
                owner.Repaint();
            }

            if (string.IsNullOrEmpty(text) && Event.current.type == EventType.Repaint)
            {
                hintStyle ??= new GUIStyle(EditorStyles.label) { normal = { textColor = Color.gray } };
                hintStyle.Draw(new Rect(rect.x + 2f, rect.y, rect.width - 2f, rect.height), hint, false, false, false, false);
            }

            return text;
        }

        // The new image's name, without the extension. Characters a file name can't have are left out, and
        // an empty name falls back to the default.
        internal string NewName ()
        {
            var name = SourceName();
            var fallback = name + Suffix;

            var text = nameMode switch
            {
                NameMode.Prefix => Clean(nameStart),
                NameMode.Suffix => Clean(nameEnd),
                NameMode.CustomName => Clean(customName),
                _ => ""
            };

            if (string.IsNullOrWhiteSpace(text))
                return fallback;

            return nameMode switch
            {
                NameMode.Prefix => text.Trim() + Suffix,
                NameMode.Suffix => name + text.TrimEnd(),
                _ => text.Trim()
            };
        }

        internal static string Clean (string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            var invalid = Path.GetInvalidFileNameChars();

            return new string(text.Where(c => Array.IndexOf(invalid, c) < 0 && c != '/' && c != '\\').ToArray());
        }

        internal string NewPath ()
        {
            var folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(source))?.Replace('\\', '/');

            return AssetDatabase.GenerateUniqueAssetPath($"{folder}/{NewName()}.png");
        }

        // A new image takes the original's import settings, so a sliced sprite stays sliced.
        internal void Save (string path)
        {
            var sourcePath = AssetDatabase.GetAssetPath(source);
            var isNew = path != sourcePath;

            if (!isNew && Path.GetExtension(path).ToLowerInvariant() != ".png")
            {
                EditorUtility.DisplayDialog("Recolor", "Only a PNG can be overwritten. Save it as a new image instead.", "OK");

                return;
            }

            File.WriteAllBytes(path, result.EncodeToPNG());
            AssetDatabase.ImportAsset(path);

            if (isNew)
                CopyImportSettings(sourcePath, path);

            var saved = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

            EditorGUIUtility.PingObject(saved);
            Debug.Log($"[Recolor] Saved {changedPixels} changed pixels to {path}.");

            // The file now holds the result, so it is the new start.
            if (!isNew)
            {
                foreach (var addon in addons)
                    addon.OnNewImage();

                LoadOriginal();
            }
        }

        // A sprite rect of the source in the pixels of the image being edited.
        internal Rect Shifted (Rect rect) => cropped ? new Rect(rect.x - cropOffset.x, rect.y - cropOffset.y, rect.width, rect.height) : rect;

        internal void CopyImportSettings (string fromPath, string toPath)
        {
            if (AssetImporter.GetAtPath(toPath) is not TextureImporter to)
                return;

            if (AssetImporter.GetAtPath(fromPath) is not TextureImporter from)
            {
                SliceLike(fromPath, to);

                return;
            }

            var settings = new TextureImporterSettings();

            from.ReadTextureSettings(settings);
            to.SetTextureSettings(settings);
            to.textureCompression = from.textureCompression;
            to.maxTextureSize = from.maxTextureSize;
            to.spritePixelsPerUnit = from.spritePixelsPerUnit;
            to.spriteBorder = from.spriteBorder;
    #pragma warning disable CS0618
            to.spritesheet = from.spritesheet.Select(sprite =>
            {
                sprite.name = RenamedSprite(sprite.name, fromPath, toPath);

                return sprite;
            }).ToArray();
    #pragma warning restore CS0618
            // Setting the sprites alone doesn't mark the importer as changed, so the reimport would skip them.
            EditorUtility.SetDirty(to);
            to.SaveAndReimport();
        }

        // A source that is not a plain texture, such as an Aseprite or PSD file, has its own importer. The
        // new PNG gets its sprites instead: the same rects, names, pivots and pixels per unit.
        internal void SliceLike (string fromPath, TextureImporter to)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(fromPath);
            var sprites = assets.OfType<Sprite>().Where(sprite => sprite.texture == source).ToArray();
            var texture = assets.OfType<Texture2D>().FirstOrDefault();

            to.textureType = TextureImporterType.Sprite;
            to.alphaIsTransparency = true;
            to.mipmapEnabled = false;
            to.textureCompression = TextureImporterCompression.Uncompressed;

            if (texture != null)
                to.filterMode = texture.filterMode;

            if (sprites.Length == 0)
            {
                to.SaveAndReimport();

                return;
            }

            to.spritePixelsPerUnit = sprites[0].pixelsPerUnit;

            // A single sprite that fills the whole cut image is the image itself.
            if (sprites.Length == 1 && Shifted(sprites[0].rect) == new Rect(0, 0, original.width, original.height))
            {
                var settings = new TextureImporterSettings();

                to.spriteImportMode = SpriteImportMode.Single;
                to.ReadTextureSettings(settings);
                settings.spriteAlignment = (int) SpriteAlignment.Custom;
                settings.spritePivot = PivotOf(sprites[0]);
                to.SetTextureSettings(settings);
                to.SaveAndReimport();

                return;
            }

            to.spriteImportMode = SpriteImportMode.Multiple;
    #pragma warning disable CS0618
            to.spritesheet = sprites.Select(sprite => new SpriteMetaData
            {
                name = RenamedSprite(sprite.name, fromPath, to.assetPath),
                rect = Shifted(sprite.rect),
                alignment = (int) SpriteAlignment.Custom,
                pivot = PivotOf(sprite),
                border = sprite.border
            }).ToArray();
    #pragma warning restore CS0618
            // Setting the sprites alone doesn't mark the importer as changed, so the reimport would skip them.
            EditorUtility.SetDirty(to);
            to.SaveAndReimport();
        }

        // A sprite named after the original file takes the new file's name: melon_walk_1 saved in
        // melon_walk_recolored.png becomes melon_walk_recolored_1. Other names, such as head, stay.
        internal static string RenamedSprite (string name, string fromPath, string toPath)
        {
            var from = Path.GetFileNameWithoutExtension(fromPath);
            var to = Path.GetFileNameWithoutExtension(toPath);

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(from) || !name.StartsWith(from, StringComparison.Ordinal))
                return name;

            // melon_walker_1 is its own name, not melon_walk followed by more.
            var rest = name.Substring(from.Length);

            return rest.Length == 0 || !char.IsLetter(rest[0]) ? to + rest : name;
        }

        // The pivot of a sprite from 0 to 1 across its rect, as the importer takes it.
        internal static Vector2 PivotOf (Sprite sprite)
            => new(sprite.pivot.x / Mathf.Max(1f, sprite.rect.width), sprite.pivot.y / Mathf.Max(1f, sprite.rect.height));

        internal void ReleasePreviews ()
        {
            if (original != null)
                DestroyImmediate(original);

            if (result != null)
                DestroyImmediate(result);

            if (regionShade != null)
                DestroyImmediate(regionShade);

            foreach (var addon in addons)
                addon.Release();

            original = null;
            result = null;
            regionShade = null;
        }

        // Every add-on in the project, one of each: new ones are made, gone ones dropped, all in order.
        internal void SetUpAddons ()
        {
            addons.RemoveAll(addon => addon == null);

            foreach (var type in TypeCache.GetTypesDerivedFrom<RecolorAddon>())
                if (!type.IsAbstract && addons.All(addon => addon.GetType() != type))
                    addons.Add((RecolorAddon) Activator.CreateInstance(type));

            addons.Sort((a, b) => a.Order.CompareTo(b.Order));

            foreach (var addon in addons)
            {
                addon.window = this;
                addon.OnEnable();
            }
        }

        internal T Addon<T> () where T : RecolorAddon => addons.OfType<T>().FirstOrDefault();

        internal IReadOnlyList<RecolorAddon> Addons => addons;

        // The window's ⋮ menu: a folder with the settings, system info and images for a bug report.
        public void AddItemsToMenu (GenericMenu menu) =>
            menu.AddItem(new GUIContent("Create bug report..."), false, () => RecolorDebugInfo.CreateReport(this));

        // Repaints when an add-on asks, such as an animation moving to its next frame.
        internal void UpdateAddons ()
        {
            var repaint = false;

            foreach (var addon in addons)
                repaint |= addon.Update();

            if (repaint)
                Repaint();
        }

        // Tells the add-ons the selection changed: started over, or added to and taken from.
        internal void SelectionChanged (bool startedOver)
        {
            foreach (var addon in addons)
                addon.OnSelectionChanged(startedOver);
        }

        // Shows other pixels in the original image, of the same size, such as a step baked in earlier.
        internal void ShowImage (byte[] png)
        {
            if (original == null || png == null)
                return;

            original.LoadImage(png);
            original.filterMode = FilterMode.Point;
            pixelCache = null;
            dirty = true;
            Repaint();
        }

        // When the open image is imported again, as when its Aseprite or PSD file is saved, the window
        // reads it again. The selection, the settings and the save points stay.
        internal void OnImported (string[] paths)
        {
            if (source == null || original == null)
                return;

            var path = AssetDatabase.GetAssetPath(source);

            if (!paths.Contains(path))
                return;

            // On the next editor update, after the import is done, so the texture holds the new pixels. The
            // update runs even while Unity is in the background, unlike a delay call.
            EditorApplication.update -= ReloadAfterImport;
            EditorApplication.update += ReloadAfterImport;
        }

        internal void ReloadAfterImport ()
        {
            EditorApplication.update -= ReloadAfterImport;

            if (this == null || source == null)
                return;

            LoadOriginal();
            Repaint();
        }
    }

    // Tells the open Recolor windows which assets were imported.
    internal class RecolorImportWatcher : AssetPostprocessor
    {
        internal static void OnPostprocessAllAssets (string[] imported, string[] deleted, string[] movedTo, string[] movedFrom)
        {
            if (imported.Length == 0)
                return;

            foreach (var window in Resources.FindObjectsOfTypeAll<RecolorWindow>())
                window.OnImported(imported);
        }
    }
}
