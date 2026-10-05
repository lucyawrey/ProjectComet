# World structure

Agent notes on world structure and server hierarchy. Decisions made with the project lead on 2026-10-03. Housing access is in `crafts.md`.

## Decided

- **Most areas can be reached without the story** (project lead, 2026-10-04), through less convenient routes (e.g. a very long walk instead of a story carriage ride). Free players also meet in-world barriers at later-part borders (see `proposal.md`, business model).

- **Zone borders: chokepoint handoff with a little overlap.** Borders can be wide (e.g. a valley between mountains) but each border joins exactly two zones; if more meet by accident, only the closest transition is synced. Near a border, players see a read-only view of the neighbouring zone.
- **No effects across borders:** attacks, AoEs, heals and buffs only affect entities on the same server.
- **Monsters stay in their zone;** border areas are designed with little or no combat.
- **Many zones are reached only by teleport or a single door** (e.g. dungeon entrances) and need no border sync.
- **Flying can cross zone borders.** Little flying-to-ground interaction and no aerial combat; far-away players are culled.
- **Hierarchy:** Region › Zone › Channel, and Region › Dungeon › Dungeon instance. **No Worlds** (2026-10-04): the region is the only player-facing "server", and guilds belong to the region. (A private server would be one region.) Worlds were dropped because players can be moved to any channel or instance in a region anyway; names from the notes like Comet and Crystal can be region names.
- **Terminology** (2026-10-04): a **zone** is any zone file (kinds: open zone, dungeon, house). An **instance** is any running copy of a zone on a game server: a **channel** (copy of an open zone, many players), a **dungeon instance** (one party) or a **house instance** (one owner; guild halls too). "Channel" replaced "layer" (2026-10-04; too close to WoW's disliked layering, and an industry-standard term for visible zone copies, as in Lost Ark and BDO), which earlier replaced "shard"; "shard" clashes with the MMO sense (Ultima Online's shards, "single-shard" games like EVE, where a shard is a whole world, like our region) and with database sharding.
- **Channel preference when entering a zone** (2026-10-04, replacing home World): party's channel, then primary guild members' and friends' channels, then the player's constellation (its associated channel if it has room, otherwise channels with the most players sharing it), then secondary guild members' channels, then players sharing the same language, then the channel the player was last on, then any channel with room.
- **Constellations** (2026-10-04): a loose, in-world grouping, in the spirit of Elden Ring's group passwords. Their only gameplay effect is channel priority. No roster, chat, leader or ranks; guilds are the player-made groupings.
  - **Why not Worlds:** constellations recreate Worlds' placement role (seeing familiar faces in a big region) but set different expectations. There's no commitment (free to change, optional, hideable), no partition (they rank below party, primary guild and friends, so they only break ties among strangers), and no identity baggage (no server reputations, population imbalance only means busier associated channels, no transfer requests).
  - Each character is in **one official constellation or none**. It's chosen at character creation and can be changed for free at any time.
  - **Other players see your constellation by default;** you can choose to hide it.
  - **Per open zone, each constellation can have at most one associated channel.** It's the preferred channel for that constellation, but anyone can be placed there.
- **Multiple guilds per character** (2026-10-04): a character can join more than one guild and chooses a primary guild, which shows on their profile. Primary and secondary guilds count separately in channel placement.
- **Channels are named and visible to players** (2026-10-04): the UI shows which channel a player is on. Names make channels feel like discrete places rather than anonymous copies: in lore, each is a discrete echo of the same area, a specific reality.
  - **Constellation-associated channels are named after their constellation** (2026-10-04).
  - **Players can deliberately move to a named channel** to find someone, e.g. a new player they've just introduced to the game and haven't friended yet: ask which channel they're on, then go there. Within a zone this is the normal channel move (leyline shimmer, player-move cooldown); in another zone, a normal teleport can target a named channel. Deliberate joins may use the headroom up to the hard cap, like party members.
- **Channel size:** 150–300 players would be an impressive upper limit; may be forced lower.
- **Channel lifecycle** (2026-10-04, compared against WoW's sharding and layering):
  - **Soft and hard caps:** new players are placed on a channel only while it's below a soft cap (~80% of the hard cap, tuned later); the headroom is for party members joining and border crossings.
  - **Channel pairing across borders:** on crossing, prefer the neighbouring zone's channel that most nearby travellers are going to; otherwise use the channel preference rule. Keeps groups of travellers together, as WoW Classic's continent-wide layers did.
  - **Closing a channel by draining:** stop placing new players on it and let it empty naturally (borders, teleports, logouts); move the remaining players only if its population stays low.
  - **Moves between channels** (merges, joining a party member's channel) use the handoff, shown as a short **leyline shimmer** before the new channel fades in. Never in combat or mid-skill; player-triggered moves have a cooldown.
  - **Constellation-associated channels aren't drained aggressively** (2026-10-04), but placement can skip a truly dead one: a player entering the zone may be put on a busier channel outside their constellation if their constellation's channel has almost no one on it (threshold tuned later).
  - **Ground items carry over on a merge** (2026-10-04): they move to the destination channel at the same position, keeping their despawn timer (one database update). A channel that closes empty with no merge clears its ground items like a restart.
  - **Channel hopping gains little:** gathering nodes are mixed per-player and shared, chosen per node (as in `crafts.md`; the move cooldown limits hopping for shared ones), and open-world boss rewards are limited to once per player per spawn cycle, whichever channel they fight on.
- **Teleport scene** (2026-10-04): a fixed-length scene where the player's soul travels along the leylines on the world map to the destination. If the destination hasn't finished loading when the travel animation ends (likely on web), the soul pulses at the destination on the map until it has. It hides loading; its details are gameplay design, not backend (handoff in `backend.md`).
- **Teleport casts can be cancelled, and enemies can interrupt them. Teleporting in combat is allowed** (2026-10-04), but cast times are long enough that trying it mid-fight will usually get you killed.
- **Dungeon loading rooms** (2026-10-04): the whole party has to be physically present (in the spirit of classic MMOs). Once the whole party is in the loading room and one member steps into the dungeon proper, the loading room's entrance closes and the dungeon starts. Everyone else still walks in on their own.
- **Re-entering an instance** (2026-10-04): a player who disconnects or leaves can re-enter the same instance.
  - Instances with no player limit: re-entry is allowed any time until the instance no longer exists.
  - Instances with a fixed player limit: the player's place is held, but the party can vote to remove them. Removal is for disconnected players and for troublesome ones alike, and carries no negative connotation. Problematic players are reported through the separate report feature. (Avoid the term "kick".)
  - Someone new can take a removed player's place.
  - **A replacement player enters a started dungeon through the closed entrance** (2026-10-04): it admits only the holder of the open slot, who walks in through the loading room. The party can also stabilise an unstable teleport station inside the dungeon to bring them closer.
- **When an instance closes** (2026-10-04): if everyone has left through gameplay (walking out of an exit, teleporting away, or clearing the dungeon and leaving), it closes instantly. If any player is missing because of a disconnect, it stays open for a few minutes so they can return. The game server can tell the two apart: leaving is a handoff, a disconnect is a dropped connection.
- **Dungeon exits vary by design** (2026-10-04): a separate exit, a tunnel back to the entrance, or a teleport at the end (to the entrance or elsewhere).
- **Leaving a dungeon into an outdoor zone uses the channel preference rule** (2026-10-04), preferring the channel the party came from when it has room.
- **No dungeon lockouts** (2026-10-04): the Anima cost of locked content and gathering the party are the only limits on reruns.
- **If a game server crashes mid-dungeon** (2026-10-04), everyone is placed back at the dungeon entrance, the Anima spent on entry is refunded, and loot already taken stays taken (it was saved immediately).
- **Loading rooms need not look like rooms** (2026-10-04): they can appear to be part of the open world (a mountain top, a grove past a narrow gap in the trees). Their static scenery may show up in open-world LODs, but players and other live entities are only synced across the official entrance, never across the rest of the room's edge.
- **Vehicles:** boats and airships cross zones on fixed routes. The only player-steered vehicles are 2–4 seat mounts with near-normal movement. Vehicles and their riders cross borders as one group.

## Considering

- **Unstable teleport stations** (project lead idea, 2026-10-04): a cheap way to gather a party at a dungeon, with consent. Unlike the stable stations in settlements, they start unstable; one player stabilises the station, then every party member is prompted to leyline-teleport there.
- **Names for unassociated channels (project lead, 2026-10-04):** names of lore constellations that players can't select, for consistency. In lore, these are "further away" realities. If the list runs out, append a suffix to a constellation name (e.g. "Lyra II").
  - *Agent note:* names only need to be unique within a zone, so the list must cover the most channels one zone ever runs, not the region's total. Worst case is a whole region in one zone (10,000 players / 150 per channel ≈ 67 channels); a normal busy zone needs far fewer. A list of ~70–100 names would likely never run out.
- **Data model (agent suggestion):** Region is the real unit (one database scope, owns all its instances; a private server is one region).

## Open

- Nothing open right now.
