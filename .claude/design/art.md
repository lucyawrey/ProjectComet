# Art and aesthetic

Agent notes on art direction. Not yet discussed in detail; the project lead's notes (`.claude/notes/+ Quick Notes.md`, "Aesthetic") have their working thoughts.

## Decided

- **Human-made creative content only.** No AI-generated models, textures, readable in-game text, music or sound.
- **Low-poly art.**
- **World Runes from gear always match the character's visible appearance** (see `skills.md`, Outfit Magic).
- **Crystals can change a character's base design** on top of their real base form (see `classes.md`).
- **No screen-wide pixelation filter** (project lead, 2026-10-04). Players could add one through client-side mods (see `backend.md`).
- **Fidelity leans slightly towards PS1, and at most GameCube** (project lead, 2026-10-04). **PS1 quirks (affine texture warping, vertex jitter) are not recreated**; only the resolution and polygon budget are borrowed.
- **Low-poly characters with low-res textures** (project lead, 2026-10-04). The texturing technique is open (question 3).
- **Cute, but not overly chibi** (project lead, 2026-10-04): mostly human proportions, even if not realistic.
- **No character features are gender-locked** (project lead, 2026-10-04).
- **Body types: feminine and masculine, and maybe androgynous** (project lead, 2026-10-04). Clothing may have feminine or masculine variants, but no clothing is restricted by body type.
- **Ancestries will differ a lot, but we try to keep one humanoid skeleton for all player characters** (project lead, 2026-10-04).
  - **Extra parts are optional extra bones on the shared skeleton** (2026-10-04, adopted from an agent suggestion): tails, long ears, wings and horns, ignored by ancestries without them and moved by simple physics or extra animations. Outfits account for them (e.g. tail holes).
  - **Non-humanoid body plans are out of scope for player ancestries** (2026-10-04, adopted from an agent suggestion): all use two-armed, plantigrade humanoid bodies (no digitigrade legs or extra limbs). Monsters and companions are unaffected.
  - **How far proportions vary is decided with the ancestry designs** (project lead, 2026-10-04). Moderate differences share animations well; a very small or big-headed ancestry would need extra animation and fitting work or a variant skeleton.

- **World tone: a bright, warm storybook baseline, with zones varying** (project lead, 2026-10-04): some zones go moody, eerie or harsh for contrast (marshes, ruins, dungeons). Crystal Chronicles is the lighting and mood reference.
- **Fog and haze: a light touch** (project lead, 2026-10-04): mostly clear sightlines, with fog only where a zone's mood calls for it. Streaming and LOD changes therefore can't rely on fog to hide them, which puts more weight on distant-zone LODs (see the parked zone-format details in `backend.md`).

## Considering

- **Texturing: leaning towards a mix** (project lead, 2026-10-04, pending a better understanding of the tech and workflows): painted trim sheets (reusable strips of painted material detail) plus vertex-colour shading for environments and most outfits, and per-asset painting for faces, hero gear and showpiece outfits. Palette/gradient texturing (agent suggestion) was explained as the flatter, cheapest option. (The shared humanoid rig, suggested alongside it, is now decided above.)
- **Texture filtering: prototype both** crisp (nearest-neighbour) and soft (bilinear) before deciding (project lead, 2026-10-04).

## References

Specific games, kept separate from decisions (project lead, 2026-10-04). Phantasy Star Online, Crystal Chronicles and Signalis are the closest jumping-off points. What each is a reference for (project lead, 2026-10-04):

| Game | Reference for |
| --- | --- |
| Phantasy Star Online | Textures, character design |
| Crystal Chronicles | Textures, character design, lighting and mood |
| Signalis | Textures, character design |
| FFXIV | Variety in character sizes, shapes and unique outfits; outfit design; ancestry design. Slightly more high-res than wanted. |

## Open

Discussion started 2026-10-04; resume here. The project lead's notes (Quick Notes, "Aesthetic") say, as working thoughts rather than decisions: low-poly characters with low-res textures; cute but able to look cool or sexy; clothing and hairstyles not gender-locked; ancestries may differ a lot; more than two body types; closest references Phantasy Star Online, Crystal Chronicles and Signalis, probably without a pixel filter; FFXIV a good reference for variety in sizes, shapes and outfits but slightly more high-res than wanted.

Agent-drafted questions, in rough order:

1. ~~Which of the note points are decisions?~~ Answered 2026-10-04 (see Decided and References).
2. ~~What is each reference for?~~ Answered 2026-10-04 (see References).
3. Texture approach: hand-painted low-res textures (PSO/Crystal Chronicles) or flat palette textures? Affects the cost of each outfit.
4. ~~How far ancestries vary on one skeleton?~~ Answered 2026-10-04 (see Decided); proportions wait for ancestry designs. Still open: how outfits fit each body type and ancestry (separate meshes or blend shapes), best tested in a prototype.
5. ~~Environment and world tone?~~ Answered 2026-10-04 (see Decided).
6. ~~Should rendering constraints shape the look?~~ Answered 2026-10-04 (fog and haze a light touch; see Decided). The technical side stays with the parked zone-format details in `backend.md`.
