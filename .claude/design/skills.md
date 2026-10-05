# Skills, Abilities and Runes

**Layer: mixed** (project lead, 2026-10-04). Base (Comet): a thin skill base that only executes actions (frame data, hitboxes, cooldowns, effects; see `proposal.md`), not necessarily slots, so another game can build more traditional MMO skill advancement. Acquisition, slotting and advancement are game code. Game (Project Anima): everything else here, including the two action tiers, core kit and flex slots, coloured Rune slots, Runes, rune stones, gear Runes and extraction, Outfit Magic, Variant and Unbound Runes.

Agent notes on the skill system. Decisions made with the project lead on 2026-10-03. See `glossary.md` for terms.

## Decided

- **Working terminology** (may change, but must stay distinct): **Skills** are primary actions; **Abilities** are all secondary actions, whatever their source (class, character-wide, Runes). Abilities are generally ill-suited to battle but not always (re-casting a one-hour buff mid-fight), and players can optionally bind them; **Runes** are slotted skills; **rune stones** are Runes as items.
- **Two tiers of actions:**
  - **Primary:** a small set used for normal rotations and combat, with proper default bindings on controller and keyboard.
  - **Secondary (Abilities):** a large selection used less often but doing cool things (long-term buffs, teleports, transformations), provided by classes, Runes, the base system and character-wide unlocks. They don't need to fit the primary control scheme, but can optionally go on hotbars. (Rough idea: a context menu on controller.)
- **Core kit + flex slots** for primary actions: each class has a designer-made core kit, plus a few flex slots. Easy to understand; complexity can be added on top later.
- **About 10 primary slots in total, including flex** (a goal: lower it if no good controller scheme is found). Exact bindings are deferred.
- **Jump, crouch, dodge and sprint** have dedicated controls but are class skills: each class can change their effect (dodge timing, jump height for a dragoon-style class, a rogue's crouch doubling as a faster sneak) while they always remain jump, crouch, dodge and sprint.
- **Runes and slots:** a **Rune** is always a slotted skill (genuine passives, modifiers to primary actions, and skills that add secondary actions). Their containers are just called **slots**. A Rune held as an item is a **rune stone**. Runes must stay distinct from primary actions.
- **Coloured slots replace SP** for Runes:
  - **All slots are the same size** (major/minor sizes dropped to limit complexity). Designers can balance powerful Runes in other ways, e.g. requiring a base Rune.
  - Colours: **Offense** and **Support** (Battle split in two, so hybrid classes show in their slot layout), and **World**. Crafting falls under World, so a class's World slots decide how good a crafter it is.
  - Support sits "in the middle": battle first, but some Support skills (e.g. a party speed buff) are also useful for exploration.
  - World stays one colour for now: splitting off an "Industry" colour would waste slots for players who don't craft. The total number of colours should stay small.
- **Learning Skills:** from levelling and class tutors (tutors should be hard to miss). Some non-class Skills come from special tutors, items or quest rewards.
- **What goes in flex slots:**
  - Some classes have extra optional class Skills for their flex slots.
  - Most flex Skills come from other classes: a character can use the untyped core Skills and optional flex Skills of any other class they have, as long as they can equip the required weapon.
  - The system can explicitly ban specific Skills for specific classes, but rarely (only for broken builds).
  - "Classes you have" means classes with a crystal **soulbound to you**, wherever it is (Storage, or carried by another player), not every unlocked class.
  - Destroying a crystal whose Skills are used by other loadouts gives a warning, then removes those Skills: a locked core slot resets to its base Skill and its Unbound Rune is removed; a flex slot is simply emptied (empty slots are always allowed).
- **Rune stones are tradeable items.** Finite rune stones (only from major quests or fixed loot): your first copy is soulbound to you (see soulbound rules in `items.md`), even if more than one can be obtained. Duplicates (anything but your first finite instance) are ordinary items that can be traded, broken down into materials, or sold to NPCs.
  - Every extra rune stone, including ones extracted from gear, must be able to leave the economy: broken down into materials or sold to NPCs.
- **Gear Runes:** the system supports gear with multiple Runes, though not every piece will use it. Runes granted by worn gear (the equipped crystal's gear set) are free (they don't use slots). You can't slot a duplicate of a Rune your worn gear currently grants.
  - **Extraction** works like FFXIV materia extraction, but yields the gear's specific (complex, fixed) Rune rather than a simple stat materia. It's non-destructive (the gear keeps its Rune) and only possible after the gear is worn enough. Mastery progress belongs to the item, not the character. Extracting produces a tradeable rune stone and resets the item's mastery, so it can be repeated after wearing the gear in again.
- **Outfit Magic** (the actual gear piece worn over the top, not a copied glamour; see `items.md`) always uses the outfit's **World** Runes instead of the underlying gear's World Runes. Gear is chosen for battle; the outfit carries world utility (e.g. temperature regulation). This means active World Runes from gear always match the character's visible appearance.
  - Any gear can be worn as equipment or used as an outfit; nothing prevents either. Some gear is mainly an outfit (weak stats, good World Runes), some mainly equipment (no World Runes), and some is good for both: you might skip an outfit to keep its World Runes, or use it as an outfit once its battle stats are outclassed.
- **Variant and Unbound Runes** customise the core kit:
  - **Variant Runes** swap a core action for a designer-made alternative; the slot stays locked.
  - **Unbound Runes** (rare, hard to get) unlock a core slot so any allowed action can go there; one per core slot type. They're alternatives to the (often better for the class) Variant for that slot; using both costs two slots.
  - Some core slots may be **untyped**. There is only one untyped Unbound Rune per player, so at most one untyped core slot can be unbound.
  - No extra limit for now. A player unbinding most of their core kit to play like Freelancer (which generally has fewer Battle rune slots) is fine. Add a limit later if the game becomes a mess.
- **The shape-based grid idea is dropped.**
- **Primary actions are shown as another colour of rune slot** (project lead's idea, adopted 2026-10-05): each tied to a control, with core slots shown as locked to the class. Primary slots share one colour of their own; classes don't get their own slot colours. Skills and Runes stay distinct concepts underneath.

## Considering

- **Skill slots may depend on class level** rather than being unlocked separately.

## Open

- Core slot types (agent idea: Basic, Skill, Burst, Defensive, Mobility). Some slots stay untyped. Kept open on purpose (2026-10-05): decide with the first class designs.
