# Comparable games: research pass

**Layer: game (Anima), with Comet notes where a game's tech is relevant.**

Agent research, written 2026-10-06, so the project lead can decide which games belong in the proposal. These are findings and suggestions, not decisions. The proposal's current comparables (Old School RuneScape, FFXIV, Classic WoW, Rabbit and Steel) and art inspirations (PSO, Crystal Chronicles, Signalis) aren't repeated here.

Each entry gives status, parallels to our design notes, lessons, and a suggested use: **comparable** (named in the proposal as a game like ours), **inspiration** (named for one system), or **background** (useful to know, not worth naming).

## Summary

| Game | Status (Oct 2026) | Closest parallel | Suggested use |
| --- | --- | --- | --- |
| Albion Online | Live, ~8–10k concurrent on Steam alone | Player-made economy, one world per region, Unity + C# servers | Comparable (decided) |
| Brighter Shores | Early access, ~150 concurrent on Steam | Small-team old-school MMO, every profession on one character | Background (cautionary) |
| Mabinogi | Live since 2004; UE5 remake (Eternity) in alpha | Life-skill sandbox, any skill on one character, music and roleplay | Not a comparable (decided) |
| TERA | PC closed 2022 | Action-combat MMO without tab targeting | Inspiration (combat), cautionary |
| Blue Protocol | Closed Jan 2025; revived as *Star Resonance* (global Oct 2025) | Anime action MMO, free class changes, skills from monsters | Background |
| Monster Hunter | Live series | Fight for materials and craft better gear; learning distinct classes; boss telegraphs (project lead's reasons) | Inspiration (gear loop, combat) |
| Path of Exile | Live | A reference ARPG (project lead's reason) | Inspiration (ARPG side of combat) |
| V Rising | Live | An RPG with a crafting and boss-killing loop (project lead's reason) | Inspiration (core loop) |
| Breath of the Wild | Released | World traversal and aesthetic (project lead's reason) | Inspiration (traversal, aesthetic) |
| Infinity Nikki | Live, two-player co-op since 2025 | Platforming, in-world collectables, cute and functional outfits (project lead's reasons) | Inspiration (traversal, exploration, outfits) |

**Albion Online** is now a comparable alongside the current four; Mabinogi isn't. My suggestion for the rest: fold the inspirations into the pages they inform (Combat, Classes and Skills, Items and Economy, Self-Hosting, Business and Audience) rather than listing them all on the front page.

## Albion Online

Sandbox Interactive (Berlin), 2017; free-to-play since 2019, with optional premium. PC, Mac, Linux and mobile on the same servers. One world per region (Americas, Asia, Europe), each a single shard. Unity client; servers in C# on Photon (see `backend.md`, research).

**Parallels:**
- Almost every item is player-crafted and gathered, and markets are local to each city: the closest live example of our "every item is real" pillar and our even split between community and developer content.
- One shard per region, seamless-feeling zones with loading between them (we go further: no loading screens).
- The only C# server stack we found proven at MMO scale, on a Unity client: evidence for our stack choice.
- Premium can be bought with in-game currency through an official gold exchange, much like our bonds.
- Classless: gear defines what you can do, and you can switch builds by changing gear.

**Lessons:**
- **Albion's main item sink is full-loot PvP death.** Gear is destroyed when players die in dangerous zones. We're PvE only, so our economy needs sinks that do the same job: wear and repair, dropping and despawning, crafting consumption (all already in `proposal.md` and `items.md`). Worth stating plainly in the proposal that we've thought about it.
- Concurrency has fallen from a peak of ~27k on Steam (April 2024) to ~8–10k in 2026, though many players use the standalone client or mobile, which Steam doesn't count.

**Decided:** comparable, for the economy and the tech stack.

## Brighter Shores

Fen Research (Andrew Gower, co-creator of RuneScape), early access on Steam since November 2024. Free to play with optional premium.

**Parallels:**
- A small team making an old-school MMO with a big cast of professions on one character.
- Episodic regions, each with its own professions.

**Lessons:**
- Peaked at ~21k concurrent at launch and fell to ~150 on Steam by September 2026 (over 99% down). The RuneScape pedigree brought players in but didn't keep them.
- **Combat was the weak point:** in 2026 the developers announced a combat overhaul, with multi-combat, AoE and special attacks, and a single global combat profession replacing per-episode ones. Profession progress split by region felt bad; this supports our choice to keep every class's progress forever in the Crystal Archives.
- Player trading and transmog came late; both are core to us from the start.

**Suggested use:** background, and a cautionary example if the team asks "why not just make old-school RuneScape again?"

## Mabinogi

Nexon (devCAT), Korea 2004; still live. *Mabinogi Eternity* moves it to Unreal Engine 5 and held a Korean alpha test in September 2026.

**Parallels:**
- **Any skill on one character:** classless, with every combat and life skill learnable, and talents for focus. The nearest older example of our "Be anything" pillar.
- A huge range of life skills (crafting, cooking, gathering, music composition and performance), strong roleplay and fashion culture, and a cute, stylised look: our community-driven half of the game.
- Rebirth (resetting age and level while keeping skills) as long-term progression sideways, like our Soul XP unlocks.

**Lessons:**
- Shows a life-sim MMO can last over twenty years on a loyal community.
- Its combat is simple and slow by modern standards; we want the life-sim breadth with better combat.
- Heavy monetisation (gacha boxes) is a common complaint; our no-paid-randomness rule is a contrast worth pointing out.

**Decided:** not a comparable. Agent note: it can still serve as background for life skills and community play.

## TERA

Bluehole (now Krafton), 2011; PC servers closed June 2022, with console versions continuing at the time. Private servers sprang up afterwards; the best-known, Menma's TERA, has since shut down, apparently under legal pressure.

**Parallels:**
- The best-known Western action-combat MMO without tab targeting: aimed attacks, dodges with invulnerability frames, big telegraphed boss mechanics. Our combat sits close to this.

**Lessons:**
- Combat was widely praised and kept a devoted audience; the game was let down by its endgame grind, monetisation and an aging engine, not its combat.
- Its afterlife on private servers, and their legal trouble, is an argument for our decision to make private servers official and easy to host.

**Suggested use:** inspiration for combat; a short cautionary note on what killed it.

## Blue Protocol and Star Resonance

*Blue Protocol*: Bandai Namco, Japan 2023, closed January 2025; its Western release (with Amazon) was cancelled. *Blue Protocol: Star Resonance*, by Bokura, launched worldwide in October 2025 on PC and mobile.

**Parallels:**
- Anime-styled action MMO with free class changes on one character (close to our crystals).
- **Imagines:** abilities gained from defeated monsters and equipped as extra skills, a cousin of our Runes and rune stones.

**Lessons:**
- The original closed within two years despite strong anticipation: thin endgame, and a launch confined to Japan.

**Suggested use:** background. It's a useful example for Runes if the team knows it, but it isn't a good game to compare ourselves to publicly.

## Monster Hunter

Capcom, since 2004; *Wilds* (2025) has seamless maps. *Monster Hunter Frontier* was a Japan-only MMO (2007–2019).

**Why it's on the list (project lead):**
- **Fight for materials, craft better gear:** beat a boss, monster or dungeon, get its materials, and craft better gear from them, rather than gear dropping directly. For us this inspires the crafting side: gear still comes from a mix of crafting and drops (`proposal.md`).
- **Action combat about learning distinct classes:** Monster Hunter's weapon types are basically classes, each with its own moveset to master.
- **Boss telegraphs.**

**Parallels (agent notes):**
- The gear loop ties combat to the crafting and trading half of the game: hunters need crafters (or co-crafting) and crafters need hunters' materials.
- Distinct movesets per class fit our core kits, where each class plays very differently.
- Real hitboxes and commitment through frame data, the combat feel in `combat.md`. Our forgiving latency and simpler controls make it a reference for feel, not difficulty.

**Suggested use:** inspiration for the gear loop (Items and Economy, Crafting and Housing) and combat.

## Path of Exile

Grinding Gear Games, 2013; *Path of Exile 2* in early access since December 2024.

**Why it's on the list (project lead):** a reference ARPG, for the "a little like an ARPG" half of our combat feel.

**Parallels (agent notes):**
- Skill gems socketed into gear, with support gems that modify them, are a well-known cousin of our Runes and gear Runes; gems level with use, like our mastery.
- Currency is items (orbs that double as crafting consumables), with a player-run economy.
- Fresh leagues every few months keep its economy healthy; we have no wipes, so long-term sinks matter more (see Albion).

**Suggested use:** reference for the ARPG side of combat (Combat).


## V Rising

Stunlock Studios, 2024. Built in Unity.

**Why it's on the list (project lead):** an RPG built on a crafting and boss-killing loop.

**Parallels (agent notes):**
- Each boss kill unlocks new recipes and abilities, which lead to better gear for the next boss: a tight version of our two intertwined loops (adventure and life, `proposal.md`) and close to the Monster Hunter gear loop.
- Action combat with aimed abilities and dodges.
- Players can host dedicated servers easily, with many settings to tune, as we want for private servers; its servers hold dozens of players, not hundreds.

**Suggested use:** inspiration for the core loop (Core Loop and Endgame); a secondary example for Self-Hosting.

## Breath of the Wild

Nintendo, 2017.

**Why it's on the list (project lead):** world traversal and aesthetic.

**Parallels:**
- **Traversal:** climbing and gliding let players go anywhere they can see, with landmarks that pull them across a seamless world. That supports our natural travel, platforming and Anima-priced teleports.
- **Aesthetic:** soft, painterly colour over simple, readable shapes; a stylised look that ages well, close in spirit to our low-poly direction (`art.md`).
- **Gear interacts with the environment:** clothing changes how you handle things like temperature (cold and heat resistance), as our World Runes on gear do.
- Physicality and systemic interactions, like our "physicality without physics" goal (agent note).

**Suggested use:** inspiration for traversal (World and Travel) and the look (Art and Audio).

## Infinity Nikki

Infold, December 2024; PC, PlayStation and mobile. Real-time two-player co-op since April 2025.

**Why it's on the list (project lead):** platforming; collectables placed in the world; outfits that are cute and functional.

**Parallels (agent notes):**
- Light platforming in an open world, alongside our platforming elements (`world.md`).
- Collectables scattered through the world (Whimstars and others) reward exploring off the path, which supports natural travel over teleports.
- Outfits that grant world abilities (fishing, bug catching, floating), like our World Runes on outfits; a fashion-centred audience. Funded by gacha for outfits, which we've ruled out.

**Suggested use:** inspiration for traversal and exploration (World and Travel) and outfits (Art and Audio, Items and Economy).

## Sources

- Albion Online concurrency: [Steambase](https://steambase.io/apps/761890), [Mein-MMO on regional servers](https://mein-mmo.de/en/albion-online-erfolgreicher-start-nach-free2play,338077)
- Brighter Shores: [Wikipedia](https://en.wikipedia.org/wiki/Brighter_Shores), [Steambase](https://steambase.io/apps/2791440), [Massively OP on the combat overhaul (Aug 2026)](https://massivelyop.com/2026/08/06/brighter-shores-lead-dev-andrew-gower-talks-with-players-in-game-about-combat-update-plans/), [Icy Veins on the global combat profession](https://www.icy-veins.com/brighter-shores/news/major-combat-profession-changes-coming-to-brighter-shores)
- Mabinogi Eternity: [4Gamer (alpha test, July 2026)](https://www.4gamer.net/games/007/G000776/20260724030/), [Massively OP dev update](https://massivelyop.com/2025/11/10/mabinogi-eternity-shows-off-its-improved-visuals-and-gameplay-in-a-development-update)
- TERA: [Wikipedia](https://en.wikipedia.org/wiki/TERA_(video_game)), [MMOBomb on the PC shutdown](https://www.mmobomb.com/news/we-wish-tera-online-fond-farewell-their-servers-shut-down-today)
- Blue Protocol: Star Resonance: [Wikipedia](https://en.wikipedia.org/wiki/Blue_Protocol:_Star_Resonance)
- Infinity Nikki co-op: [Push Square](https://www.pushsquare.com/news/2025/04/free-ps5-platformer-infinity-nikki-adds-online-co-op-in-humongous-update)
- Monster Hunter, Path of Exile, V Rising and Breath of the Wild are from general knowledge of the games, not checked against sources in this pass.
