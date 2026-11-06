# Recolor

Unity editor tool that swaps one colour of an image for another while keeping its shading, and
splits the anti-aliased pixels along edges back into the colours they mix, so only the part of the
old colour changes and no fringe of it is left behind.

- Recolour the whole image, or only the parts you click.
- Works with PNG, JPG, Aseprite, PSD and any image Unity imports.
- The shading always stays: every shade keeps its distance from the others.
- Sprite sheets: a clicked part never spills into the next frame, and **Animate frames** plays the
  recoloured frames beside the result, each by its pivot.

## Documentation

- [Install](#install)
- [Quick start](#quick-start)
- [Picking the colours](#picking-the-colours)
- [Matching settings](#matching-settings)
- [Clean edges](#clean-edges)
- [Keep shading](#keep-shading)
- [Recolouring only some parts](#recolouring-only-some-parts)
- [Sprite sheets](#sprite-sheets)
- [Saving](#saving)
- [Image files and import settings](#image-files-and-import-settings)
- [Help and bug reports](#help-and-bug-reports)
- [Recolor Pro](#recolor-pro)
- [Support](#support)

## Install

Requires **Unity 2022.3** or newer. Recolor is editor-only: nothing is added to your build.

- **Asset Store:** in **Window > Package Manager**, pick **My Assets**, find Recolor and click
  **Download**, then **Import**. It's added under **Packages**, beside Unity's packages.
- **Git URL:** in **Window > Package Manager**, **+ > Add package from git URL**, with
  `https://github.com/Nefisto/Recolor-LITE.git`.

A demo is included: in the Package Manager, select Recolor, open **Samples** and import **Demo**.

## Quick start

1. Open **Tools > Recolor > Open**.
2. Drag an image into the **Image** field, or select it in the Project window and click
   **Use selection**.
3. Click the colour you want to change on the original image. It becomes the **From** colour.
4. Pick the new colour in **To**, with the colour field or the wheel. The result updates as you change it.
5. Click **Save as new image**.

## Picking the colours

- **From** is the colour to replace. Click it on the original image, or set it in the field. While
  the cursor is over the image, a zoom shows the pixels around it; hold **Alt** to see only the colour.
- **To** is where that colour goes. Every shade of the From colour follows: lighter and darker shades
  land as the same lighter and darker shades of the To colour.

The shortcuts for the original image are listed under it in the window.

## Matching settings

Which pixels count as the From colour depends on whether it has a hue.

**A colour with a hue** (red, green, blue...) is matched by hue, so all its shades are taken, light
or dark:

- **Tolerance (degrees):** how far around the hue wheel still counts.
- **Feather (degrees):** past the tolerance, how far the change fades out.
- **Min saturation:** greys have no hue, so the ones below this stay as they are.

**A grey, white or black** has no hue, so it's matched by how close the colours are in RGB:

- **Tolerance:** 0 to 100, where 100 is black to white.
- **Feather:** past the tolerance, how far the change fades out.

Each way keeps its own numbers, so switching between a colour and a grey doesn't lose your settings.

## Clean edges

With anti-aliasing, the pixels between a red cape and its black outline are dark reds: part red,
part black. Changed as they are, they would leave a reddish fringe around a blue cape.

**Clean edges** finds how much of each colour such a pixel holds, say 60% red and 40% black, and
changes only the red part: it becomes 60% blue and 40% black, as smooth as the original edge. It
works the same on soft fades inside the image.

- **Mix tolerance:** how far off the line between two colours a pixel can be and still count as
  their mix. Raise it when loose pixels of the old colour are left in a fade or along an edge; lower
  it when colours that aren't a mix get caught.

Pixel art with hard edges has few such pixels, so it changes little there.

## Keep shading

When the To colour would push the lightest or darkest shades past the limit, they would merge into
one. **Keep shading** moves every shade only as far as fits, so none merge, and **From lands on**
shows where the From colour went.

The option shows only when it's holding the colour back, or while it's off:

- **On:** every shade keeps its distance; the From colour lands near the To colour. A slightly
  lighter or darker To colour moves it closer.
- **Off:** the From colour lands on the To colour exactly, and the shades past the limit are cut
  there, so some of them merge.

## Recolouring only some parts

Set **Area** to **Clicked region** to change only the parts you click, so the shirt changes and the
hat stays, even when both are the same red. Switching to it with nothing selected selects what the
whole image would change, so you can take out what shouldn't be in it.

On the original image:

| Keys | Action |
| --- | --- |
| Click | Pick the From colour and select a new part |
| Shift + click | Add a part to the selection |
| Ctrl + click (Cmd on Mac) | Remove a part from the selection |
| Drag | Keep painting pixels (Single pixel) |
| Esc | Clear the selection |
| Hold Alt | Hide the zoom |

Settings for the clicked region:

- **Edge strength:** how sharp a change between two neighbouring pixels has to be to stop the
  region, 0 to 100. Lower it when the region leaks into a part next to it; raise it when it stops
  inside the part, at a step of its own shading.
- **Selection:** how the next click selects. It doesn't change what's already selected.
  - **Non-contiguous:** every pixel of the frame close to the clicked one, joined or not.
  - **Contiguous:** grows from the clicked pixel to its neighbours.
  - **Single pixel:** only the clicked pixel; dragging keeps going.
- **Grow through** (Contiguous only): **4 directions** grows to the pixels beside, above and below.
  **8 directions** also crosses corners, so thin diagonal lines are taken whole, but the region can
  slip through a diagonal gap in an outline.
- **Every match** selects every pixel the From colour matches, as the whole image would change.
- **Clear** clears the selection.

Settings changes and selections support Unity's Undo.

## Sprite sheets

On an image sliced into sprites (**Sprite Mode: Multiple** in its import settings), Recolor knows
where each frame is:

- A clicked part never spills into the next frame.
- **Animate frames**, above the result, plays the recoloured frames one after another, each aligned
  by its pivot. **FPS** sets the speed, and **Ping-pong** plays back to the first frame after the
  last one instead of jumping back to the start.

Frames play in name order, with numbers read as numbers, so `walk_10` comes after `walk_9`.

## Saving

- **Save as new image** saves a PNG next to the original. It copies the original's import settings, so
  a sliced sheet stays sliced, and its sprites are renamed after the new file.
- **File name** sets what the new image is called. It shows under the **▾** button beside Save as new
  image, open to start with; the button's tooltip shows the name it saves. Pick the mode with the small
  menu button beside the field, and type the text in it.
  - **Suffix** (the mode it starts in): the image's name, then your text. The text starts as
    `_recolored`, so `hero.png` saves `hero_recolored.png`; change it to `_blue` to save `hero_blue.png`.
  - **Prefix:** your text, then `_recolored`: `villain` saves `villain_recolored.png`.
  - **Custom name:** your text only: `hero_red` saves `hero_red.png`.

  An empty field saves as `<name>_recolored.png`. Each image keeps its own file name: opening another
  image brings back what was set for it, or the default. They're found by the image's GUID, so they
  survive a rename or a move, and stored in `ProjectSettings/RecolorFileNames.json`. An existing file is never replaced: when the name is
  taken, Unity adds a number to it, and a line under the buttons says so, with the ▾ open or closed.
- **Overwrite original** replaces the original file. It asks first, and Undo won't bring the file
  back. Only PNG originals can be overwritten.

## Image files and import settings

- **Supported:** PNG, JPG and any texture Unity imports (TGA, PSD, Aseprite...). Recolor reads the
  file itself, so the image doesn't need Read/Write on.
- **Aseprite** files need Unity's **2D Aseprite Importer** package (`com.unity.2d.aseprite`), and
  **PSD** files need the **2D PSD Importer** (`com.unity.2d.psdimporter`).
- **Large or compressed textures:** when the imported texture is smaller than the file or
  compressed, its colours can be off, and Recolor shows a warning. **Import at full size** changes
  its import settings to a Max Size large enough for the whole image, with no compression.

## Help and bug reports

**Tools > Recolor > Help** shows the version and the edition (Free or Pro), with links to buy Pro,
leave a review and email me.

To report a bug:

1. Open the image in the Recolor window and set it up as when the bug happened.
2. Click **Create bug report** in the Help window, or in the ⋮ menu of the Recolor window.
3. Pick where to save it. Recolor creates a folder with your settings and system info
   (`debug-info.json`), a copy of the image and its `.meta`, the image as Recolor read it, the result,
   and a `READ-ME.txt` on what to send.
4. Email those files with a short description of what happened.

## Recolor Pro

Recolor Pro, on the Unity Asset Store, adds what sprite sheets need: the selection found on every
frame, steps, stored selections, colour variants and transition sheets. It adds them to the
same Recolor window.

Recolor Pro already includes this package, so you can install Pro over it and everything keeps
working. To remove this one afterwards, use **Tools > Recolor > Remove Recolor LITE**.

## Support

Found a bug or have an idea for a feature? Email nefisto.developer@gmail.com.
For a bug, use **Tools > Recolor > Help > Create bug report** and attach what it saves: the
settings and system info, and the image you used.
