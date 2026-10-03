# Companions

Agent notes on companions: creatures that follow you, fight with you or carry you. Decisions made with the project lead on 2026-10-03. Topic covered; a few questions are left open.

## Decided

- **All companions are creatures for now.** No non-creature mounts or minions (e.g. mechanical mounts, summoned constructs) are planned; a separate system for them may come later.
  - **Keep "creature" out of the core companion model** (adopted from an agent suggestion), e.g. a kind field on species, so a later non-creature system (vehicles, minions) can share slots, tasks and stables. Shared systems underneath, but each kind can still feel very different to players.
- **Companions are their own system, separate from items** (see `items.md`), but follow item rules wherever they can, for consistency.
- **Each companion is an individual** with its own record (name, stats). Having "multiples" of a companion means several individuals of one species. (Adopted from an agent suggestion.)
- **Three locations, one at a time** (adopted from an agent suggestion, mirroring the item containers model):
  - **Active:** out in the world with you.
  - **Carried:** with you but not summoned.
  - **Stabled:** kept somewhere like Storage.
- **Carried companions use a small, fixed number of companion slots**, separate from the inventory.
- **One active companion plus a mount.** A mount that should also fight takes the active companion's place.
- **Tasks:** each companion can do one or more tasks.
  - **Any class:** ground mount, flying mount, follower.
  - **Combat tasks need a matching class** (e.g. beast master, summoner): fighter, healer, caster.
  - **A fighting mount is a combat task** and needs a matching class by default.
  - The notes' "channeler" task was a random example and is dropped for now.
- **Companions level up**, earning XP while active.
- **Big companions can carry passengers** (the 2–4 seat mounts in `world.md`). The owner steers.
- **Party members can only ride as passengers.** Only the owner controls their companions.
- **Customisation:** players can name companions, and give them cosmetic gear (saddles, barding, accessories) and learned dyes. No stat gear.
- **Capturing varies by species:** some need capture items, some taming, some a quest.
- **Flying needs both a flying mount and the region's Flight Attunement** (see Unlockables).
- **Stables are at settlements, housing and guild halls.**
- **Companions come from capturing wild creatures, and from rewards and shops** (quests, dungeons, NPC vendors).
- **Releasing a companion removes it.** Story-wise it's set free, not destroyed. It doesn't become a wild, capturable creature.
  - **A released soulbound companion** stops showing in the stable until it's reclaimed, the same way soulbound items are recalled. This is mostly useless, but it keeps the soulbound rules consistent everywhere.
- **Tradeability and soulbound work the same as for items:** a Market / Direct / Untradeable tier per species, plus a per-companion soulbound state.

## Considering

- **Beast master-type Runes** could let other classes use combat tasks, such as a fighting mount (see `skills.md`).
- **Hatching and breeding:** they would only come together. Not in the initial scope, but maybe someday.

## Open

- How many companion slots there are.
- Capture details per species.
- How companion levelling works (caps, what levels change).
- Where reclaiming happens (tied to the item recall location, which is also open).
