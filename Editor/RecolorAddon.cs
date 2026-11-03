using System;
using UnityEngine;

namespace Nefisto.Recolor
{
    // A part added to the Recolor window, such as Animate frames or the Pro features. The window finds
    // every add-on in the project, keeps one of each with its own state, and calls it at set points.
    // It is internal: only this assembly and those named in AssemblyInfo can add one.
    [Serializable]
    internal abstract class RecolorAddon
    {
        [NonSerialized]
        internal RecolorWindow window;

        // Lower comes first, where several add-ons draw in the same place.
        internal virtual int Order => 0;

        internal virtual void OnEnable () { }

        internal virtual void OnDisable () { }

        // Another image was picked, or the image was replaced by its result: anything kept for it goes.
        internal virtual void OnNewImage () { }

        // The image was read again from its file, such as after a script reload or a new import.
        internal virtual void OnImageLoaded () { }

        internal virtual void OnUndoRedo () { }

        // The selection started over, or was added to or taken from.
        internal virtual void OnSelectionChanged (bool startedOver) { }

        // Right after the From and To colours.
        internal virtual void AfterColours () { }

        // Right after the matching settings.
        internal virtual void DrawAfterMatching () { }

        // At the end of the clicked region controls, while something is selected.
        internal virtual void DrawRegionControls () { }

        // After the clicked region controls.
        internal virtual void DrawAfterRegion () { }

        // After the save buttons, at the end of the settings.
        internal virtual void DrawSections () { }

        // At the bottom of the settings column, outside its scroll, just above the shortcuts. Only while
        // an image is open.
        internal virtual void DrawFooter () { }

        // A colour for the result to go to instead of the To colour.
        internal virtual bool TargetColour (out Color colour)
        {
            colour = default;

            return false;
        }

        // Just before the result is worked out again.
        internal virtual void BeforeRecolor () { }

        // The height of a row of controls in the header of the original image, under the area controls,
        // or 0 for none.
        internal virtual float OriginalHeaderHeight => 0f;

        internal virtual void DrawOriginalHeader (Rect row) { }

        // The height of a row of controls under the title of the result preview, or 0 for none.
        internal virtual float ResultHeaderHeight => 0f;

        // How wide the row's controls are, for the band behind them. 0 takes the whole row.
        internal virtual float ResultHeaderWidth => 0f;

        internal virtual void DrawResultHeader (Rect row) { }

        // Draws something in place of the result, inside its frame. True when it did.
        internal virtual bool DrawResult (Rect area) => false;

        // A title for the original image preview instead of "Original image".
        internal virtual string OriginalTitle () => null;

        // Called on every editor update. True to repaint the window.
        internal virtual bool Update () => false;

        // The add-on's state for a bug report, as JSON. Its serialized fields, unless it holds something too
        // big to paste, such as images.
        internal virtual string DebugState () => JsonUtility.ToJson(this, true);

        // Sets the add-on back from its state in a bug report.
        internal virtual void LoadDebugState (string json) => JsonUtility.FromJsonOverwrite(json, this);

        // The window lets go of its textures; the add-on lets go of its own.
        internal virtual void Release () { }
    }
}
