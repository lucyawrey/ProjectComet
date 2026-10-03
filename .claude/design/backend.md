# Backend and data model

Agent notes on backend architecture and data-model ideas. Almost everything here is still open or an agent suggestion.

## Decided

- **Private servers should be easy to self-host**, without a large proprietary dependency.
- **One shared container system** for bags, Class Crystals and Storage (see `classes.md`).

## Considering

- **Backend:** plan for two approaches:
  - A. A custom .NET stack (Gateway server, Data Center server with PostgreSQL, headless game server, game client).
  - B. A product like SpacetimeDB. The project lead is slightly biased against it because of the self-hosting requirement; verify current license terms before weighing it.
  - C (unlikely). A systems-language backend written with help from another developer, if neither A nor B works.
- **Data model (agent suggestions, not agreed):**
  - **Items:** every item has an owner (a character, or account Storage) and a location: inventory slot, equipped slot, Storage, or inside another item (bag or crystal), plus a slot index. Container rules are data. Moving, trading and equipping are all "change the item's location" in one database transaction, so an item can never be in two places.
  - **Class entries:** current XP, highest level, when unlocked. A Class Crystal is an item with a soulbound owner, a reference to one of the owner's class entries, a bought/granted flag and a loadout reference. Promotion is one transaction: check requirement, move XP and create the entry if new, convert the crystal, charge resources.
  - **Loadouts:** one slot list per loadout; each slot has a kind (Skill or Rune), a colour, a binding if it's a Skill slot, and a locked flag.
  - **Mastery:** gear and class Runes share one mastery system; progress is stored on the item (gear) or the class entry (class).
  - **Finite rune stones:** the first copy a character receives is marked soulbound on acquisition; later copies are ordinary items.
  - **Anima escrow for locked content:** entry moves Anima into a hold tied to the instance; a clear finalises it, anything else refunds it. On startup, holds from crashed instances are refunded.
  - **Per-player gathering nodes:** per-player records of which nodes have been used.
  - **Temporary structures** (campfires, pitched tents) exist only on the shard; nothing is persisted.

## Open

- **Gateway and Data Center: one service or two?** In the custom .NET approach, should the Gateway server (public-facing, primarily HTTP API) and the Data Center server (database API layer) be the same thing or separate?
- **Are the plans sensible?** Find real-world examples of documented MMO backends (talks, postmortems, open-source servers, engineering blogs) and compare our architecture against them.
