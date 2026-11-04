using System;
using UnityEditor;
using UnityEngine;

namespace Nefisto.Recolor
{
    // Shows the version of Recolor, its edition (Free, or Pro when Recolor Pro is in the project), a link
    // to buy Pro, a link to review the edition in use, and how to report a bug: create a bug report and
    // email it.
    internal class RecolorHelpWindow : EditorWindow
    {
        // The Asset Store page of Recolor Pro. Empty until it is published; the buy button waits for it.
        internal const string ProUrl = "";

        // The Asset Store page of the free Recolor. Empty until it is published; the review link waits for it.
        internal const string LiteUrl = "";

        internal const string ContactEmail = "nefisto.developer@gmail.com";

        private const string ProFeatures = "Pro adds Apply to all frames, Variants, Transition sheets, Save points and Saved selections.";

        // The logo's height in the window; its width follows the image.
        private const float LogoHeight = 36f;

        private string liteVersion;
        private string proVersion;
        private bool proInstalled;
        private Texture2D logo;

        [MenuItem("Tools/Recolor/Help", priority = 100)]
        public static void Open ()
        {
            var window = GetWindow<RecolorHelpWindow>(true, "Recolor Help");

            window.minSize = window.maxSize = new Vector2(340f, 210f);
        }

        private void OnEnable ()
        {
            liteVersion = RecolorDebugInfo.LiteVersion;
            proInstalled = RecolorDebugInfo.ProInstalled;
            proVersion = proInstalled ? RecolorDebugInfo.ProVersion : null;
            logo = LoadLogo();
        }

        // RecolorLogo.png sits next to this script, wherever the package is (Recolor, or inside Recolor Pro).
        private Texture2D LoadLogo ()
        {
            var script = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
            if (string.IsNullOrEmpty(script))
                return null;

            return AssetDatabase.LoadAssetAtPath<Texture2D>(System.IO.Path.GetDirectoryName(script).Replace('\\', '/') + "/RecolorLogo.png");
        }

        private string Edition => proInstalled ? "Pro" : "Free";

        private void OnGUI ()
        {
            // Short labels, so the values stay clear of the logo in the top right corner.
            EditorGUIUtility.labelWidth = 70f;

            if (logo != null)
            {
                var width = LogoHeight * logo.width / logo.height;
                GUI.DrawTexture(new Rect(position.width - width - 10f, 8f, width, LogoHeight), logo, ScaleMode.ScaleToFit);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Recolor", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Version", liteVersion ?? "Unknown");

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Edition");

                if (proInstalled)
                    EditorGUILayout.LabelField(proVersion != null ? $"Pro ({proVersion})" : "Pro");
                else
                {
                    GUILayout.Label("Free", GUILayout.ExpandWidth(false));
                    GUILayout.FlexibleSpace();

                    var tooltip = string.IsNullOrEmpty(ProUrl)
                        ? $"Coming soon to the Unity Asset Store. {ProFeatures}"
                        : $"Opens Recolor Pro on the Unity Asset Store. {ProFeatures}";

                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(ProUrl)))
                        if (GUILayout.Button(new GUIContent("Buy Pro", tooltip), GUILayout.Width(80f)))
                            Application.OpenURL(ProUrl);
                }
            }

            DrawReview();

            EditorGUILayout.Space(12f);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Found a bug or have a feature suggestion?", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));

                if (EditorGUILayout.LinkButton(new GUIContent("Email me", ContactEmail)))
                    Application.OpenURL(MailTo());

                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox("For a bug, create a bug report and attach debug-info.json to the email, with the image you used and a short description of what happened.", MessageType.Info);

            var create = new GUIContent("Create bug report...", "Saves your Recolor settings, the versions, your Unity and system info and the images in a new folder, with a READ-ME on what to send.");

            if (GUILayout.Button(create))
                RecolorDebugInfo.CreateReport();
        }

        // A link to the reviews of the edition in use, on its Asset Store page. Off until the page exists.
        private void DrawReview ()
        {
            var page = proInstalled ? ProUrl : LiteUrl;
            var hasPage = !string.IsNullOrEmpty(page);

            EditorGUILayout.Space(6f);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Enjoying Recolor?", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));

                using (new EditorGUI.DisabledScope(!hasPage))
                    if (EditorGUILayout.LinkButton(new GUIContent("Leave a review", hasPage ? "Opens the reviews on the Unity Asset Store." : "Coming soon to the Unity Asset Store.")))
                        Application.OpenURL(page + "#reviews");

                GUILayout.FlexibleSpace();
            }
        }

        // A new email with the edition and version in the subject, and what to send in the body.
        private string MailTo ()
        {
            var version = proInstalled ? proVersion : liteVersion;
            var subject = $"Recolor {Edition} {version ?? ""}".TrimEnd();
            const string body = "What happened, and what did you expect?\n\n\n" +
                                "For a bug, please attach the files from Tools > Recolor > Help > Create bug report.\n";

            return $"mailto:{ContactEmail}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
        }
    }
}
