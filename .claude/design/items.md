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
  - **Mastery builds up on an outfit item's World Runes** (they're active while worn as an outfit).
- **Soulbound items** exist to make items feel physical: every item is "real" and can be handed around.
  - Soulbound items can be dropped, held by other players and handed back, and recalled by the owner.
  - Another player holding your soulbound Class Crystal can't equip it, but can carry it and give it back.
  - Soulbound gear can be lent out freely, and other players can equip it. Once it returns to the owner, it counts as empty in the borrower's gear sets.
  - **Only the owner can do anything destructive or transforming** to a soulbound item: sell, trade away, dismantle, use as a crafting input, upgrade, extract its Rune, or consume it. A soulbound consumable (e.g. a rune stone consumed to learn its Rune) can't be consumed by anyone else. Such items are nearly useless to hand around; the rule exists to keep items physical and the rules clear. Others can wear (gear), carry, store, drop and hand on soulbound items.
  - **Handing soulbound items over:** the trade UI can be used, but the owner can never receive anything in return: a trade with soulbound items is always only soulbound items on one side and nothing on the other. Dropping and picking up also works. Scams are still possible this way, but harder.
  - **Picked-up soulbound items are clearly marked** as belonging to someone else and say they will return to their owner eventually.
  - **Finite rune stones use the soulbound rule:** the first copy a character receives is soulbound to them; later copies are ordinary items that can be traded, broken down or sold to NPCs.
  - The owner can recall a soulbound item at any time, for free, wherever it is (held by another player, in a dropped bag, or lost after despawning). Recalling is done at a specific place: Storage or the Crystal Archives (which one is still to decide).
  - **Soulbound items also return on their own** 24 hours after leaving the owner's possession, to the same place recalls deliver to. Picked-up soulbound items say so.
  - **Lost (despawned) soulbound items use the same return mechanic, but much faster** (exact time to decide). A manual recall is only needed to get them back sooner.
- **Crystal borrowing is scrapped for now** (see Considering).
- **Containers vs sets (adopted from an agent suggestion):**
  - **Containers** are physical locations: inventory, Storage and bags. Each item is in exactly one, and every move is one transaction.
  - **Gear and outfit sets are reference lists, not containers:** typed slots that each point at an item or are empty. Several sets can point at the same item.
  - Both share data-driven **slot rules** (what a slot accepts), so a helmet slot and a herb-only bag use the same "does this fit?" check.
  - **Equipping in the field** = new item inventory → Storage, old item Storage → inventory, and the equipped set's reference updated. Other sets referencing the old item read that slot as empty until it's back in Storage.
  - **Slots read as empty; references are never cleared** when an item leaves Storage (withdrawn by the owner, swapped out in the field, lent, dropped). When the item returns to Storage (deposited or recalled), every set that references it has it again automatically.
  - **Effective state** (what you're actually wearing) is derived from the equipped sets' references plus whether each item is currently in Storage. The same check covers lent soulbound gear returning to its owner.
- **Bags** can sit in the inventory or be dropped on the ground, and keep their contents either way. Depositing a bag into Storage empties its contents into Storage.
- **There are no soulbound bags.** A soulbound item inside a dropped bag can still be recalled out of it by its owner.
- **Dropped items and bags despawn after one hour.** This is the main way most items are destroyed.
- **Timers are easy server configuration**, not hard-coded: the despawn time and the soulbound return times can be tuned (e.g. to manage server load, or by private server hosts).
- **Soulbound items that despawn** enter a lost state: they no longer exist in the world until the owner recalls them.

## Considering

- **Crystal borrowing (on hold).** Unique, but removed for simplicity. Bring it back if a feature this unusual becomes worth the complexity. If it returns, use the **lockout** rule:
  - A borrower uses the owner's class level and skill setup (they can rearrange hotbar/controller bindings), never earns XP for the owner, and all XP they earn goes to their own Soul XP. Borrowing a friend's crystal to farm Soul XP was a welcome trick.
  - The borrower gets the crystal's gear. While the crystal is lent, its items count as "lent out" and the owner's other gear and outfit sets show those slots as empty.
  - "Classes you have" for flex Skills would still mean crystals soulbound to you wherever they are; lending must never break the owner's loadouts, and a borrower can't take its Skills into their own crystals.
  - Rejected alternative: letting owner and borrower both use the same item (effectively a free copy of the gear, and double mastery on one item).
- **Mastery on an outfit item's other Runes:** maybe builds up too, but slower, even though those Runes aren't active.
- **Mastery per Rune (agent suggestion):** since gear can have multiple Runes, track mastery per Rune on the item. Extracting one Rune resets only that Rune's mastery.
- **Selling or trading a referenced item (agent suggestion):** warn first. Nothing auto-equips from the inventory.
- **Lent soulbound gear (agent suggestions):**
  - Mastery builds on the item while a borrower wears it (mastery belongs to the item), so the owner benefits.
  - A recall of gear the borrower has equipped takes effect immediately; the slot reads as empty, like any other item that has left Storage. (Alternative: delay until the borrower leaves combat or the instance.)

## Open

- Bags: keep both kinds from the notes (several slots of a limited range of item types, or bundle-style any type up to a total quantity)? Bags inside bags (agent: no)? Does a bag deposited into Storage stay as an empty bag?
- Where else is Storage available: housing, guild halls, tents or camps?
- Do dropped items survive a shard restart (temporary structures don't)? If not, soulbound ones simply become lost.
- Recall location: Storage or the Crystal Archives?
- How fast do lost soulbound items return?
- Collection (not yet discussed): what counts as collected (first acquisition? crafted quality and dye variants?), and what "easier ways to get it again" means in practice without becoming a duplication source.
