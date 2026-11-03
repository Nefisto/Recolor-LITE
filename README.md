# Recolor

Unity editor tool that swaps one colour of an image for another while keeping its shading, and
splits the anti-aliased pixels along edges back into the colours they mix, so only the part of the
old colour changes and no fringe of it is left behind.

- Recolour the whole image, or only the parts you click.
- Works with PNG, JPG, Aseprite, PSD and any image Unity imports.
- The shading always stays: every shade keeps its distance from the others.
- Sprite sheets: a clicked part never spills into the next frame, and **Animate frames** plays the
  recoloured frames beside the result, each by its pivot.

Open it from **Tools > Recolor > Open**. **Tools > Recolor > Help** shows the version, the edition (Free or Pro) and where to report a bug or suggest a feature. To report a bug, use **Create bug report** (also in the ⋮ menu of the Recolor window): it saves a folder with the settings and system info, the image and a READ-ME on what to send.

## Install

In **Window > Package Manager**, **+ > Add package from git URL**, with
`https://github.com/Nefisto/Recolor-LITE.git`.

## Recolor Pro

Recolor Pro, on the Unity Asset Store, adds what sprite sheets need: the selection found on every
frame, save points, saved selections, colour variants and transition sheets.
