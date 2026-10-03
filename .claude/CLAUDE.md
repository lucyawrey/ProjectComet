# Project Comet: agent context

Handoff notes for agent sessions on Project Comet. Last updated 2026-10-03.

## Working agreement

- **Discuss before acting.** Propose a plan and wait for agreement before writing docs, pushing, or changing branches. Don't fill in content or structure that hasn't been discussed.
- **Don't present agent suggestions as decisions.** An earlier session's context mixed agent suggestions in with the project lead's decisions. Keep three categories apart: *decided*, *considering*, and *open*.
- **Human-readable docs are the goal of this phase.** Design docs are written for people. Agent-oriented material (like this file) lives under `.claude/`.

## What this phase is

Planning only. Multiple characters per account will exist, but this design phase ignores multi-character flows for simplicity. The project lead is drafting plans for fun, and eventually to present to a small indie team they've joined. No hiring, no development yet.

## Project lead

Experienced web developer with shipped production apps; strong in deployments, orchestration and database design. Hobbyist game developer with little experience in real-time game networking. C# / .NET is the only language they're confident maintaining a critical backend in.

## Decided

- **Human-made creative content only.** Agents help with code; no AI-generated models, textures, readable in-game text, music or sound.
- **Low-poly art.**
- **Content is roughly an even split** between community-driven play (economy, crafting, trading, housing, guilds, player events) and developer-made content. Smaller in scope than an MMO from a larger team.
- **Action combat, not tab-target.** Skills have real hitboxes; there's an optional lock-on. Should be more forgiving of latency than an FPS. (Detailed design in the project lead's notes: 2D hitboxes with "height zones".)
- **Seamless world with no loading screens.** A firm requirement and the project's biggest challenge. Zone shards and dungeon instances still exist on the server side; only teleportation hides loading.
- **Private servers should be easy to self-host**, without a large proprietary dependency.
- **First milestone (adopted from an agent suggestion):** a vertical slice with one zone, the core loop, bot clients load-testing 100+ simulated players, and simulated latency from day one.

### Combat (decided 2026-10-03)

- **PvE only for the first major version.** No PvP system is planned, so no PvP netcode.
- **Every class can dodge, but invulnerability varies by class and skill.** Defenders have invulnerability levels (none / dodge / a rarer "true" invuln); attacks have pierce levels, and some attacks hit through normal invuln.
- **Height zones are separate from invulnerability.** Zones (digging, crouching, standing, jumping, flying) are vertical position; invulnerability is defensive state. Both feed one shared "is this hit valid?" check.
- **Real height check:** height thresholds between attacker and defender add or subtract an integer offset to the zone check. Can be disabled for flat areas like boss arenas as an optimization.
- **Frame data in 30 Hz simulation ticks:** explicit startup, active and recovery frames, run in parallel with (not driven by) animations. Consistent timing matters more than visual match.
- **Fight sizes:** dedicated raids up to ~30 players; open-world bosses aim for ~100 in the worst case (revisit if not feasible).
- **No body blocking** in the initial design. "Nudging" (moving slower through other bodies) is under consideration.
- **Most attacks don't move anyone**, to keep some tab-target feel; a limited set of skills include movement.
- **Physicality without physics** is one of the game's largest goals, despite the simple graphics.

### World structure (decided 2026-10-03)

- **Zone borders: chokepoint handoff with a little overlap.** Borders can be wide (e.g. a valley between mountains) but each border joins exactly two zones; if more meet by accident, only the closest transition is synced. Near a border, players see a read-only view of the neighbouring zone.
- **No effects across borders:** attacks, AoEs, heals and buffs only affect entities on the same server.
- **Monsters stay in their zone;** border areas are designed with little or no combat.
- **Many zones are reached only by teleport or a single door** (e.g. dungeon entrances) and need no border sync.
- **Flying can cross zone borders.** Little flying-to-ground interaction and no aerial combat; far-away players are culled.
- **Hierarchy:** Region › Zone › Shard, and Region › Dungeon › Instance. **Worlds** are labels inside a region: they decide default shard placement and organize guilds. Several Worlds share shards in a region. (A private server would be one region.)
- **Shard preference when entering a zone:** party's shard, then home World's shard, then any shard with room.
- **Shard size:** 150–300 players would be an impressive upper limit; may be forced lower.
- **Vehicles:** boats and airships cross zones on fixed routes. The only player-steered vehicles are 2–4 seat mounts with near-normal movement. Vehicles and their riders cross borders as one group.

### Classes and Class Crystals (decided 2026-10-03)

- **A Class Crystal is a key plus a loadout.** It holds no class data itself: it grants permission to use one of its soulbound owner's permanent class entries, and contains a gear set, outfit and appearance. Several crystals can point to the same class (different loadouts). Separate Gear, Outfit and Appearance Crystals are dropped.
- **One shared container system** for bags, Class Crystals and Storage.
- **Crystals can be traded or lent.** The class level always belongs to the soulbound owner. A borrower uses the owner's class level and the owner's skill setup (they can rearrange hotbar/controller bindings), never earns XP for the owner, and all XP they earn goes to their own Soul XP. Borrowing a friend's crystal as a way to farm Soul XP is a welcome trick.
- **A crystal is always equipped.** The tutorial's "unclassed" state is a hidden crystal with its own gear set.
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
- **Swapping between two crystals of the same class** plays the class-change animation but is by definition a gear swap. It may also swap the skill loadout.
- **Class loadouts** (skill setups) are separate per-character saved objects; a player can keep any number. A crystal is assigned one when gear is added. The UI can hide this and let players edit "the crystal's loadout" directly, but it still saves to a separate loadout that survives the crystal's deletion.
- **Appearance in crystals:** besides gear/outfit looks, a crystal can change the character's base design (body, features) on top of their real base form, to emphasise the transformation.
- **Swap casts can be interrupted.**
- **Promotion tree:** the system allows a class to have multiple parents, even if class design never uses it.
- **XP:** all XP goes to the equipped class. Overflow from a maxed class or a maxed craft becomes Soul XP. An unlockable toggle may send XP to Soul instead of a non-maxed class. No percentage splits; Freelancer no longer takes an automatic share.
- **Names:** Soul Experience grows your soul in size and power. "Class promotion" for classes; "Soul Ascension" for soul milestones (which may upgrade Anima Capacity and skill slots together, though they stay separate concepts).
- **Anima** is the magic of your soul: an energy resource gained over time and from rare items and quests, never purchasable with real money. Anima Capacity is how much you can store. It doesn't discourage playing; it encourages playing in certain ways by centralising gates MMOs already have:
  - Teleports (to encourage natural travel). An emergency teleport to the nearest safe zone works even at zero Anima.
  - Crystals (to encourage specialisation).
  - Entry to certain content, like high-end raids that shouldn't be cleared too quickly. Charged immediately on entry (animated as using magic to enter) and refunded if you don't clear. Clears should reliably give loot, with little RNG. Anima-locked content only gives loot at the end; other content can drop loot anywhere.
  - Maybe crafting certain endgame items that shouldn't flood the market.
- **Outfit dispelling by other players is dropped.**

### Skills (decided 2026-10-03)

- **Working terminology** (may change, but must stay distinct): **Skills** are primary actions; **Abilities** are all secondary actions, whatever their source (class, character-wide, Runes). Abilities are generally ill-suited to battle but not always (re-casting a one-hour buff mid-fight), and players can optionally bind them; **Runes** are slotted skills; **rune stones** are Runes as items.
- **Two tiers of actions:**
  - **Primary:** a small set used for normal rotations and combat, with proper default bindings on controller and keyboard.
  - **Secondary:** used less often but do cool things (long-term buffs, teleports, transformations). On controller, a context menu (hold a button, pick with the D-pad). On keyboard, players can optionally put both tiers on one hotbar.
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
  - "Classes you have" means classes with a crystal **soulbound to you**, wherever it is (Storage, lent out, traded), not every unlocked class. Lending a crystal never breaks your loadouts, and a borrower can't take its Skills into their own crystals.
  - Destroying a crystal whose Skills are used by other loadouts gives a warning, then removes those Skills: a locked core slot resets to its base Skill and its Unbound Rune is removed; a flex slot is simply emptied (empty slots are always allowed).
- **Rune stones are tradeable items.** Finite rune stones (only from major quests or fixed loot) never let you trade away your first one, even if more than one can be obtained. Duplicates (anything but your first finite instance) can be traded, broken down into materials, or sold to NPCs.
- **Gear Runes:** Runes granted by worn gear are free (they don't use slots). Gear Runes can be extracted as rune stones, but you can't slot a duplicate of a Rune your worn gear currently grants.
- **Outfit Magic** (similar to FFXIV glamours) always uses the outfit's **World** Runes instead of the underlying gear's World Runes. Gear is chosen for battle; the outfit carries world utility (e.g. temperature regulation). This means active World Runes from gear always match the character's visible appearance.
  - Any gear can be worn as equipment or used as an outfit; nothing prevents either. Some gear is mainly an outfit (weak stats, good World Runes), some mainly equipment (no World Runes), and some is good for both: you might skip an outfit to keep its World Runes, or use it as an outfit once its battle stats are outclassed.
- **Variant and Unbound Runes** customise the core kit:
  - **Variant Runes** swap a core action for a designer-made alternative; the slot stays locked.
  - **Unbound Runes** (rare, hard to get) unlock a core slot so any allowed action can go there; one per core slot type. They're alternatives to the (often better for the class) Variant for that slot; using both costs two slots.
  - Some core slots may be **untyped**. There is only one untyped Unbound Rune per player, so at most one untyped core slot can be unbound.
  - No extra limit for now. A player unbinding most of their core kit to play like Freelancer (which generally has fewer Battle rune slots) is fine. Add a limit later if the game becomes a mess.
- **The shape-based grid idea is dropped.**

### Netcode (adopted from agent suggestions 2026-10-03)

- **Lenient PvE hit validation:** accept player hits on monsters as seen by the client, within sanity limits; honour dodges that started before a hit landed on the client, up to ~150–200 ms.
- Height zones as bitmasks; the terrain-height offset shifts the mask. Invuln vs pierce compared as integers.
- Sync actions as events ("player X started skill Y at tick T facing Z") rather than continuous state; prioritise updates by relevance; cap displayed characters.
- Movement skills and knockback as fixed curves in frame data, predicted by the client.
- Nudging: server-enforced against monsters/NPCs, client-only between players.
- Border visibility: near a border the client also listens to the neighbouring shard (ticket via the Gateway) but only sends input to the owner; handoff happens a few metres past the line to avoid ping-pong.
- Fixed-route vehicles are positioned from route + clock, so only passengers are handed off; riders' positions are relative to the vehicle.
- A named load test after the 100-bot milestone: one shard, 100 players, one boss.

## Considering

- **Backend:** plan for two approaches:
  - A. A custom .NET stack (Gateway server, Data Center server with PostgreSQL, headless game server, game client).
  - B. A product like SpacetimeDB. The project lead is slightly biased against it because of the self-hosting requirement; verify current license terms before weighing it.
  - C (unlikely). A systems-language backend written with help from another developer, if neither A nor B works.
- **Art techniques:** palette/gradient texturing and a shared humanoid rig (agent suggestions).
- **Class swap cooldowns (agent suggestion):** share cooldowns by action category (dash, burst, big heal) as a fraction, so the new class's skill continues at the same fraction of its own length; add a short swap cooldown; carry HP and resources over as percentages. Client preloads skill data for crystals in the inventory.
- **Class status derived from data (agent suggestion):** Locked = no class entry; Archived = entry but no crystal; Stored = crystal elsewhere (Storage or another character); Attuned = crystal in inventory or equipped.

## Open questions

- **Gateway and Data Center: one service or two?** In the custom .NET approach, should the Gateway server (public-facing, primarily HTTP API) and the Data Center server (database API layer) be the same thing or separate?
- **Are the plans sensible?** Find real-world examples of documented MMO backends (talks, postmortems, open-source servers, engineering blogs) and compare our architecture against them.

### Skills (open)

- Idea (project lead): present primary actions as if they were another colour of rune slot, each tied to a control, with core slots shown as "locked" to the class. Primary slots share one colour of their own; classes don't get their own slot colours. More harmonious UI while keeping the concepts distinct.
- Should the secondary menu be split into character-wide actions (teleports, mounts) and loadout actions? (agent suggestion)
- Core slot types (agent idea: Basic, Skill, Burst, Defensive, Mobility). Some slots stay untyped.

### Classes (open)

- Level cap and ascension level?

## Risks

- **Big:** netcode (sharpened by action combat and the seamless world), moderation, economy integrity.
- **New:** more players than the game can support.
- **Not a current concern:** audio budget (find a composer if this becomes real), low population (solve later; the goal now is to make the thing).

## Source material

The project lead's Obsidian notes (Quick Notes, Class Ideas, Technical Notes, Story Snippets, Conversations) cover game systems in detail: inventory and collection, companions, Class Crystals, Soul Experience, crafts, skills, world structure, aesthetic, server hierarchy, and database tables. Raw copies are in `.claude/notes/`. They are messy working notes, not decisions; check with the project lead before treating anything in them as final.

## Repository state

The three earlier repos were consolidated into `lucyawrey/ProjectComet` as archive branches with full history:

| Branch | Came from |
| --- | --- |
| `archive/dotnet-datacenter` | ProjectComet `main` (Godot + .NET "DataCenter" backend) |
| `archive/nakama` | ProjectComet `nakama` |
| `archive/rust-bevy` | `project_comet_rs` `main` |
| `archive/rust-bevy-sqlite-opfs` | `project_comet_rs` `client-sqlite-opfs` |
| `archive/rust-bevy-spacetimedb` | `project_comet_rs` `spacetimedb` |
| `archive/godot-client` | `project_comet_godot` `main` |

The archive branches are abandoned, but any of them can be mined for ideas: data models, schemas, architecture, networking experiments.

- `main` was reset on 2026-10-03 to a fresh history containing only `.claude/`. The old .NET code and its history live on `archive/dotnet-datacenter`. Human-readable design docs will be added to `main` as they're written.
- No tags are wanted.

## Next steps

1. Go through the notes together, one area at a time. **Combat, world structure, classes and skills are done** (see Decided; some questions remain open). Remaining areas: crafts, items/inventory/collection, companions, unlockables, aesthetic, database tables.
2. Research documented real-world MMO backends to sanity-check the architecture, including the Gateway/Data Center split.
3. Agree on the doc structure, then write human-readable docs.
