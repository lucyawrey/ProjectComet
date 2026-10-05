# Classes, Class Crystals and Anima

**Layer: game (Project Anima).** Class Crystals, Soul XP and Anima sit on the base's progression framework (XP, levels, unlock flags, basic skills).

Agent notes on classes, crystals, XP and Anima. Decisions made with the project lead on 2026-10-03.

## Decided

- **A Class Crystal is a key plus references.** It holds no class data itself: it grants permission to use one of its soulbound owner's permanent class entries, and references a loadout, gear set, outfit set and appearance data, all stored separately (see `items.md`). Several crystals can point to the same class (different loadouts). Separate Gear, Outfit and Appearance Crystals are dropped.
- **The crystal is the only thing a character equips.** Crystals are not containers (this replaces the earlier "one shared container system for bags, Class Crystals and Storage").
- **Crystals are soulbound and only the owner can equip them.** Other players can hold one and hand it back (see soulbound items in `items.md`). **Crystal borrowing is scrapped for now**; its details are kept under Considering in `items.md` in case it returns.
- **A crystal is always equipped.** The tutorial's "unclassed" state (the opening of the prologue, before Freelancer is granted; see `proposal.md`, 2026-10-05) is a hidden crystal with its own gear set; that gear moves automatically to the first real crystal when it's unlocked.
- **No fixed crystal limit.** Instead:
  - Each new crystal for a class you don't already hold costs more Anima (never XP), possibly plus materials like raw crystal (mined, or bought with in-game currency; never plain currency directly). The goal is soft pressure to specialise; doing everything stays possible.
  - Cost is based on what you hold now, **never lifetime totals**.
  - Crystals are flagged bought or granted. Quest-granted crystals (starting class, DLC classes) are free and don't raise the price; claiming a granted crystal again is free.
  - Extra loadout crystals for a class you already hold cost a flat amount.
  - Every character's first Freelancer crystal is granted; extra Freelancer crystals cost a small flat amount.
  - A hard cap on total crystals, higher than the number of classes.
- **Class promotion:** most classes sit in a promotion tree rooted at Freelancer (Freelancer → base → high tier). Promoting converts a crystal for a small fixed resource cost.
  - The first time a class is unlocked by promotion, XP moves from the base class (or fully from Soul XP; designers may revisit). The new class needs a lot of XP to reach level 1, so the base class drops. Promoting into an already unlocked class costs no XP.
  - Promoting a granted crystal produces a bought one.
  - Promoting one crystal while you still hold others of that class charges the rising price (awkward, but worth it for game feel).
- **The Crystal Archives** keep every class entry's current XP and highest level.
- **Class changes** work almost anywhere, in and out of combat. Cooldowns don't reset on swap and need some scaling. Zones, areas or individual encounters can block class changes entirely or only in combat.
- **Swapping between two crystals of the same class** plays the class-change animation but is by definition a gear swap. Switching crystals is the only way to change gear in combat. It may also swap the skill loadout.
- **Class loadouts** (skill setups) are separate per-character saved objects; a player can keep any number. A crystal is assigned one when gear is added. The UI can hide this and let players edit "the crystal's loadout" directly, but it still saves to a separate loadout that survives the crystal's deletion.
- **Appearance in crystals:** besides gear/outfit looks, a crystal can change the character's base design (body, features) on top of their real base form, to emphasise the transformation.
- **Swap casts can be interrupted.**
- **Promotion tree:** the system allows a class to have multiple parents, even if class design never uses it.
- **XP:** all XP goes to the equipped class. Overflow from a maxed class or a maxed craft becomes Soul XP. An unlockable toggle may send XP to Soul instead of a non-maxed class. No percentage splits; Freelancer no longer takes an automatic share.
- **Names:** Soul Experience grows your soul in size and power. "Class promotion" for classes.
- **Soul XP is a spendable currency**, spent on class promotions and other permanent unlocks. **Soul Ascension (soul milestones) is dropped.**
  - **Everything bought with Soul XP is permanent:** a character unlock or a soulbound item, never something that can be consumed, thrown away or traded.
- **Anima** is the magic of your soul: an energy resource gained over time and from rare items and quests, never purchasable with real money. Anima Capacity is how much you can store. It doesn't discourage playing; it encourages playing in certain ways by centralising gates MMOs already have:
  - Teleports (to encourage natural travel). An emergency teleport to the nearest safe zone works even at zero Anima.
  - Crystals (to encourage specialisation).
  - Entry to certain content, like high-end raids that shouldn't be cleared too quickly. Charged immediately on entry (animated as using magic to enter) and refunded if you don't clear. Clears should reliably give loot, with little RNG. Anima-locked content only gives loot at the end; other content can drop loot anywhere.
  - Maybe crafting certain endgame items that shouldn't flood the market.
- **Outfit dispelling by other players is dropped.**
- **Level caps per part; numbers later** (2026-10-05, adopted from an agent suggestion): each part has a cap (e.g. early part ~30, late part ~50); tier 2 classes promote around the early part's cap, and high tiers level to the late cap. Exact numbers come from balancing.
- **Anima builds up offline, up to capacity** (2026-10-05, adopted from an agent suggestion): same rate as online, capped at Anima Capacity, with no overflow.
- **Party members without enough Anima for locked content are warned before entry** (2026-10-05, adopted from an agent suggestion): the party sees who lacks Anima while gathering at the entrance, before anyone is charged.

## Considering

- **Class swap cooldowns (agent suggestion):** share cooldowns by action category (dash, burst, big heal) as a fraction, so the new class's skill continues at the same fraction of its own length; add a short swap cooldown; carry HP and resources over as percentages. Client preloads skill data for crystals in the inventory.
- **Class status derived from data (agent suggestion):** Locked = no class entry; Archived = entry but no crystal; Stored = crystal elsewhere (Storage, or carried by another player); Attuned = crystal in inventory or equipped.

## Open

- Nothing open right now.
