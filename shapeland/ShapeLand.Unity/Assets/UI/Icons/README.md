# Interface icons

Icons for ShapeLand's touch controls, taken from published icon packs (not drawn for the project). Each file is the pack's white vector icon drawn at 256 x 256, the glyph scaled to 208 px on its longer side and centered on its own bounds. They import uncompressed, without mipmaps, so they stay sharp.

| File | Source | Icon | License |
| --- | --- | --- | --- |
| `jump.png` | [Kenney, Game Icons](https://kenney.nl/assets/game-icons) (`Vector/vector_whiteIcons.svg`) | `arrowUp` | CC0 (`Kenney Game Icons License.txt`) |
| `look.png` | Kenney, Game Icons | `singleplayer` | CC0 |
| `return.png` | Kenney, Game Icons | `home` | CC0 |
| `chat.png` | [Google, Material Symbols](https://github.com/google/material-design-icons) (`@material-symbols/svg-400` 0.48.1, `rounded/chat-fill.svg`) | `chat`, filled | Apache 2.0 (`Material Symbols LICENSE.txt`) |

Drawn with `rsvg-convert`. Kenney's icons are cells of the pack's vector sheet, a grid of 50-unit cells: `arrowUp` row 0 column 2, `home` row 3 column 3, `singleplayer` row 1 column 12 (counting from 0, matched against the pack's PNGs).
