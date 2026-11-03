using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Nefisto.Recolor
{
    // Without Recolor Pro, shows its Apply to all frames button greyed out where Pro puts it, so a
    // sprite sheet shows what Pro adds. Gone once Pro is in the project.
    [Serializable]
    internal class ApplyToAllFramesTeaser : RecolorAddon
    {
        private const string ProAddon = "Nefisto.Recolor.ApplyToAllFramesAddon";

        [NonSerialized]
        private bool proInstalled;

        internal override int Order => 10;

        internal override void OnEnable ()
            => proInstalled = TypeCache.GetTypesDerivedFrom<RecolorAddon>().Any(type => type.FullName == ProAddon);

        // The same row Pro keeps in any area of a sliced image, so the image doesn't move.
        internal override float OriginalHeaderHeight => !proInstalled && window.frames.Count >= 2 ? EditorGUIUtility.singleLineHeight : 0f;

        internal override void DrawOriginalHeader (Rect row)
        {
            if (window.area != RecolorWindow.AreaMode.ClickedRegion)
                return;

            using (new EditorGUI.DisabledScope(true))
                GUI.Button(row, new GUIContent("Apply to all frames (Pro)",
                    "Recolor Pro, on the Unity Asset Store, finds the part selected on one frame on every other frame of the sprite sheet."), EditorStyles.miniButton);
        }
    }
}
