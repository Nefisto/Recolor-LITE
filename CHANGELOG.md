# Changelog

All notable changes to Recolor are listed here, newest first.

## 1.0.2

- No changes to Recolor itself: this version adds a Recolor Pro feature.

## 1.0.1

- **File name** for Save as new image, with a small mode picker button:
  - **Suffix** (the default): the image's name, then your text, which starts as `_recolored`. Change it to `_blue` to save `hero_blue.png`.
  - **Prefix:** your text, then `_recolored`: `villain` saves `villain_recolored.png`.
  - **Custom name:** your text only.
- Each image keeps its own file name, found by its GUID so it survives a rename or a move, and stored in `ProjectSettings/RecolorFileNames.json`.
- When the file name is already taken, a note under the field says which name the image is saved as.

## 1.0.0

First release.

- Swap one colour of an image for another while keeping its shading.
- Clean edges: anti-aliased pixels are split into the colours they mix, so no fringe of the old colour is left.
- Keep shading: every shade keeps its distance when the new colour would push some past the limit.
- Recolour the whole image, or only the parts you click (non-contiguous, contiguous or single pixel).
- Sprite sheets: a clicked part never spills into the next frame, and Animate frames plays the recoloured frames.
- Save as a new image with the original's import settings, sprites renamed after the new file, or overwrite the original.
- Help window with the version, links and Create bug report.
