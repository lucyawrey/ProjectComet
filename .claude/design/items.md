# Items, inventory and collection

**Layer: mixed.** Base (Comet): inventory, Storage and bags as containers, slot rules and "something is equipped", an owner or binding field on items, tradeability tiers, learned flags, currency as items, the ledger, crafter signatures. Game (Project Anima): crystals as the only equipped thing, gear and outfit sets as references, the outfit overlay, soulbound rules (recall, return, lending, re-obtaining), the item collection log, the coin purse, wear and repair, dyes, rune stones, World Rune mastery, housing furniture.

Agent notes on items, inventory, Storage, gear sets, outfits and soulbound items.

## Decided

### Containers

- **Inventory:** a fixed size per character, not upgradable by players (major updates may raise it). Each item type has its own stack size; going over it uses another slot, including for items with a stack size of 1.
- **Storage** is reached at settlements, housing and guild halls, and holds a practically unlimited number of items. **The goal is that players can collect every item and gear piece in the game.** Sane caps warn players only at ridiculous numbers.
- **Inventory and Storage share most of their systems.**
- **Containers vs sets:**
  - **Containers** are physical locations: inventory, Storage and bags. Each item is in exactly one, and every move is one transaction.
  - **Gear and outfit sets are reference lists:** typed slots that each point at an item or are empty. Several sets can point at the same item.
  - Both share data-driven **slot rules** (what a slot accepts), so a helmet slot and a herb-only bag use the same "does this fit?" check.
- **Bags** come in two kinds: several slots' worth of a limited range of item types, or bundle-style any type up to a total quantity. Bags can't go inside bags.
- Bags can sit in the inventory or be dropped on the ground, and keep their contents either way. Depositing a bag into Storage empties its contents into Storage; the bag stays in Storage, empty.
- **Bag UI:**
  - Each bag type can have its own UI (e.g. the coin purse).
  - A selector switches between the main inventory and any carried bag.
  - Bags can optionally be hidden from the main inventory grid; an indicator then shows how many inventory slots bags are using.
  - When bags are shown in the inventory, they can be opened either from the selector or by clicking them in the main inventory.
- **A soulbound item inside a dropped bag can be recalled out of it by its owner.**
- **Dropped items and bags despawn after one hour, and all of them vanish on a channel restart.** On a channel merge they carry over instead (see `world.md`). This is the main way most items are destroyed.
- **Dropping over destroying:** players generally get rid of things by dropping them rather than destroying them, so someone else might pick them up.
- **Timers are easy server configuration**, not hard-coded: the despawn time and the soulbound return times can be tuned (e.g. to manage server load, or by private server hosts).
- **No per-character ownership limit:** there are no "unique" items limited to one held per character (it would clash with physical items and traders holding stock). Special cases like one-off story items are handled by quest logic.

### Crystals, gear and outfits

- **The only thing a character equips is a Class Crystal.**
  - A crystal **references** a class entry, a loadout, a gear set, an outfit set and appearance data, stored separately from the crystal (gear sets may end up as part of the loadout).
  - **Crystals go in the base inventory, never in bags:** they don't hold items, but they work like bags themselves.
  - Each crystal takes one inventory slot, so carrying many slowly fills the inventory (gentle pressure to specialise). Storage holds any number, limited only by crystal cost.
- **Gear sets:** items in a gear set technically live in Storage, but the UI and lore present them as being "in" the crystal.
  - **Gear can be equipped in the field**, with no cast. Under the hood it's a swap: the new item moves from the inventory into Storage, the old item moves from Storage into the inventory, and the equipped set's reference is updated. Other sets referencing the old item read that slot as empty until it's back in Storage.
  - Only the **equipped** crystal's gear can be edited in the field.
  - **No gear changes in combat**, either directly or onto a crystal. Switching crystals in combat (with a cast) is still allowed and swaps class, loadout, gear and outfit together.
  - **Gear and outfit sets can have empty slots.**
  - **Unique-equipped is universal:** a set can never hold two items of the same type (e.g. two copies of one ring), matching how a duplicate Rune can't be slotted. Checked per set.
  - **The prologue's unclassed gear moves automatically** to the first real crystal when it's unlocked.
  - **One item can be in several gear sets.** Lore: class crystals resonate and share the same physical gear piece. Double equipping is impossible, since only one crystal is equipped.
  - **Slots read as empty; references are never cleared** when an item leaves Storage (withdrawn by the owner, swapped out in the field, lent, dropped). When the item returns to Storage (deposited or recalled), every set that references it has it again automatically.
  - **Effective state** (what you're actually wearing) is derived from the equipped sets' references plus whether each item is currently in Storage. The same check covers lent soulbound gear returning to its owner.
- **Outfit sets work the same way as gear sets.** An outfit is the actual gear piece worn over the top (like a cosmetic slot in other games), not a copied "glamour".
  - If the same item is in the same slot of both the equipped gear set and the outfit set, the outfit slot counts as empty and the item just shows in the gear slot.
  - **Mastery builds up on an outfit item's World Runes** (they're active while worn as an outfit).
- **Crafting and gathering tools:** each gear set has **3 tool slots**. Never enough for every craft, so crystals set up for crafting or gathering still specialise. Not every craft has an associated class, and some crafts may be about forcing an unsuited class to work for it (a fun challenge).
- **Wear and repair:** gear, tools and outfit pieces wear out with use and can be repaired (by crafters or NPCs). Outfit pieces wear out more slowly than gear. Furniture doesn't wear.
  - At zero durability an item keeps working at **reduced stats** until repaired; it's never destroyed by wear.
  - **Borrowers can repair lent soulbound gear** (repair restores rather than destroys or transforms).
- **Dyes:** once a colour is learned, applying it is free. The colour is stored on the item.
- **Crafter signatures are optional:** crafters choose whether to sign each item with their name.

### Soulbound items

- **Soulbound items** exist to make items feel physical: every item is "real" and can be handed around.
  - Soulbound items can be dropped, held by other players and handed back, and recalled by the owner.
  - Another player holding your soulbound Class Crystal can't equip it, but can carry it and give it back.
  - Soulbound gear can be lent out freely, and other players can equip it. Once it returns to the owner, it counts as empty in the borrower's gear sets.
  - **Only the owner can do anything destructive or transforming** to a soulbound item: sell, trade away, dismantle, use as a crafting input, upgrade, extract its Rune, or consume it. Others can wear (gear), carry, store, drop and hand on soulbound items. Such items are nearly useless to hand around; the rule exists to keep items physical and the rules clear.
  - **Handing soulbound items over:** the trade UI can be used, but the owner can never receive anything in return: a trade with soulbound items is always only soulbound items on one side and nothing on the other. Dropping and picking up also works.
  - **Picked-up soulbound items are clearly marked** as belonging to someone else and say they will return to their owner eventually.
  - **Finite rune stones use the soulbound rule:** the first copy a character receives is soulbound to them; later copies are ordinary items that can be traded, broken down or sold to NPCs.
- **Recall:** the owner can recall a soulbound item at any time, for free, wherever it is (held by another player or in a dropped bag). **Recall happens at recall stations,** their own kind of station. **The Crystal Archives include Storage and recall,** making them the large, combined station.
- **Soulbound items also return on their own** 24 hours after leaving the owner's possession, to the same place recalls deliver to. Picked-up soulbound items say so.
- **No lost state:** a soulbound item that despawns (or is on the ground at a channel restart) returns to its owner immediately, through the same return mechanic.
- **Re-obtaining** applies only to destroyed items of untradeable types, and only if you don't currently have a soulbound copy of that type. It's done at an NPC for a coin fee (a small sink).

### Tradeability

- **Tradeability is a three-tier content setting on each item type:**
  - **Market** (default): tradeable and listable on the market.
  - **Direct:** player-to-player trades only, never on the market (e.g. quest drops from monsters).
  - **Untradeable:** every instance is always soulbound to whichever player receives it.
- **Soulbound is a separate per-instance state.** The Untradeable tier always sets it, but soulbound items are not implicitly untradeable. For example, finite rune stones are a Market type even though your first copy is soulbound.

### Items vs flags

- **Things are items; knowledge is a flag:**
  - **Items:** gear and outfits (actual pieces), materials, consumables, Class Crystals, rune stones, bags, furniture, currency.
  - **Learned flags** (character-wide unlocks): e.g. hairstyles, dyes, recipes, Rune unlocks, emotes, titles, achievements, attunements. Usually unlocked by consuming an item, which stays tradeable until someone learns it.
  - **Companions** are their own system (`companions.md`). **Companion items** (eggs, capture items) are ordinary items until used or registered, when they become a companion record.
  - Outfits stay real items, partly because unlocked outfits would become free, permanent sources of World Runes.
- **Three separate data concepts:** items, learned flags, and an **item collection log** (records item types a character has obtained). The collection UI can show flags and the log together.
  - **The item collection log gives no mechanical benefit** (it's for showing off), while flags are real unlocks. Exception: **some recipes are learned from the log** (first obtaining an item grants the recipe flag).
  - **One entry per item type,** set the first time you obtain it (quality and dye variants don't count separately).
- **Collection goals:** showing off, using things (outfits, hairstyles…) and protection against losing things. Protection can be dropped if it conflicts with the economy.
- **Housing furniture is items**, and goes in the item collection log. **Placed in a house** is an item location alongside inventory, Storage, bag and ground.
- **Story items:** when the story needs an item to make sense (a letter to deliver), it's a real item, for physicality: you have to make space for the letter. Plenty of quest progress is simply flags.
  - Most story items are untradeable (always soulbound).
  - Quest drops from monsters may be ordinary, non-soulbound items, but can't be listed on the market: **direct player trades only**.
- **Keys:** mixed. Keys you hand around are items, but a lot of access becomes a flag once unlocked (a dungeon door might need a literal key item to unlock it once, and is then flagged).
  - **Instance items:** inside a dungeon instance, items can unlock doors; they're disposed of when used or when the dungeon ends. Redoing the dungeon resets everything.
- **Music and decorative collectables** (like FFXIV orchestrion rolls) are found as items and consumed to learn them as flags, like dyes.

### Currency

- **Currency is items, with a coin purse.** All currencies are simply currencies: gold has no special status over other currency types.
  - Currencies are stackable items, marked as currencies in their content data. They can be dropped and picked up like anything else.
  - A **coin purse** is an ordinary limited-variety bag that holds every currency type in one inventory slot.
  - A currency UI in both the inventory and Storage shows totals.
  - Spending (shops, repairs, the market) takes from the inventory and purse, and from Storage wherever Storage is available.
- **Untradeable currencies exist and never leave their owner:** they can't be dropped, handed over or lent, which avoids splitting and recalling stacks. They live in the inventory, bags (the coin purse is just a bag) or Storage.

## Considering

- **Storage in tents or camps:** maybe, perhaps only for higher-tier camps.
- **Tool slot count:** 3 for now, open to reconsideration (e.g. if tool Runes make extra slots too strong).
- **Crystal borrowing (on hold).** Unique, but removed for simplicity. Bring it back if a feature this unusual becomes worth the complexity. If it returns, use the **lockout** rule:
  - A borrower uses the owner's class level and skill setup (they can rearrange hotbar/controller bindings), never earns XP for the owner, and all XP they earn goes to their own Soul XP. Borrowing a friend's crystal to farm Soul XP was a welcome trick.
  - The borrower gets the crystal's gear. While the crystal is lent, its items count as "lent out" and the owner's other gear and outfit sets show those slots as empty.
  - "Classes you have" for flex Skills would still mean crystals soulbound to you wherever they are; lending must never break the owner's loadouts, and a borrower can't take its Skills into their own crystals.
  - Rejected variant: letting owner and borrower both use the same item (effectively a free copy of the gear, and double mastery on one item).
- **Active vs inactive mastery:** any worn item's Runes build mastery at the full rate while active and more slowly while inactive. This covers an outfit item's non-World Runes, and also gear's World Runes while an outfit's World Runes override them.
- **Mastery per Rune:** since gear can have multiple Runes, track mastery per Rune on the item. Extracting one Rune resets only that Rune's mastery.
- **Selling or trading a referenced item:** warn first. Nothing auto-equips from the inventory.
- **Lent soulbound gear:**
  - Mastery builds on the item while a borrower wears it (mastery belongs to the item), so the owner benefits.
  - A recall of gear the borrower has equipped takes effect immediately; the slot reads as empty, like any other item that has left Storage. (Alternative: delay until the borrower leaves combat or the instance.)

## Rejected

- **Upgradable inventory** for players.
- **A separate equipment layer:** the crystal is the only equipped thing.
- **Copied glamours:** outfits are real gear pieces.
- **Unlocked (flag) outfits:** would become free, permanent sources of World Runes.
- **Soulbound bags.**
- **"Unique" items limited to one per character.**
- **A separate "tokens" concept:** all currencies are just currencies.

## Open

- Nothing open right now.
