# Items, inventory and collection

Agent notes on items, inventory, Storage, gear sets, outfits and soulbound items. Decisions made with the project lead on 2026-10-03. Collection is not yet discussed in detail.

## Decided

- **Inventory:** a fixed size per character, not upgradable by players (major updates may raise it). Each item type has its own stack size; going over it uses another slot, including for items with a stack size of 1.
- **Storage** is reached at settlements and holds a practically unlimited number of items.
- **Inventory and Storage share most of their systems.**
- **The only thing a character equips is a Class Crystal.** There is no separate equipment layer.
  - A crystal **references** a class entry, a loadout, a gear set, an outfit set and appearance data. These are stored separately from the crystal (gear sets may end up as part of the loadout).
  - Each crystal takes one inventory slot, so carrying many slowly fills the inventory (gentle pressure to specialise). Storage holds any number, limited only by crystal cost.
- **Gear sets:** items in a gear set technically live in Storage, but the UI and lore present them as being "in" the crystal.
  - **Gear can be equipped in the field**, with no cast. Under the hood it's a swap: the new item moves from the inventory into the set (Storage), and the old item moves from the set into the inventory. This is an implementation detail, not a loophole.
  - Only the **equipped** crystal's gear can be edited in the field.
  - **No gear changes in combat**, either directly or onto a crystal. Switching crystals in combat (with a cast) is still allowed and swaps class, loadout, gear and outfit together.
  - **One item can be in several gear sets.** Lore: class crystals resonate and share the same physical gear piece. Double equipping is impossible anyway, since only one crystal is equipped.
- **Outfit sets work the same way as gear sets.** An outfit is the actual gear piece worn over the top (like a cosmetic slot in other games), not a copied "glamour".
  - If the same item is in the same slot of both the equipped gear set and the outfit set, the outfit slot counts as empty and the item just shows in the gear slot.
- **Soulbound items** exist to make items feel physical: every item is "real" and can be handed around.
  - Soulbound items can be dropped, held by other players and handed back, and recalled by the owner.
  - Another player holding your soulbound Class Crystal can't equip it, but can carry it and give it back.
  - Soulbound gear can be lent out freely. Once it returns to the owner, it counts as empty in the borrower's gear sets.
- **Crystal borrowing is scrapped for now** (see Considering).

## Considering

- **Crystal borrowing (on hold).** Unique, but removed for simplicity. Bring it back if a feature this unusual becomes worth the complexity. If it returns, use the **lockout** rule:
  - A borrower uses the owner's class level and skill setup (they can rearrange hotbar/controller bindings), never earns XP for the owner, and all XP they earn goes to their own Soul XP. Borrowing a friend's crystal to farm Soul XP was a welcome trick.
  - The borrower gets the crystal's gear. While the crystal is lent, its items count as "lent out" and the owner's other gear and outfit sets show those slots as empty.
  - "Classes you have" for flex Skills would still mean crystals soulbound to you wherever they are; lending must never break the owner's loadouts, and a borrower can't take its Skills into their own crystals.
  - Rejected alternative: letting owner and borrower both use the same item (effectively a free copy of the gear, and double mastery on one item).
- **Everything that holds items is a container (agent suggestion):** inventory, Storage, bags, gear sets and outfit sets, each with data-driven rules (capacity, what it accepts, stacking, where it can be accessed). Equipping in the field is then a move between two containers the rules allow (inventory ⇄ gear set anywhere out of combat; Storage ⇄ inventory only at settlements).
- **A referenced item that isn't in Storage (agent suggestion):** other sets keep the reference, but the slot counts as empty until the item is back in Storage. Nothing auto-equips from the inventory. Selling or trading a referenced item warns first.

## Open

- Does mastery build up on outfit items? Their World Runes are active, so it may be natural.
- Bags: keep both kinds from the notes (several slots of a limited range of item types, or bundle-style any type up to a total quantity)? Bags inside bags (agent: no)?
- Where else is Storage available: housing, guild halls, tents or camps?
- Dropped items in the open world: persisted, or gone (except soulbound items, which can be recalled)?
- Soulbound vs untradeable: soulbound gear can be lent, so what exactly stops it being given away for good? Is the finite rune stone rule (never trade away your first copy) the same thing as soulbound?
- Collection (not yet discussed): what counts as collected (first acquisition? crafted quality and dye variants?), and what "easier ways to get it again" means in practice without becoming a duplication source.
