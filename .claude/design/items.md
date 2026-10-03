# Items, inventory and collection

Agent notes on items, inventory, Storage, gear sets, outfits and soulbound items. Decisions made with the project lead on 2026-10-03. Collection is not yet discussed in detail.

## Decided

- **Inventory:** a fixed size per character, not upgradable by players (major updates may raise it). Each item type has its own stack size; going over it uses another slot, including for items with a stack size of 1.
- **Storage** is reached at settlements, housing and guild halls, and holds a practically unlimited number of items.
- **Inventory and Storage share most of their systems.**
- **The only thing a character equips is a Class Crystal.** There is no separate equipment layer.
  - A crystal **references** a class entry, a loadout, a gear set, an outfit set and appearance data. These are stored separately from the crystal (gear sets may end up as part of the loadout).
  - Each crystal takes one inventory slot, so carrying many slowly fills the inventory (gentle pressure to specialise). Storage holds any number, limited only by crystal cost.
- **Gear sets:** items in a gear set technically live in Storage, but the UI and lore present them as being "in" the crystal.
  - **Gear can be equipped in the field**, with no cast. Under the hood it's a swap: the new item moves from the inventory into the set (Storage), and the old item moves from the set into the inventory. This is an implementation detail, not a loophole.
  - Only the **equipped** crystal's gear can be edited in the field.
  - **No gear changes in combat**, either directly or onto a crystal. Switching crystals in combat (with a cast) is still allowed and swaps class, loadout, gear and outfit together.
  - **Gear and outfit sets can have empty slots.**
  - **Unique-equipped is universal:** a set can never hold two items of the same type (e.g. two copies of one ring), matching how a duplicate Rune can't be slotted.
  - **The tutorial's gear moves automatically** to the first real crystal when it's unlocked.
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
  - The owner can recall a soulbound item at any time, for free, wherever it is (held by another player or in a dropped bag). Recalling is done at a specific place (intentionally undecided for now; see Open).
  - **Soulbound items also return on their own** 24 hours after leaving the owner's possession, to the same place recalls deliver to. Picked-up soulbound items say so.
  - **No lost state:** a soulbound item that despawns (or is on the ground at a shard restart) returns to its owner immediately, through the same return mechanic.
- **Crystal borrowing is scrapped for now** (see Considering).
- **Containers vs sets (adopted from an agent suggestion):**
  - **Containers** are physical locations: inventory, Storage and bags. Each item is in exactly one, and every move is one transaction.
  - **Gear and outfit sets are reference lists, not containers:** typed slots that each point at an item or are empty. Several sets can point at the same item.
  - Both share data-driven **slot rules** (what a slot accepts), so a helmet slot and a herb-only bag use the same "does this fit?" check.
  - **Equipping in the field** = new item inventory → Storage, old item Storage → inventory, and the equipped set's reference updated. Other sets referencing the old item read that slot as empty until it's back in Storage.
  - **Slots read as empty; references are never cleared** when an item leaves Storage (withdrawn by the owner, swapped out in the field, lent, dropped). When the item returns to Storage (deposited or recalled), every set that references it has it again automatically.
  - **Effective state** (what you're actually wearing) is derived from the equipped sets' references plus whether each item is currently in Storage. The same check covers lent soulbound gear returning to its owner.
- **Bags** come in both kinds from the notes: several slots' worth of a limited range of item types, or bundle-style any type up to a total quantity. Bags can't go inside bags.
- Bags can sit in the inventory or be dropped on the ground, and keep their contents either way. Depositing a bag into Storage empties its contents into Storage; the bag stays in Storage, empty.
- **There are no soulbound bags.** A soulbound item inside a dropped bag can still be recalled out of it by its owner.
- **Dropped items and bags despawn after one hour, and all of them vanish on a shard restart.** This is the main way most items are destroyed.
- **Timers are easy server configuration**, not hard-coded: the despawn time and the soulbound return times can be tuned (e.g. to manage server load, or by private server hosts).
- **Collection goals:** showing off, using things (outfits, hairstyles…) and protection against losing things. Protection can be dropped if it conflicts with the economy.
- **Things are items; knowledge is a flag** (adopted from an agent suggestion):
  - **Items:** gear and outfits (actual pieces), materials, consumables, Class Crystals, rune stones, bags.
  - **Learned flags** (character-wide unlocks): e.g. hairstyles, dyes, recipes, Rune unlocks, emotes. Usually unlocked by consuming an item, which stays tradeable until someone learns it.
  - **Companions** are their own system (see notes; not yet discussed).
  - Outfits stay real items, partly because unlocked outfits would become free, permanent sources of World Runes.
- **Three separate data concepts:** items, learned flags, and an **item collection log** (records items a character has obtained). The collection UI can show flags and the item collection log together.
  - Why the log is separate from flags: **the item collection log gives no mechanical benefit** (it's for showing off), while flags are real unlocks. Possible exceptions: re-obtaining items, or unlocking crafting recipes from collected items (see Open).
- **The item collection log has one entry per item type**, set the first time you obtain it (quality and dye variants don't count separately).
- **Tradeability is a three-tier content setting on each item type** (adopted from the old .NET DataCenter's `ContentItemTradeability`):
  - **Market** (default): tradeable and listable on the market.
  - **Direct:** player-to-player trades only, never on the market (e.g. quest drops from monsters).
  - **Untradeable:** every instance is always soulbound to whichever player receives it.
- **Soulbound is a separate per-instance state.** The Untradeable tier always sets it, but soulbound items are not implicitly untradeable. For example, finite rune stones are a Market type even though your first copy is soulbound.
- **Some recipes are learned from the item collection log** (first obtaining an item grants the recipe flag). The log itself still gives no other benefit.
- **Re-obtaining** applies only to destroyed items of untradeable types, and only if you don't currently have a soulbound copy of that type. It's done at an NPC.
- **Currency works like Animal Crossing:** a wallet number that normally takes no inventory slots. Players can take money out of the wallet as coin stacks (items), and drop them on the ground, where another player might pick them up. Picked-up coins go straight into the wallet by default; a player setting can keep them as coin stacks instead.
- **Dropping over destroying:** players generally get rid of things by dropping them rather than destroying them, so someone else might pick them up. This adds interactions.
- **Housing furniture is items**, and goes in the item collection log. **Placed in a house** is a new item location alongside inventory, Storage, bag and ground.
- **Story items:** when the story needs an item to make sense (a letter to deliver), it's a real item, for physicality: you have to make space for the letter. Plenty of quest progress is simply flags.
  - Most story items are untradeable (always soulbound).
  - Quest drops from monsters may be ordinary, non-soulbound items, but can't be listed on the market: **direct player trades only**.

## Considering

- **Storage in tents or camps:** maybe, perhaps only for higher-tier camps.
- **Recall stations:** soul recall could be its own kind of station, separate from Storage and the Crystal Archives. "Extra large" stations might combine all three (Storage, Crystal Archives, recall).
- **Crystal borrowing (on hold).** Unique, but removed for simplicity. Bring it back if a feature this unusual becomes worth the complexity. If it returns, use the **lockout** rule:
  - A borrower uses the owner's class level and skill setup (they can rearrange hotbar/controller bindings), never earns XP for the owner, and all XP they earn goes to their own Soul XP. Borrowing a friend's crystal to farm Soul XP was a welcome trick.
  - The borrower gets the crystal's gear. While the crystal is lent, its items count as "lent out" and the owner's other gear and outfit sets show those slots as empty.
  - "Classes you have" for flex Skills would still mean crystals soulbound to you wherever they are; lending must never break the owner's loadouts, and a borrower can't take its Skills into their own crystals.
  - Rejected alternative: letting owner and borrower both use the same item (effectively a free copy of the gear, and double mastery on one item).
- **Active vs inactive mastery:** any worn item's Runes build mastery at the full rate while active and more slowly while inactive. This covers an outfit item's non-World Runes, and also gear's World Runes while an outfit's World Runes override them.
- **Mastery per Rune (agent suggestion):** since gear can have multiple Runes, track mastery per Rune on the item. Extracting one Rune resets only that Rune's mastery.
- **No per-character ownership limit (agent suggestion):** skip "unique" items (at most one held per character); it clashes with physical items (every trade, pickup and reward would need a refusal path) and with traders holding stock. Special cases like one-off story items are handled by quest logic.
- **Selling or trading a referenced item (agent suggestion):** warn first. Nothing auto-equips from the inventory.
- **Lent soulbound gear (agent suggestions):**
  - Mastery builds on the item while a borrower wears it (mastery belongs to the item), so the owner benefits.
  - A recall of gear the borrower has equipped takes effect immediately; the slot reads as empty, like any other item that has left Storage. (Alternative: delay until the borrower leaves combat or the instance.)

## Open

- **Recall location** (intentionally undecided): Storage, the Crystal Archives, or separate recall stations (see Considering).
- **Re-obtaining cost:** free, or a fee at the NPC?
- **Remaining item/flag boundaries (agent leans, not discussed):**
  - Keys: items for ones you can hand over (a house key for a friend), flags for one-off access unlocks.
  - Teleport and flight attunements, titles, achievements: flags (attunements belong to unlockables, not yet discussed).
  - Music and decorative collectables (like FFXIV orchestrion rolls): flags learned by consuming an item, like dyes.
  - Crafting and gathering tools: gear in the gear set (needs tool slots, or uses weapon slots). Touches the most systems.
  - Companion items (eggs, capture items): items until hatched or registered; details wait for companions.
