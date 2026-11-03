using System;
using UnityEditor;
using UnityEngine;

namespace Nefisto.Recolor
{
    // Shows the version of Recolor, its edition (Free, or Pro when Recolor Pro is in the project), a link
    // to buy Pro, and how to report a bug: create a bug report and email it.
    internal class RecolorHelpWindow : EditorWindow
    {
        // The Asset Store page of Recolor Pro. Empty until it is published; the buy button waits for it.
        internal const string ProUrl = "";

        internal const string ContactEmail = "nefisto.developer@gmail.com";

        private const string ProFeatures = "Pro adds Apply to all frames, Variants, Transition sheets, Save points and Saved selections.";

        private string liteVersion;
        private string proVersion;
        private bool proInstalled;

        [MenuItem("Tools/Recolor/Help", priority = 100)]
        public static void Open ()
        {
            var window = GetWindow<RecolorHelpWindow>(true, "Recolor Help");

            window.minSize = window.maxSize = new Vector2(340f, 190f);
        }

        private void OnEnable ()
        {
            liteVersion = RecolorDebugInfo.LiteVersion;
            proInstalled = RecolorDebugInfo.ProInstalled;
            proVersion = proInstalled ? RecolorDebugInfo.ProVersion : null;
        }

        private string Edition => proInstalled ? "Pro" : "Free";

        private void OnGUI ()
        {
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
