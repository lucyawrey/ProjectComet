# Items and Economy

Every item in Anima is real: a physical thing in exactly one place, which can be dropped, handed over, traded or lost. No item is ever sold for real money. That makes the economy matter, and it makes the world feel solid.

## Carrying and keeping things

- **Inventory** is a fixed size per character, not something players upgrade.
- **Storage**, at settlements, houses and guild halls, is practically unlimited: the goal is that a player can keep every item and gear piece in the game. Sensible caps only warn at ridiculous numbers.
- **Bags** extend the inventory, either a few slots for certain item types or a bundle of anything up to a total. Bags can be dropped with their contents.
- **Dropped things** despawn after an hour, or when a channel restarts. That's the main way items leave the world.

## Gear and outfits

Gear lives in **gear sets** belonging to Class Crystals (see [Classes and Skills](Classes%20and%20Skills.md)). In the interface a crystal holds its gear; under the hood the gear sits in Storage and the set points at it, so one piece can belong to several crystals' sets. Gear can be changed in the field but not in combat; swapping crystals is how you change gear mid-fight.

**Outfit sets** work the same way: real gear pieces worn over the top for looks and for their World Runes.

Gear wears down with use and can be repaired by crafters or NPCs. At zero durability it keeps working at reduced stats; it's never destroyed by wear.

## Soulbound items

Some items are bound to their owner, but they stay physical:

- Others can carry, hold, store and hand back your soulbound items, and wear soulbound gear you lend them.
- Only the owner can sell, trade away, break down, transform or consume them.
- The owner can **recall** a soulbound item at any time, for free, from a **recall station**. Unattended items return on their own after 24 hours, and anything that would despawn returns immediately. Nothing soulbound is ever lost.
- The **Crystal Archives** are the large stations: Storage, the Archives and recall in one place.
- A destroyed untradeable item can be re-obtained from an NPC for a coin fee.

## Tradeability

Each item type has one of three settings:

- **Market:** tradeable and listable on the market (the default).
- **Direct:** player-to-player trades only (for example quest drops).
- **Untradeable:** always soulbound to whoever receives it.

## Things you know vs things you have

Physical things are items: gear, outfits, materials, consumables, crystals, rune stones, bags, furniture. Knowledge is a **learned flag**: hairstyles, dyes, recipes, emotes, titles, attunements. Learnable things usually come as an item that stays tradeable until someone uses it.

The **item collection log** separately records every item type a character has ever obtained, for showing off.

## Currency

Currency is items too, stacked in the inventory and Storage. A **coin purse** is just a bag that holds every currency in one slot. Spending draws from your inventory, purse and (where available) Storage. **Untradeable currencies** never leave their owner: they can't be dropped, handed over or lent.

## The market and money

- **A region-wide exchange** with buy and sell orders, reached at market boards in settlements, with a sales tax. Player-run stalls may come later.
- **Buy limits** over a time window apply to some scarce or volatile items.
- **Money comes in** from quests, selling to NPCs and some monster drops: more generous than a purely player-driven economy, less than a classic coin-drop MMO.
- **Money goes out** through the market tax, repairs and NPC services, housing and guild hall upkeep, crafting, and cosmetic and prestige purchases from NPCs (cosmetics, furniture, housing plots, titles).
- **Designers watch it** through dashboards built from the item ledger: where money enters, where it leaves, and how much exists.

## Where gear comes from

A mix of crafting and drops. Crafted gear's quality depends on the crafter (see [Crafting and Housing](Crafting%20and%20Housing.md)), and **co-crafting** lets someone with materials but not the skill get an item made: by another player, who shares the signature and the experience, or by an NPC, for a fee and at middling quality.

## Real money

The only thing sold for real money is a **tradeable membership item**, like Old School RuneScape's bonds. Players can buy one and sell it on the market for in-game currency. There is no item shop and no paid randomness (see [Business and Audience](Business%20and%20Audience.md)).

## Open questions

- How many tool slots each gear set has (three for now).
- Whether some endgame crafting costs Anima.
