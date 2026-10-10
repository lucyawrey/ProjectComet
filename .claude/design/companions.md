# Companions

**Layer: game (Anima).** Comet keeps only movement and attachment: mounts as a movement mode, passengers, an entity following its owner (see `proposal.md`).

Agent notes on companions: creatures that follow you, fight with you or carry you.

## Decided

- **All companions are creatures for now.** A separate system for non-creature mounts or minions (mechanical mounts, summoned constructs) may come later.
  - **Keep "creature" out of the core companion model,** e.g. a kind field on species, so a later non-creature system can share slots, tasks and stables. Shared systems underneath, but each kind can still feel very different to players.
- **Companions are their own system, separate from items** (see `items.md`), but follow item rules wherever they can, for consistency.
- **Each companion is an individual** with its own record (name, stats). Having "multiples" of a companion means several individuals of one species.
- **Three locations, one at a time** (mirroring the item containers model):
  - **Active:** out in the world with you.
  - **Carried:** with you but not summoned.
  - **Stabled:** kept somewhere like Storage.
- **Carried companions use a fixed, small number of companion slots** (e.g. 4), separate from the inventory, the same for everyone and never raised.
- **One active companion plus a mount.** A mount that should also fight takes the active companion's place.
- **Tasks:** each companion can do one or more tasks.
  - **Any class:** ground mount, flying mount, follower.
  - **Combat tasks need a matching class** (e.g. beast master, summoner): fighter, healer, caster.
  - **A fighting mount is a combat task** and needs a matching class by default.
- **Companions level up,** earning XP while active, to a species cap. **Levels improve task skill:** mount speed and stamina, combat strength for class-bound tasks.
- **Big companions can carry passengers** (the 2–4 seat mounts in `world.md`). The owner steers.
- **Party members can only ride as passengers.** Only the owner controls their companions.
- **Customisation:** players can name companions, and give them cosmetic gear (saddles, barding, accessories) and learned dyes. No stat gear.
- **Capturing varies by species:** some need capture items, some taming, some a quest.
- **Flying needs both a flying mount and the region's Flight Attunement** (see `unlockables.md`).
- **Stables are at settlements, housing and guild halls.**
- **Companions come from capturing wild creatures, and from rewards and shops** (quests, dungeons, NPC vendors).
- **Releasing a companion removes it.** Story-wise it's set free, not destroyed. It doesn't become a wild, capturable creature.
  - **A released soulbound companion** stops showing in the stable until it's reclaimed at a recall station, the same way soulbound items are recalled. This keeps the soulbound rules consistent everywhere.
- **Tradeability and soulbound work the same as for items:** a Market / Direct / Untradeable tier per species, plus a per-companion soulbound state.

## Considering

- **Beast master-type Runes** could let other classes use combat tasks, such as a fighting mount (see `skills.md`).
- **Hatching and breeding:** they would only come together. Not in the initial scope, but maybe someday.

## Rejected

- **A "channeler" task** (from the notes; it was a random example).

## Open

- Capture details per species (content design).
