# Free low-poly assets: research pass

**Layer: mixed.** Placeholder and prototype assets serve both ShapeLand-adjacent tests and Anima's phase 3 onwards.

Agent research, written 2026-10-06. A basic survey. **Decided:** Kenney, Quaternius and KayKit are the starting sources for placeholder and prototype assets (`art.md`). No specific packs are chosen.

## What to check for each pack

- **Licence, against an open client repo.** The client source is open (`proposal.md`, security), so assets committed to the repo are effectively redistributed. CC0 is simplest. CC-BY works with an attribution list. Share-alike licences (CC-BY-SA, GPL) may conflict with Anima's official content licence. Custom itch.io licences need reading one by one.
- **Human-made.** We use human-made creative content only (`art.md`). Long-running solo artists with packs that predate generative AI are low-risk; community upload sites now carry some AI-generated models, so check per model.
- **Style fit.** PS1-leaning, at most GameCube; low-res textures; cute, not overly chibi (`art.md`). Many free packs use flat colours with no textures, a different look but fine for placeholders.
- **Rigging.** We plan one humanoid skeleton for all player characters (`art.md`). Pack characters come with their own rigs; they'd need retargeting, or would only serve as placeholders.

## Sources

| Source | Licence | What's there | Notes |
| --- | --- | --- | --- |
| **Kenney** (kenney.nl) | CC0 | Large library of 3D kits (nature, castles, dungeons, platformer pieces, characters) | Clean, flat-coloured style; one studio, long track record. Good for blockouts. |
| **Quaternius** (quaternius.com) | CC0 | ~80 packs, ~1,150 models: modular characters, monsters, nature, medieval, plus a universal animation library | Stylised low-poly; glTF and FBX. Its shared animation library is the closest to our one-skeleton plan among free sources. |
| **KayKit** (Kay Lousberg, itch.io) | CC0 | Rigged and animated characters (Adventurers, Skeletons and more), dungeon, platformer and other kits; separate animation packs | Chunky, cute style, closer to chibi than we want for characters, but props and dungeons fit. Paid "extra" tiers add more. Don't resell unmodified copies. |
| **Poly Pizza** (poly.pizza) | Mostly CC-BY, some CC0 | Google Poly archive (~2,300 models) plus community uploads | Very mixed quality and style; attribution needed for most. Check authorship per model. |
| **OpenGameArt** (opengameart.org) | Mixed: CC0, CC-BY, CC-BY-SA, GPL | Large, older community library | Filter by licence; avoid share-alike for anything that could ship. |
| **itch.io asset packs** | Per pack (some CC0, many custom) | Includes PS1/PSX-style packs (props, interiors, buildings), closest to our target fidelity | Read each licence; style varies widely. itch.io lets you filter by the CC0 and low-poly tags. |
| **Sketchfab** | Per model (often CC-BY) | Huge range | Many AI-generated and photogrammetry uploads now; check per model. Mostly off-style. |
| **Unity Asset Store** (free assets) | Asset Store EULA | Many free packs | The standard EULA doesn't allow redistribution, so these can't go in a public repo. Fine for private local experiments only. |
| **Mixamo** (Adobe) | Adobe terms | Free humanoid animations and auto-rigging | Allowed in games, but redistributing the raw files is restricted, so they're awkward in an open repo. Useful for local prototyping. |

## Agent observations

- For prototypes, **Kenney, Quaternius and KayKit** cover most needs under CC0 with known human authors, so they're the low-risk starting set.
- **No free source matches our final look** (PS1-leaning with low-res textures and our own skeleton). Free assets are most useful as placeholders and blockouts; shipped art will mostly be our own.
- An attribution file (`CREDITS.md` or similar) from day one makes CC-BY assets easy to use if we want them.

## Sources consulted

- Quaternius overview: [Cinevva, free 3D model sites](https://app.cinevva.com/guides/game-assets-guide.html)
- KayKit licence: [KayKit on itch.io](https://kaylousberg.itch.io/kaykit-adventurers), [KayKit animations](https://kaylousberg.itch.io/kaykit-animations)
- Poly Pizza licences: [Cinevva, free 3D model sites](https://app.cinevva.com/hi/guides/free-3d-model-sites)
- Unity Asset Store EULA: [Asset Store EULA FAQ](https://assetstore.unity.com/browse/eula-faq)
- PS1-style packs: [itch.io CC0 low-poly tag](https://itch.io/game-assets/newest/tag-cc0/tag-low-poly), [PS1 Asset Pack Volume 1](https://halfhuman.itch.io/ps1-game-asset-vol1)
- Kenney, OpenGameArt, Sketchfab and Mixamo are from general knowledge, not checked against sources in this pass.
