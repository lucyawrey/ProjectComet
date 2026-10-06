# World structure

**Layer: mixed.** Base (Comet): hierarchy, zone borders, flying and fixed-route vehicles, channel placement with a pluggable preference policy, soft and hard caps, draining and merges, instance lifecycle with entry and exit hooks, teleport handoff. Game (Project Anima): constellations and the channel preference order, channel naming, loading rooms, re-entry and replacement rules, no lockouts, the teleport scene and cast rules, leyline theming, Anima costs.

Agent notes on world structure and server hierarchy. Housing access is in `housing.md`; the zone format and handoff mechanics are in `backend.md`.

## Decided

### Structure

- **Hierarchy:** Region › Zone › Channel, and Region › Dungeon › Dungeon instance. The region is the only player-facing "server", and guilds belong to the region. A private server is usually one region (see "deployment" in `glossary.md`).
- **Terminology:** a **zone** is any zone file (kinds: open zone, dungeon, house). An **instance** is any running copy of a zone on a game server: a **channel** (copy of an open zone, many players), a **dungeon instance** (one party) or a **house instance** (one owner; guild halls too).
- **World traversal takes cues from Breath of the Wild** (project lead's reference).
- **Per-player collectables are spread around the world** to improve world interaction: each player finds and collects their own, so nobody takes them from anyone else.
  - **Each is collected once per character.**
  - **Every collectable counts towards a collection,** and each placed instance is logged on its own as a learned flag (`unlockables.md`); a collection counts the flags in its group. Some give nothing else; others also give items, which may be soulbound, or untradeable currencies (`items.md`), chosen per collectable by designers.
  - Example: 100 pieces of a collectable currency spread through a region, spendable only at certain NPCs; each piece found is logged, so the collection shows 37/100 even after the currency is spent.
- **The game has platforming elements.** Walls, travel and platforming use the player's world collider, not the small attack hitbox (`combat.md`).
- **Most areas can be reached without the story,** through less convenient routes (e.g. a very long walk instead of a story carriage ride). Free players also meet in-world barriers at later-part borders (`proposal.md`, business model).

### Borders

- **Zone borders: chokepoint handoff with a little overlap.** Borders can be wide (e.g. a valley between mountains) but each border joins exactly two zones; if more meet by accident, only the closest transition is synced. Near a border, players see a read-only view of the neighbouring zone.
- **No effects across borders:** attacks, AoEs, heals and buffs only affect entities on the same server.
- **Monsters stay in their zone;** border areas are designed with little or no combat.
- **Many zones are reached only by teleport or a single door** (e.g. dungeon entrances) and need no border sync.
- **Flying can cross zone borders.** Little flying-to-ground interaction and no aerial combat; far-away players are culled.
- **Vehicles:** boats and airships cross zones on fixed routes. The only player-steered vehicles are 2–4 seat mounts with near-normal movement. Vehicles and their riders cross borders as one group.

### Channels

- **Channel size:** 150–300 players would be an impressive upper limit; may be forced lower.
- **Channel preference when entering a zone** (Project Anima's policy): party's channel, then primary guild members' and friends' channels, then the player's constellation (its associated channel if it has room, otherwise channels with the most players sharing it), then secondary guild members' channels, then players sharing the same language, then the channel the player was last on, then any channel with room.
- **Constellations:** a loose, in-world grouping, in the spirit of Elden Ring's group passwords. Their only gameplay effect is channel priority; guilds are the player-made groupings.
  - Constellations help players see familiar faces in a big region without commitment (free to change, optional, hideable) or partition (they rank below party, primary guild and friends, so they only break ties among strangers).
  - Each character is in **one official constellation or none**. It's chosen at character creation and can be changed for free at any time.
  - **Other players see your constellation by default;** you can choose to hide it.
  - **Per open zone, each constellation can have at most one associated channel.** It's the preferred channel for that constellation, but anyone can be placed there.
- **Multiple guilds per character:** a character can join more than one guild and chooses a primary guild, which shows on their profile. Primary and secondary guilds count separately in channel placement.
- **Channels are named and visible to players:** the UI shows which channel a player is on. In lore, each is a discrete echo of the same area, a specific reality.
  - **Constellation-associated channels are named after their constellation.**
  - **Players can deliberately move to a named channel** to find someone, e.g. a new player they've just introduced to the game: ask which channel they're on, then go there. Within a zone this is the normal channel move (leyline shimmer, player-move cooldown); in another zone, a normal teleport can target a named channel. Deliberate joins may use the headroom up to the hard cap, like party members.
- **Channel lifecycle:**
  - **Soft and hard caps:** new players are placed on a channel only while it's below a soft cap (~80% of the hard cap, tuned later); the headroom is for party members joining and border crossings.
  - **Channel pairing across borders:** on crossing, prefer the neighbouring zone's channel that most nearby travellers are going to; otherwise use the channel preference rule. Keeps groups of travellers together, as WoW Classic's continent-wide layers did.
  - **Closing a channel by draining:** stop placing new players on it and let it empty naturally (borders, teleports, logouts); move the remaining players only if its population stays low.
  - **Moves between channels** (merges, joining a party member's channel) use the handoff, shown as a short **leyline shimmer** before the new channel fades in. Never in combat or mid-skill; player-triggered moves have a cooldown.
  - **Constellation-associated channels aren't drained aggressively,** but placement can skip a truly dead one: a player entering the zone may be put on a busier channel outside their constellation if their constellation's channel has almost no one on it (threshold tuned later).
  - **Ground items carry over on a merge:** they move to the destination channel at the same position, keeping their despawn timer (one database update). A channel that closes empty with no merge clears its ground items like a restart.
  - **Channel hopping gains little:** gathering nodes are mixed per-player and shared, chosen per node (`crafts.md`; the move cooldown limits hopping for shared ones), and open-world boss rewards are limited to once per player per spawn cycle, whichever channel they fight on.

### Teleports

- **Teleport scene:** a fixed-length scene where the player's soul travels along the leylines on the world map to the destination. If the destination hasn't finished loading when the travel animation ends (likely on web), the soul pulses at the destination on the map until it has. It hides loading; the handoff is in `backend.md`.
- **Teleport casts can be cancelled, and enemies can interrupt them. Teleporting in combat is allowed,** but cast times are long enough that trying it mid-fight will usually get you killed.

### Dungeons

- **Dungeon loading rooms:** the whole party has to be physically present (in the spirit of classic MMOs). Once the whole party is in the loading room and one member steps into the dungeon proper, the loading room's entrance closes and the dungeon starts. Everyone else still walks in on their own.
- **Loading rooms need not look like rooms:** they can appear to be part of the open world (a mountain top, a grove past a narrow gap in the trees). Their static scenery may show up in open-world LODs, but players and other live entities are only synced across the official entrance, never across the rest of the room's edge.
- **Re-entering an instance:** a player who disconnects or leaves can re-enter the same instance.
  - Instances with no player limit: re-entry is allowed any time until the instance no longer exists.
  - Instances with a fixed player limit: the player's place is held, but the party can vote to remove them. Removal is for disconnected players and for troublesome ones alike, and carries no negative connotation. Problematic players are reported through the separate report feature. (Avoid the term "kick".)
  - Someone new can take a removed player's place, entering a started dungeon through the closed entrance, which admits only the holder of the open slot. The party can also stabilise an unstable teleport station inside the dungeon to bring them closer (see Considering).
- **When an instance closes:** if everyone has left through gameplay (walking out of an exit, teleporting away, or clearing the dungeon and leaving), it closes instantly. If any player is missing because of a disconnect, it stays open for a few minutes so they can return. The game server can tell the two apart: leaving is a handoff, a disconnect is a dropped connection.
- **Dungeon exits vary by design:** a separate exit, a tunnel back to the entrance, or a teleport at the end (to the entrance or elsewhere).
- **Leaving a dungeon into an outdoor zone uses the channel preference rule,** preferring the channel the party came from when it has room.
- **No dungeon lockouts:** the Anima cost of locked content and gathering the party are the only limits on reruns.
- **If a game server crashes mid-dungeon,** everyone is placed back at the dungeon entrance, the Anima spent on entry is refunded, and loot already taken stays taken (it was saved immediately).

## Considering

- **Unstable teleport stations** (project lead idea): a cheap way to gather a party at a dungeon, with consent. Unlike the stable stations in settlements, they start unstable; one player stabilises the station, then every party member is prompted to leyline-teleport there.
- **Names for unassociated channels** (project lead idea): names of lore constellations that players can't select, for consistency. In lore, these are "further away" realities. If the list runs out, append a suffix to a constellation name (e.g. "Lyra II").
  - *Agent note:* names only need to be unique within a zone, so the list must cover the most channels one zone ever runs. Worst case is a whole region in one zone (10,000 players / 150 per channel ≈ 67 channels); a list of ~70–100 names would likely never run out.

## Rejected

- **Worlds** (named sub-groupings inside a region, like home servers): players can be moved to any channel or instance in a region anyway, and Worlds bring server reputations, population imbalance and transfer requests. Constellations cover the placement role.
- **"Layer" and "shard"** as names for channel copies (see `glossary.md`).

## Open

- Nothing open right now.
