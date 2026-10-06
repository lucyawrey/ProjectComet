# Art and aesthetic

**Layer: game (Project Anima).** "Human-made creative content only" applies to the whole project. ShapeLand, the base's demo game, uses plain shapes in a toy-box style instead (`proposal.md`).

Agent notes on art direction. The project lead's working thoughts are in `.claude/notes/+ Quick Notes.md` ("Aesthetic").

## Decided

- **Human-made creative content only.** No AI-generated models, textures, readable in-game text, music or sound.
- **Low-poly art.**
- **Fidelity leans slightly towards PS1, and at most GameCube.** **PS1 quirks (affine texture warping, vertex jitter) are not recreated**; only the resolution and polygon budget are borrowed.
- **No screen-wide pixelation filter.** Players could add one through client-side mods (see `backend.md`).
- **Low-poly characters with low-res textures.**
- **Cute, but not overly chibi:** mostly human proportions, even if not realistic.
- **No character features are gender-locked.**
- **Body types: feminine and masculine, and maybe androgynous.** Clothing may have feminine or masculine variants, but no clothing is restricted by body type.
- **Ancestries will differ a lot, but we try to keep one humanoid skeleton for all player characters.**
  - **Extra parts are optional extra bones on the shared skeleton:** tails, long ears, wings and horns, ignored by ancestries without them and moved by simple physics or extra animations. Outfits account for them (e.g. tail holes).
  - **Player ancestries all use two-armed, plantigrade humanoid bodies** (no digitigrade legs or extra limbs). Monsters and companions are unaffected.
  - **How far proportions vary is decided with the ancestry designs.** Moderate differences share animations well; a very small or big-headed ancestry would need extra animation and fitting work or a variant skeleton.
- **Outfits are cute and functional:** they look good and do something, through their World Runes (Infinity Nikki is a reference).
- **World Runes from gear always match the character's visible appearance** (see `skills.md`, Outfit Magic).
- **Crystals can change a character's base design** on top of their real base form (see `classes.md`).
- **World tone: a bright, warm storybook baseline, with zones varying:** some zones go moody, eerie or harsh for contrast (marshes, ruins, dungeons). Crystal Chronicles is the lighting and mood reference.
- **Fog and haze: a light touch:** mostly clear sightlines, with fog only where a zone's mood calls for it. Streaming and LOD changes therefore can't rely on fog to hide them (see the zone format in `backend.md`).

## Considering

- **Texturing: leaning towards a mix** (pending a better understanding of the tech and workflows): painted trim sheets (reusable strips of painted material detail) plus vertex-colour shading for environments and most outfits, and per-asset painting for faces, hero gear and showpiece outfits. Palette/gradient texturing is the flatter, cheapest alternative.
- **Texture filtering: prototype both** crisp (nearest-neighbour) and soft (bilinear) before deciding.

## References

Specific games, kept separate from decisions. Phantasy Star Online, Crystal Chronicles and Signalis are the closest jumping-off points.

| Game | Reference for |
| --- | --- |
| Phantasy Star Online | Textures, character design |
| Crystal Chronicles | Textures, character design, lighting and mood |
| Signalis | Textures, character design |
| FFXIV | Variety in character sizes, shapes and unique outfits; outfit design; ancestry design. Slightly more high-res than wanted. |
| Breath of the Wild | Overall aesthetic; also a reference for world traversal (`world.md`) |

## Rejected

- **Recreating PS1 rendering quirks** and **a built-in pixel filter.**
- **Non-humanoid player body plans.**

## Open

- **Texture approach:** hand-painted low-res textures (PSO, Crystal Chronicles) or flat palette textures. Affects the cost of each outfit.
- **Outfit fitting:** how outfits fit each body type and ancestry (separate meshes or blend shapes), best tested in a prototype.
