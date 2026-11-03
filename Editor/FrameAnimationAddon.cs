using System;
using UnityEditor;
using UnityEngine;

namespace Nefisto.Recolor
{
    // Plays the frames of a sliced image one after another beside the result.
    [Serializable]
    internal class FrameAnimationAddon : RecolorAddon
    {
        [SerializeField]
        private bool animateFrames;

        [SerializeField]
        private float previewFps = 8f;

        // Plays back to the first frame after the last one, instead of jumping back to the start.
        [SerializeField]
        private bool pingPong;

        [NonSerialized]
        private int shownFrame;

        internal override int Order => 10;

        private bool PlaysFrames () => animateFrames && window.frames.Count >= 2;

        private int FrameNow () => RecolorWindow.PlayIndex(window.frames.Count, previewFps, pingPong);

        // A row under the title of the preview: the toggle, and while it plays, its speed and ping-pong.
        // Greyed out while the image is not sliced into frames. It only changes what the preview shows.
        internal override float ResultHeaderHeight => EditorGUIUtility.singleLineHeight;

        // The toggle alone, or with the speed and ping-pong while it plays.
        internal override float ResultHeaderWidth => animateFrames && window.frames.Count >= 2 ? 110f + 8f + 62f + 6f + 70f + 8f + 80f : 110f;

        internal override void DrawResultHeader (Rect row)
        {
            var sliced = window.frames.Count >= 2;
            var toggle = new Rect(row.x, row.y, 110f, row.height);

            // Shown unticked while disabled; the choice is kept for the next sliced image.
            using (new EditorGUI.DisabledScope(!sliced))
            {
                var ticked = GUI.Toggle(toggle, sliced && animateFrames, new GUIContent("Animate frames", sliced
                    ? "Plays the frames one after another beside the result, in the order of their names. Every frame sits by its pivot, as in the game."
                    : "Slice the image into sprites (Sprite Mode: Multiple) to play its frames."));

                if (sliced)
                    animateFrames = ticked;
            }

            if (!sliced || !animateFrames)
                return;

            // A short slider of whole frames a second after the toggle, then ping-pong.
            // Dragging the FPS label changes the speed too, as on any number field.
            // Unity's own slider hides its bar when it is this short, so the number and the bar are drawn
            // apart: the label and its number first, then a short bar.
            const float pingPongWidth = 80f;

            var field = new Rect(toggle.xMax + 8f, row.y, 62f, row.height);
            var bar = new Rect(field.xMax + 6f, row.y, 70f, row.height);
            var labelWidth = EditorGUIUtility.labelWidth;
            var fps = Mathf.RoundToInt(previewFps);

            EditorGUIUtility.labelWidth = 28f;
            fps = EditorGUI.IntField(field, new GUIContent("FPS", "How many frames a second the preview plays. Drag the label, the bar or type a number."), fps);
            EditorGUIUtility.labelWidth = labelWidth;

            fps = Mathf.RoundToInt(GUI.HorizontalSlider(bar, fps, 1f, 30f));
            previewFps = Mathf.Clamp(fps, 1, 30);

            pingPong = GUI.Toggle(new Rect(bar.xMax + 8f, row.y, pingPongWidth, row.height), pingPong,
                new GUIContent("Ping-pong", "Plays back to the first frame after reaching the last one, instead of jumping back to the start."));
        }

        // Side by side: the whole result on the left, its frames playing on the right.
        internal override bool DrawResult (Rect area)
        {
            if (!PlaysFrames())
                return false;

            var width = (area.width - RecolorWindow.PreviewPadding) * 0.5f;
            var right = new Rect(area.x + width + RecolorWindow.PreviewPadding, area.y, width, area.height);

            RecolorWindow.DrawLabelledImage(new Rect(area.x, area.y, width, area.height), window.result, "Sheet");
            GUI.Label(new Rect(right.x + 2f, right.y, right.width - 4f, RecolorWindow.PreviewTitleHeight), "Animated", EditorStyles.miniLabel);
            DrawPlayingFrame(new Rect(right.x, right.y + RecolorWindow.PreviewTitleHeight, right.width, right.height - RecolorWindow.PreviewTitleHeight));

            return true;
        }

        // Repaints only when the animation moves to another frame.
        internal override bool Update ()
        {
            if (!PlaysFrames())
                return false;

            var now = FrameNow();

            if (now == shownFrame)
                return false;

            shownFrame = now;

            return true;
        }

        // The frame of the result shown now, on one canvas that fits every frame. Each frame sits by its
        // pivot, as in the game, so the canvas stays still and the drawing doesn't slide.
        private void DrawPlayingFrame (Rect area)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            var frames = window.frames;
            var pivots = window.framePivots;
            var result = window.result;

            // How far the frames reach from their pivots on each side.
            float left = 0f, right = 0f, bottom = 0f, top = 0f;

            for (var i = 0; i < frames.Count; i++)
            {
                left = Mathf.Max(left, pivots[i].x);
                right = Mathf.Max(right, frames[i].width - pivots[i].x);
                bottom = Mathf.Max(bottom, pivots[i].y);
                top = Mathf.Max(top, frames[i].height - pivots[i].y);
            }

            var canvasSize = new Vector2(left + right, bottom + top);
            var scale = Mathf.Min(area.width / canvasSize.x, area.height / canvasSize.y);
            var canvas = new Rect(area.center - canvasSize * scale * 0.5f, canvasSize * scale);

            var index = FrameNow();
            var frame = frames[index];
            var pivot = pivots[index];

            // The pivot point on the canvas; GUI rows go down, so the frame's top is above it by its height less the pivot.
            var anchor = new Vector2(canvas.x + left * scale, canvas.y + top * scale);
            var rect = new Rect(anchor.x - pivot.x * scale, anchor.y - (frame.height - pivot.y) * scale, frame.width * scale, frame.height * scale);
            var uv = new Rect((float) frame.x / result.width, (float) frame.y / result.height, (float) frame.width / result.width, (float) frame.height / result.height);

            GUI.DrawTextureWithTexCoords(canvas, RecolorWindow.Checker(), new Rect(0f, 0f, canvas.width / 16f, canvas.height / 16f), false);
            GUI.DrawTextureWithTexCoords(rect, result, uv, true);
        }
    }
}
