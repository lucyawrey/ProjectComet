# Comet Overview

Comet is the foundation Project Anima is built on: the servers, shared libraries, Unity package and tools that make a seamless, browser-playable action MMO possible. Project Anima is the game; Comet is everything underneath it that another, similar game could reuse.

## Why two layers

Building an MMO means building a lot of infrastructure before any of it is fun: networking, persistence, a world that streams without loading screens, moderation tools, a content pipeline. Almost none of that is specific to one game's ideas.

Splitting the project in two keeps that work useful even if the team wants a different game. If Project Anima's vision isn't the one we end up making, Comet still gets us most of the way to a similar MMO that fits other goals.

## What Comet assumes

Comet is not a general-purpose engine, and games built on it will feel related. It makes firm choices about:

- **The world:** one seamless world per region, split into zones and copied into channels, with dungeons and houses as instances. No loading screens except behind teleports.
- **Combat:** action combat with real hitboxes, height zones (crouching, standing, jumping, flying) and invulnerability, timed in frame data. PvE only.
- **Platforms:** desktop browsers first, plus a desktop app, all on WebSocket.
- **Persistence:** every item is a real database row, every change of owner is in a ledger, and private servers are easy to run.

What a game decides for itself: its classes and progression, how skills are acquired and slotted, its economy and market, its social rules, its business model, and all of its content.

## Mechanisms in Comet, policy in the game

The general rule is that Comet provides thin mechanisms and each game sets the policy on top. For example:

| Area | Comet provides | Project Anima decides |
| --- | --- | --- |
| Channels | Placement with a pluggable preference policy, soft and hard caps, draining and merges | Constellations and who you're placed with first |
| Instances | Lifecycle, entry and exit hooks, teleport handoff | Loading rooms, re-entry rules, no lockouts |
| Items | Containers, slot rules, an owner or binding field, the ledger | Soulbound recall, gear sets as references, outfits |
| Skills | Executing an action: frame data, hitboxes, cooldowns, effects | How skills are learned, slotted and advanced |
| Companions | Mounts as a movement mode, passengers, followers | Companion records, capture, tasks, levelling |
| Economy | Player trading and the ledger | The market, taxes, sinks |

A few things stay in Comet because they're structural and hard to add later: currency as items, learned flags, tradeability tiers, flying and fixed-route vehicles, and the combat core.

## Keeping the split cheap

A clean split in one codebase costs a little more up front; building a true product engine would cost far more, and is how small teams stall. So Comet follows a few rules:

- **Extract, don't pre-build.** Something moves into Comet only once two games actually use it. Until then it lives in the game.
- **One repository, no API promises.** Comet and its games live together. Breaking changes are fine; both games are fixed in the same commit. Comet becomes a product only if a second real game exists.
- **Extension points only when needed.** Plain C# interfaces and registration by key, added when something needs them.
- **A tiny reference game.** ShapeLand, a game of coloured shapes, exercises Comet in the early phases and stays small on purpose (see [ShapeLand](ShapeLand.md)).

## Reusing Comet

A team building a different game on Comet would get the seamless world, netcode, combat core, persistence and ledger, content pipeline, moderation and admin tools, and self-hosting support. They would write their own game modules (classes, progression, economy rules), their own Unity project for UI, rendering and art, and their own content. Comet and ShapeLand are MIT-licensed.

## Open questions

- None at the proposal level.
