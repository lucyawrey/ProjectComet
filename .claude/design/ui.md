# UI drafting

**Layer: mixed.** How game UI is drafted before it's built, for Comet's games; ShapeLand's phase 0 screens are the first use. Phase 0's UI pieces themselves are in `prototype.md` (3b).

## Decided

- **One drafting approach, applied first to ShapeLand phase 0** (the join screen, chat box, name labels and bubbles). Project Anima's screens wait.
- **Drafts are HTML/CSS mockups,** viewable in a browser.
- **Mockups stick to a USS-compatible subset of CSS:** flexbox layout, properties USS supports, transitions but no keyframe animations or grid, so the chosen mockup ports almost one to one to UI Toolkit (styles may carry over as USS).
- **Agents draft, the project lead reacts:** mockups are made from what's been discussed, with a few variants where a choice matters; the project lead picks and adjusts, then the chosen one is built in UI Toolkit.
- **Mockups live in `.claude/drafts/ui/`** (agent drafts) and are published as private claude.ai pages for review, so they can be opened anywhere and commented on.
- **Plain interface text may be written by agents:** labels, buttons and error messages are functional, not creative content. The human-made rule (`art.md`) still covers creative text such as lore, item descriptions and dialogue.
- **ShapeLand's UI all lives in the ShapeLand Unity project for now:** screens, styles, and the world-space label and bubble code, like chat itself. Pieces move into the Comet Unity package once Project Anima needs them.
- **ShapeLand's look is picked from variants** in the mockups: the plain look was chosen (`prototype.md`).

## Considering

## Rejected

- **Drafting straight in UI Toolkit:** each iteration is slower and harder to share outside Unity.
- **Wireframes in a separate design tool first:** an extra step, when USS-compatible mockups already carry over.

## Open
