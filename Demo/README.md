# Recolor Demo

Sample images to try Recolor on, and a scene that shows them.

- **Sprites/melon.png**: the watermelon, one sprite, with its pink headband and backpack.
- **Sprites/melon_walk.png**: the watermelon's 8-frame walk cycle, sliced into sprites (**Sprite Mode: Multiple**).
- **Recolor Demo.unity**: the watermelon standing, and walking.

Open the window from **Tools > Recolor > Open** and drag an image from this folder into it. The images
you save go next to the originals.

## Recolor a colour, keeping its shading

1. Open **melon.png**.
2. Click the pink of the headband in the original image to pick the **From** colour.
3. Pick a **To** colour, such as blue: every shade of pink becomes the same shade of blue, on the
   headband and on the backpack, and the outline stays as it is.
4. Click **Save as new image**: `melon_recolored.png` is saved beside it.

## Recolor only some parts

1. Set **Area** to **Clicked region**.
2. Click the backpack: only it is selected. Shift+click the headband to add it, Ctrl+click (Cmd on
   Mac) to take a part out again.
3. Pick a **To** colour: only the selected parts change.

## Sprite sheets

1. Open **melon_walk.png**.
2. Pick the green of the rind as the **From** colour and a **To** colour.
3. Turn on **Animate frames**, above the result, to watch the recoloured watermelon walk.
4. Click **Save as new image**: the saved sheet keeps the same eight sprites, renamed after the new file.

With **Clicked region**, a part clicked on one frame never spills into the next one.

The full documentation is in the Recolor README.
