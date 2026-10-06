# Glossary

**Layer: mixed.** Base (Comet): deployment, region, zone, channel, instance, height zones, invulnerability, Storage, tradeability, learned flag. Game (Project Anima): constellation, soulbound rules, collection log, gear and outfit sets, Class Crystals, Crystal Archives, Soul XP, Anima, Runes, rune stones, mastery, Outfit Magic, the Freelancer tree.

Working terms. They may change but must stay distinct. Details live in the topic files.

| Term | Meaning |
| --- | --- |
| **Comet** | The shared base: libraries, servers, Unity package and tools for a family of similar MMOs. MIT-licensed. Not the Unity engine. |
| **Project Anima** | Working name of the game built on Comet. The repository is still called `ProjectComet`. |
| **ShapeLand** | Comet's tiny demo and reference game: coloured shapes (cube, diamond, pyramid) that slide, chat and fight curved shape monsters with magic. Used for phases 0–2. |
| **Base / game (layer)** | Whether something belongs to Comet or to a game built on it. Each design file has a Layer line; game policy sits on thin base mechanisms. |
| **Class Crystal** | A soulbound item that grants permission to use one of its owner's class entries, and references a loadout, gear set, outfit set and appearance. The only thing a character equips; always one equipped. |
| **Class entry** | A character's permanent record of a class: current XP and highest level. |
| **Crystal Archives** | All of a character's class entries; also the large station combining Storage, the Archives and recall. |
| **Bought / granted crystal** | Granted crystals (quests, starting class, first Freelancer) are free and don't raise the rising crystal price. |
| **Freelancer** | The only tier 1 class and root of the promotion tree; no core kit, so every primary slot is flex. |
| **Class promotion** | Converting a crystal to a higher-tier class. |
| **Loadout** | A per-character saved skill setup, assigned to a crystal. |
| **Soul Experience (Soul XP)** | A spendable XP currency: overflow from maxed classes and crafts, spent on promotions and permanent unlocks. |
| **Anima / Anima Capacity** | The magic of your soul: an energy resource that regenerates over time, spent on teleports, crystals, locked content and magical appearance changes. Capacity is how much you can store. Never sold for real money. |
| **Skills** | Primary actions: ~10 slots, a designer-made core kit plus flex slots. Jump, crouch, dodge and sprint are class Skills on dedicated controls. |
| **Core kit / core slot** | A class's locked Skills. Core slots may be typed or untyped. |
| **Flex slot** | An open primary slot, filled from the class's optional Skills or other held classes' untyped core and flex Skills. |
| **Abilities** | All secondary actions (from classes, Runes, the base system, character-wide unlocks). Outside the primary control scheme. |
| **Rune** | A slotted skill: passive, modifier to a Skill, or grant of an Ability. |
| **Rune stone** | A Rune as a tradeable item. |
| **Slots (rune)** | Same-size slots in categories: Offense, Support, World. Primary slots have their own category. Each category has a colour in the UI. |
| **Variant Rune** | Swaps a core Skill for a designer-made alternative. |
| **Unbound Rune** | Unlocks a core slot of its type so any allowed Skill can go there. |
| **Gear / class Runes** | Runes built into gear (free while worn) or classes (locked, shown in a slot). |
| **Mastery / extraction** | Mastery builds up on worn gear (stored on the item) or on a class's Runes (stored on the class entry); extraction yields a rune stone and resets mastery. |
| **Outfit Magic** | Wearing an actual gear piece over the top as an outfit; the outfit's World Runes replace the gear's. |
| **Gear set / outfit set** | The gear and outfit a crystal references. Items technically live in Storage but are presented as "in" the crystal; one item can be in several sets. |
| **Storage** | Practically unlimited item storage at settlements, houses and guild halls. Shares most systems with the inventory. |
| **Learned flag** | A character-wide unlock (hairstyle, recipe, Rune unlock, emote, attunement…), usually learned by consuming an item. |
| **Tradeability** | Per item type: Market (default), Direct (player trades only, no market) or Untradeable (every instance always soulbound). Soulbound items are not implicitly untradeable. |
| **Item collection log** | A record of item types a character has obtained; separate from the items themselves and from learned flags, but shown with flags in the collection UI. |
| **Soulbound** | An item tied to its owner: it can be dropped, carried by others and handed back, and recalled by the owner at a recall station. Only the owner can equip a soulbound crystal. |
| **Deployment** | One complete installation: a Login server with its account database, plus every region it lists. The official game is one deployment; each private server is another. Accounts work across regions within a deployment, never across deployments. |
| **Region › Zone › Channel; Dungeon › Dungeon instance** | Server hierarchy. The region is the player-facing "server": a separate copy of the world. |
| **Zone** | Any zone file: open zone, dungeon or house. |
| **Instance** | Any running copy of a zone on a game server: a channel, dungeon instance or house instance. |
| **Channel** | A copy of an open zone shared by many players. Named and visible to players. |
| **Constellation** | An official grouping a character may join (one or none); only affects channel placement. Each can have one associated channel per open zone. |
| **Height zones** | Vertical layers (digging, crouching, standing, jumping, flying) used in hit checks. |
| **Invulnerability / pierce** | Defender's invuln level vs attack's pierce level. |

## Rejected terms

- **"Project Comet"** for the game: Comet is now the base's name.
- **World** (a sub-grouping inside a region): dropped; constellations cover the "familiar faces" role.
- **Layer** and **shard** for channel copies: "layer" echoes WoW's disliked layering; "shard" clashes with whole-world shards (Ultima Online, EVE) and database sharding.
- **Engine** for the base: clashes with Unity being the engine.
