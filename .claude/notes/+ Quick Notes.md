# Gameplay
* Massively Multiplayer PvE Action RPG/Life simulation game. Inspired by: Rabbit and Steel, Monster Hunter, Crystal Chronicles, Old School RuneScape, FFXIV, WoW, Path of Exile, Paper Mario, Phantasy Star Online, V Rising, Breath of the Wild, Infinity Nikki.
* Not a tab targeting MMO, but will have a lot of similarities to one. Has an optional tab targeting/lock on system for aiming ranged attacks or auto facing an enemy for melee attacks, but almost all Skills have actual hit boxes that can hit one to many targets in a range. For example, a “single target” melee attack might create a small oval hitbox slightly in front of the player, while an AoE melee attack might create a larger circle hitbox centered on the player. Hitbox active frames will ideally be well matched up with animations.
* While the game is 3D, its hitboxes and hurtboxes are essentially 2D. The vertical trajectory of an attack animation is mostly decorative. However, hitboxes and hurtboxes still interact with a more simplified version of 3D space using “height zones” which affect whether a hit is valid. Many hitboxes and hurtboxes cover two height zones but there are exceptions. This can be thought of as if a 2D hitbox was extruded into a 3D shape (like a cylinder from a circle). Tentative height zones are:
	0. Digging
	1. Crouching
	2. Standing
	3. Jumping
	4. Flying
	5. Invulnerable
* Some attacks have different active frames in each height zone they effect. 
* Height zones scenario examples:
	* Boss uses sweep attack, which causes a bad status effect but only hits zone 1-crouching. The player jumps, changing their hurtbox from zones 1+2 to  2+3 and thus avoiding the attack.
	* Boss uses overhead attack that hits zones 2+3. It can be avoided by crouching, which changes player hurtbox to only zone 1 at the cost of lower movement speed. Jumping breaks crouching.
# Items - Inventory
* Characters have a fixed inventory size that cannot be upgraded, much like Runescape or Minecraft. It may be increased in major updates.
* Each character has Storage they can access at settlements that can hold virtually infinite items.
* A character's currently equipped Gear and Class Crystal do not take up space in their inventory.
* Each type of item has its own set stack size when in a character's inventory, but multiple stacks can be held in multiple inventory slots if an item's stack size is exceeded. This includes items with a stack size of 1.
* Effective inventory can be increased by carrying Bag in your inventory that can either hold multiple slots worth of items of limited variety, or multiple slots of any item type but limited total quantity (similar to a Minecraft bundle).
* Class Crystals share functionality with Bags in that they can expand your effective inventory by holding a full set of gear inside themselves.
* Inventory can be auto sorted. Inventory should have a special display mode where Containers are visually separated from the rest of your inventory.
* Gear can grant Skills while equipped. The skills on gear can be extracted as normal Skill Runes.
* An item can be marked as Soulbound. This means that if the item cannot be traded (but can still dropped or given away) and can be recalled to the Soulbound character at any time from an appropriate location even if it no longer exists in the world.
# Items - Collection
* A big aspect of the game is collecting things. To facilitate this, each character has enough storage to collect every item in the game.
* Almost everything you would want to collect in an MMO exists as an item in this game. Equipment, Materials, Consumables, Bags, Class Crystals, Skill Runes, Item Recipes, Dyes, Hairstyle Manuals.
* You need to keep the actual items that you are collecting in order to use them in things such as outfits. Collected items that are traded away or dropped are tracked account wide and can have easier methods for being re-obtained. 
# Companions
- Separate from your item collection. Various monsters and creatures can be obtained as companions that can follow you around or be used as mounts.
- Like items, you can have multiple of a single companion, trade them to other players, or release them into the world.
- More comparable to a lightweight creature collector game than traditional MMO mounts.
- Each companion is capable of one or more tasks: ground mount, flying mount, follower, fighter, healer, caster, channeler. Certain tasks are only usable with an appropriate accompanying class such as beast master or summoner.
# Unlockables
* Progression not tied to your item collection, companions, or class/craft levels.
* Includes Anima Capacity, Flight Attunements, and Teleportation Attunements.
## Classes
* Each character starts with one base class in a location appropriate to that class and their Ancestry.
	* The early game is largely defined by playing through quest chains related to that location and your class.
	* Unlocking a new base class, following its story, and leveling it should feel like somewhat like playing the game again.
* Classes are represented by Class Crystals, which a character shatters to transform into it’s class (equipping the class)
	* Class Crystals, like all items, take a slot in the player’s limited inventory. Taking multiple classes into the field takes up multiple inventory slots. Class Crystals cannot be put in bags and don’t stack.
	* Class Crystals contain their Class, a full set of Gear, a full Outfit, and a set of Appearance Options. Changing out the gear, outfit, or appearance in a Class Crystal requires a spell cast that takes longer in combat.
	* Separate Gear, Outfit, and Appearance Crystals exist. These work with Freelancer, but can also override whatever is in your equipped Class Crystal. In lore, a Class Crystal is a sort of super technology that merges all of these together with Skill Crystals.
	* Applied Outfits can be dispelled by other players using a spell effect.
	* Outfit Magic is an old magic that infuses one item of gear with the appearance and some of the *properties* of another gear item. Some gear skills, such as temperature regulation ones, are overridden by the skills from any equipped outfit instead of the equipped gear.
* When class crystals reach a certain level you can promote them to a high-tier classes, which start at a level lower than the promotion requirement. The level of the low-tier class when promoted is remembered. High-tier classes are slightly more powerful than low-tier classes at equivalent levels. They are also typically more specialize.
* The total number of total class crystals a player can keep in their inventory or storage is limited, and will likely be three in the initial release.
* The highest level attained for every class of every tier is saved for a given character in the Crystal Archives, so you can never truly lose progress. Similar to Persona, you can spend a resource at the archives to replace one of your current class crystals with a different class crystal from your history at its highest level. This can be done even after a promotion in order to re-promote in another direction.
* A Class’s status can either be Locked: never obtained; Archived: in Crystal Archives; Stored: in storage or with another character; or Attuned: In current inventory or equipped.
* All experience points earned either level a standard Class, the Freelancer Class, a Craft, or become Soul Experience. Soul experience can be spent on class promotions and other permanent unlocks such as Skills or Anima Capacity Upgrades. The system is designed so no experience can be wasted. Experience gain increase should be linear to make this work better. This will allow for more effective grinding in low level zones than most MMOs, but that is acceptable.
* The player may be provided with an interface that allows selecting how gained experience is distributed between their Current Class, Current Class Crafts, Freelancer, and Soul.
* The player can select whether experience should go to their equipped Class, their Soul, or split. Before Freelancer is max level, a portion of experience gained will always go to it even when it is not equipped. This means grinding feels more efficient with Freelancer maxed. A maxed equipped class puts all experience to Soul automatically, even if it is only considered maxed out due to lacking an expansion or subscription.
* There is no experience boost for leveling a second class, and experience requirements stay consistent. Experience boosts come from specific sources. Examples include new character experience bonuses (up to an exp cap), rest experience bonuses (up to an exp cap), item consumption bonuses, skill bonuses, roulette/content fill bonuses, helping a new player bonuses, and level sync down bonuses. There will typically not be bonuses explicitly scaled to the players current level outside of level sync down bonuses. Doing low level content without a level sync down does not have a boost.
* A given class at a given level will always have the same Attributes before Gear and Skills are applied.
* Freelancer is the exception. It uses a formula to calculate its Attributes based on its own level, your Ancestry, your Attuned Classes, and your Attuned Crafts.
# Crafts
- Crafts, like Classes, represent a player’s abilities and have a Level. Unlike a Class, Crafts are not equipped one at a time and can be used freely.
- Crafts are used for Crafting, Building, Gathering, and Fishing.
- A player can unlock every Craft and use all of them without swapping any out, but a temporary level cap is placed on Crafts that are not currently Attuned. This should somewhat discourage omni crafting.
- Crafts will either have their own Craft Crystals that can be swapped out for a fee at the Crystal Library, or be instead tied to Classes. If this is the case, you would be able to level a Craft and its linked Class interchangeably through combat and crafting, although they would still level independently of one another. All Crafts would still be obtainable without a class, but would be level-capped based on whether a linked Class is currently Attuned.
# Skills
* Outside of Ancestry, Anima Capacity, Class Levels, Craft Levels, and Gear the only way to distinguish a character is their Equipped Skills.
* Skills are split into Passive Skills and Action Skills.
* Skills are permanently learned from Leveling Up, NPC Tutors, Skill Crystals, or mastering Gear Skills.
* All skills are unique and can’t be doubled up on. Obtaining duplicate skills from multiple sources gives extra experience instead of a copy of the skill. Some skills have identical variants with unique identifiers to make up for this. Example: Magic Up (a) and Magic Up (b).
* Skills cost a number of SP (Skill Points) to Activate, from 0 to 8. They can be deactivated to free up skill points at any time. Many skills have an SP cost of 0, especially general skills and core class skills.
* Gear Skills always cost 0 SP when a piece of gear is equipped, but may cost more SP to equip on their own after learning them. Certain Gear Skills can come from equipped outfits instead of base equipped gear.
* Activating certain Skills can exclude Activating other Skills.
* Action Skills in particular are very restrictive, with a hard limit on how many combat actions can be equipped at once and action types like “dash” only allowing one to be equipped.
* SP is gained with every level up along with Skill unlocks for the appropriate Class and Level.
* Each Class (including Freelancer) has its own SP pool and Active Skill list.
* Skills can be associated with: General, Martial, Magic, Craft, or a specific Class.
* General, Martial, Magic, and Craft Skills are available to all Classes.
* Class Skills are available to their associated class, any of their promotions, and sometimes a limited subset of other classes including Freelancer.
* Above a certain level, Class Skills require you to be Attuned to the correct Class to equip them.
* The above information can be simplified to: all Skills have a list of classes (either All or Limited) that can use them, an SP cost, and a possible Class Attunement requirement.
* Many Skills affect only Attributes. These usually are percentage based and thus scale with Level and Gear.
* Alternate Approach 1: Actions are a separate system entirely where the player is limited by number of actions they can equip instead of by SP.
* Alternate Approach 2: A Class’s core Actions and Skills are set in stone for a given level, and a set of permanent general character Actions are as well. Skills are instead equipped to override or improve on these defaults. In this approach, no Skills would be explicitly class restricted, they may just be useless to some classes without other prerequisite skills. Cross-class Actions would be specific Action Skills that grant that action to other classes or to Freelancer. This approach would include less 0 SP skills. You would not be able to disable gear given skills or core class skills, and the process of learning Skills and Action Skills from classes would be somewhat similar with how skills can be gained from gear.
* Alternate Approach 3: Skills use a shape based system instead.
* 
# Game World
- A core goal is a connected open world with no loading screens similar to WoW, including its vehicles. Unlike wow, any warping done through quests and travel NPCs, who act as a sort of “narrow” connection between zones. All warping will be explicitly teleportation magic, and most zones should not he accessible only through teleportation. Even dungeons should have no loading screens, instead having a transition room for loading. The only loading screen like thing there will be is for teleporting, and that will exist to hide loading but also to give a scene of player movement though the leylines. Space portals shouldn’t have loading times, and the game should attempt to pre load the destination while casting.
# Aesthetic
* Low poly characters with low res textures and that are cute but not so cute that characters can’t pull off cool or sexy. Clothing and hairstyles should not be gender locked, but each ancestry may have major differences. More than just two body types is desirable. Closets jumping off points are Phantasy Star Online, Crystal Chronicles and Signalis but there probably won’t be a pixelated filter. FFXIV is also good reference aesthetically (and with how much variety there is in character sizes, shapes and unique outfits) but is slightly more high res than desired.
# Networking
Game Server Deployment Approaches
*  Logical Server (usa_east, japan)
	* World (Comet, Crystal) - often split between multiple physical servers grouped under the same logical server because worlds are kind of fake. Used to organize players/guilds and sort players in non instanced zones in the same Logical Server
		* Zone (The City of Crystals, Silent Shore) - A region located in the overworld or as a standalone level where players will naturally encounter each other in larger numbers without needing to join a group. 
			* Shard (comet_silentshore, comet_silentshore_1) - All zones have 1 shard per world by default although that 1 shard will be on ice if no one is in it, with more added in the case of a lot of overcrowding and players not using the feature to get pushed to other worlds automatically. Due to smaller world size, the plan is to use shards somewhat rarely in this setup. Players are usually put into a shard associated with the current World they have selected when entering a zone. Despite this, all shards are shared across the whole Logical Server. A single shard needs to be fully hosted by a single physical machine.
		* Dungeon (Jail of the Lost, Corrupted Castle) - A region that is separate from the rest of the world and is created for the player or group that enters it.
			* Instance (comet_jaillost_25) - a single instance needs to be fully hosted by one physical machine

* Logical Server/World (comet: Comet, USA/East)
	* Zone (silent_shore: Silent Shore)
		* Shard (comet.silent_shore.21)
	* Dungeon (corrupted_castle: Corrupted Castle)
		* Instance (comet.corrupted_castle.68)
# Database
Tables
* Character
* CharacterStatus
* CharacterCraft
* CharacterClass
* CharacterAppearance
* CharacterSkillset
* CharacterGearset
* CharacterOutfit
* UnlockCollection
* CompanionCollection
* ItemCollection