using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Nefisto.Recolor
{
    // Tools > Recolor > Open demo: opens the demo scene and selects the demo's README, which walks through
    // the features on the watermelon. Recolor Pro's demo comes first when Pro is in the project, since its
    // README also covers the Pro features.
    internal static class RecolorDemo
    {
        private static readonly string[] Folders =
        {
            "Packages/com.nefisto.recolor.pro/Demo",
            "Packages/com.nefisto.recolor/Demo"
        };

        [MenuItem("Tools/Recolor/Open demo", priority = 1)]
        private static void Open ()
        {
            foreach (var folder in Folders)
            {
                var scene = folder + "/Recolor Demo.unity";

                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) == null)
                    continue;

                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;

                EditorSceneManager.OpenScene(scene);

                var readme = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/README.md");

                if (readme != null)
                {
                    Selection.activeObject = readme;
                    EditorGUIUtility.PingObject(readme);
                }

                return;
            }

            EditorUtility.DisplayDialog("Recolor", "The demo isn't in this project. Import Recolor again to bring it back.", "OK");
        }
    }
}
