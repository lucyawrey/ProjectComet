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
- **Containers vs sets (adopted from an agent suggestion):**
  - **Containers** are physical locations: inventory, Storage and bags. Each item is in exactly one, and every move is one transaction.
  - **Gear and outfit sets are reference lists, not containers:** typed slots that each point at an item or are empty. Several sets can point at the same item.
  - Both share data-driven **slot rules** (what a slot accepts), so a helmet slot and a herb-only bag use the same "does this fit?" check.
  - **Equipping in the field** = new item inventory → Storage, old item Storage → inventory, and the equipped set's reference updated. Other sets referencing the old item read that slot as empty until it's back in Storage.
  - **Effective state** (what you're actually wearing) is derived from the equipped sets' references plus whether each item is currently in Storage. The same check covers lent soulbound gear returning to its owner.
- **Bags** can sit in the inventory or be dropped on the ground, and keep their contents either way. Depositing a bag into Storage empties its contents into Storage.
- **There are no soulbound bags.** A soulbound item inside a dropped bag can still be recalled out of it by its owner.
- **Dropped items and bags despawn eventually.** This is the main way most items are destroyed.
- **Soulbound items that despawn** enter a lost state: they no longer exist in the world until the owner recalls them.

## Considering

- **Crystal borrowing (on hold).** Unique, but removed for simplicity. Bring it back if a feature this unusual becomes worth the complexity. If it returns, use the **lockout** rule:
  - A borrower uses the owner's class level and skill setup (they can rearrange hotbar/controller bindings), never earns XP for the owner, and all XP they earn goes to their own Soul XP. Borrowing a friend's crystal to farm Soul XP was a welcome trick.
  - The borrower gets the crystal's gear. While the crystal is lent, its items count as "lent out" and the owner's other gear and outfit sets show those slots as empty.
  - "Classes you have" for flex Skills would still mean crystals soulbound to you wherever they are; lending must never break the owner's loadouts, and a borrower can't take its Skills into their own crystals.
  - Rejected alternative: letting owner and borrower both use the same item (effectively a free copy of the gear, and double mastery on one item).
- **Selling or trading a referenced item (agent suggestion):** warn first. Nothing auto-equips from the inventory.

## Open

- Does mastery build up on outfit items? Their World Runes are active, so it may be natural.
- Bags: keep both kinds from the notes (several slots of a limited range of item types, or bundle-style any type up to a total quantity)? Bags inside bags (agent: no)? Does a bag deposited into Storage stay as an empty bag?
- Where else is Storage available: housing, guild halls, tents or camps?
- Despawn timing: how long do dropped items last, and do they survive a shard restart (temporary structures don't)?
- Recalling soulbound items: where can it be done ("an appropriate location" in the notes), and does it cost anything (e.g. Anima)? Can an owner recall lent soulbound gear at any time?
- Soulbound vs untradeable: soulbound gear can be lent, so what exactly stops it being given away for good? Is the finite rune stone rule (never trade away your first copy) the same thing as soulbound?
- Collection (not yet discussed): what counts as collected (first acquisition? crafted quality and dye variants?), and what "easier ways to get it again" means in practice without becoming a duplication source.
