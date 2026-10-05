# Database tables

**Layer: mixed.** Project Anima's game tables (`<game>_` side tables, see `backend.md`): crystals and class entries, Soul XP, Anima, Runes, attunements, gear and outfit sets, the collection log, companions, the market, housing, constellations, membership and parts. The rest is base (Comet): accounts, characters, items and containers, the ledger, flags, social basics, instances, moderation.

Agent notes on table design, per area. **The project lead decides the final schema**; the table drafts here are input. Architecture-level decisions (account and region databases, conventions, ledger, registry, chat buffer) live in `backend.md`.

Depth for this phase: tables, key columns and relationships, and which rules the database enforces. No full DDL.

Starting material: the project lead's table list in `.claude/notes/+ Quick Notes.md` ("Database": Character, CharacterStatus, CharacterCraft, CharacterClass, CharacterAppearance, CharacterSkillset, CharacterGearset, CharacterOutfit, UnlockCollection, CompanionCollection, ItemCollection); the old schemas on the archive branches (`archive/dotnet-datacenter`: `docs/reference.sql`); the data-model suggestions under Considering in `backend.md`; FishMMO's ~30 entities (see `backend.md`, Research).

## Decided

### Accounts and staff (account database)

- **Accounts are built on ASP.NET Core Identity's tables**, renamed to fit our conventions: password hashing, lockout, email confirmation and two-factor come built in.
- **Staff roles can be scoped to one region or to all regions**.
- **Character select queries the region databases**: the Login server asks each region database for the account's characters through the Data library, with no copied directory. A region that's down shows as unavailable.

### Characters (region database)

- **Character data is split by how often it changes**: identity, status, ownership and progress are separate tables, so periodic saves and ownership claims only rewrite small rows (PostgreSQL writes a new row version on every update).
- **Anima is stored as an amount plus a timestamp**; regeneration is computed when it's read or spent. Anima builds up offline, up to capacity (`classes.md`).
- **Character names are unique per region, ignoring case**. **Soft-deleted characters hold their name for a retention period** before it's freed. Names can contain spaces, which count for uniqueness: trimmed, with no leading or trailing spaces and at most one space between words; length limits apply (project lead).
- **Appearance is one shared `appearance` table with typed columns plus a JSONB column for numeric sliders**. Referenced fields (body type, hairstyle, face preset, ancestry parts) and colours are columns with foreign keys to the content lookups; numeric face and body sliders are one JSONB column. A character points at its base appearance row; a crystal optionally points at an override row, where null columns mean "use the base".

### Classes, crystals, loadouts, gear and outfit sets (region database)

- **A crystal is an `item` row plus a one-to-one `crystal` row**. Location, owner and soulbinding come from the item; the crystal row holds the class entry, bought or granted, loadout, gear and outfit sets, and an optional override `appearance` row.
- **Unique-equipped is enforced by the database**: `set_slot` also stores the item's type (safe, since an item's type never changes), with a unique index on (set, item type).
- **Loadout slots are rows**, with foreign keys to the content lookup tables.

### Items, containers and the ledger (region database)

- **One `item` table for every container**: one row is one physical item or stack in exactly one place (location, container_id, slot), so being in two places is impossible by construction. A partial unique index on (location, container, slot) protects slotted positions; Storage has no slots. Moves within one owner are plain updates and aren't logged; owner changes also write a ledger row in the same transaction.
- **Dropped items are persisted**: location = ground, with the instance. Pickups are ordinary owner changes in the ledger; despawn and channel restarts delete them (soulbound items return). Dungeon instance items and temporary structures stay in memory.
- **Per-item state is columns** (durability, quality, crafter signature, soulbound_to), with JSONB only for dye colours, and **gear Rune mastery in its own table** (`item_rune_mastery`).
- Storage is per character for now (multiple characters are ignored in this phase).

### Flags, collection, crafts and companions (region database)

- **Learned flags are rows** (`character_id`, `flag`) with foreign keys to the content lookups (a per-character bitset was considered).
- **Companion cosmetic gear is items located on the companion** (`location = companion`, `container_id` = the companion), so trading, dyes and the ledger work unchanged.
- **One ledger covers items and companions**, with an entity-kind column.

### Social, market and housing (region database)

- **Friendships are mutual, with requests**. Friends affect channel placement, so both sides consent.
- **One primary guild per character is enforced by a partial unique index** on primary memberships; guild ranks are a per-guild table with permission flags.
- **The market plans on one currency, but listings store a currency type plus amount** so any currency stays possible (project lead).
- **Placed furniture positions live in a separate `item_placement` table** (one-to-one with `item`).
- Constellations are content entries; direct trades are short-lived sessions ending in one transaction, with no table.

### World and moderation (region database)

- **An `instance` table is the placement record** for channels, dungeon and house instances. Channel names are unique per zone among live instances (partial unique index on (zone, name) where not closed).
- **Report chat is rows copied from the buffer** into `report_chat` when a report is made, deleted on resolution unless the report's `keep_evidence` flag is set. A report's recent ledger movements and violations are looked up at review time, not copied.
- **Automatic violations (`violation`) are kept like the ledger:** about 6 months, partitioned by month. Named to avoid confusion with learned flags.

## Draft tables (agent proposals)

The Layer column (following `proposal.md`) marks each table as base (Comet) or game (Project Anima). Game tables would become `anima_`-prefixed side tables (`backend.md`); names here are unprefixed for readability. Under "extract, don't pre-build", these are expectations, not build orders.

### Accounts and staff (account database)

| Table | Layer | Key columns | Notes |
| --- | --- | --- | --- |
| `account` (Identity user) | Base | id, email, created_at, kind (player or staff), status | Staff powers live on separate staff accounts. |
| Identity login and token tables | Base | password hash, external logins, tokens | Renamed to snake_case. |
| `staff_role` | Base | account_id, role (moderator, GM, admin), region_id (null = all), granted_by, granted_at, revoked_at | Instant revocation sets revoked_at. |
| `sanction` | Base | id, account_id, kind, reason, starts_at, expires_at (null = permanent), issued_by, region_id and character_id involved, report_id, lifted_at, lifted_by | Account-wide. |
| `appeal` | Base | id, sanction_id (unique), text, status, reviewed_by | Reviewer must differ from the issuer (app rule or trigger). |
| `moderator_action` | Base | id, actor_account_id, action, target, region_id, details (JSONB), at | The moderator action log; append-only. |
| `pending_approval` | Base | id, action, requested_by, approved_by, status | Configurable two-person rule. |
| `region` | Base | id, name, endpoints, status | Region list handed out by the Login server. |

### Characters (region database)

| Table | Layer | Key columns | Notes |
| --- | --- | --- | --- |
| `character` | Base; `constellation` columns are game | id, account_id, name, created_at, ancestry (content number), appearance_id, constellation (nullable), constellation_hidden, deleted_at | Identity; rarely changes. |
| `appearance` | Base; crystal override rows are game | id, body_type, hairstyle, face_preset, ancestry parts, colours, sliders (JSONB) | Base rows (from `character.appearance_id`) and crystal override rows (null = use base). |
| `character_status` | Base; `equipped_crystal_id` is game | character_id, equipped_crystal_id, zone, instance, position, facing, hp, buffs (JSONB), cooldowns (JSONB), saved_at | Saved periodically and on handoff. |
| `character_owner` | Base | character_id, game_server_id, instance_id, version, claimed_at | Versioned ownership row (handoff and fencing); the Region server watches it for presence. |
| `character_progress` | Game | character_id, soul_xp, anima, anima_updated_at, anima_capacity | Immediate transactions. |
| `anima_hold` | Game | id, character_id, instance_id, amount, created_at, status | Escrow for locked content. |

### Classes, crystals, loadouts, gear and outfit sets (region database)

| Table | Layer | Key columns | Notes |
| --- | --- | --- | --- |
| `class_entry` | Game | id, character_id, class (content number), xp, highest_level, unlocked_at | The Crystal Archives; current level derived from XP. |
| `class_rune_mastery` | Game | class_entry_id, rune (content number), progress | Class Rune mastery lives on the class entry. |
| `crystal` | Game | item_id (1:1 with `item`), class_entry_id, acquired (bought or granted), loadout_id, gear_set_id, outfit_set_id, appearance_id (nullable override) | |
| `loadout` | Game | id, character_id, name | Survives a crystal's deletion. |
| `loadout_slot` | Game | loadout_id, index, kind (Skill or Rune), colour, content number, binding, locked | |
| `gear_set` / `outfit_set` | Game | id, character_id | Reference lists, not containers. |
| `set_slot` | Game | set_id, slot (body, ring, tool…), item_id, item_type | Unique (set_id, item_type). Slots read as empty when the item isn't in Storage; never cleared. |

### Items, containers and the ledger (region database)

| Table | Layer | Key columns | Notes |
| --- | --- | --- | --- |
| `item` | Base; `durability`, `quality` and game locations (house, companion, market listing) are game | id, type, quantity, holder_character_id, location (inventory, Storage, bag, ground, house, companion, market listing, mail, guild storage), container_id, slot, soulbound_to, durability, quality, crafter_id, dye (JSONB), created_at | |
| `item_rune_mastery` | Game | item_id, rune, progress | |
| `item_movement` | Base | id, at, entity_kind (item or companion), entity_id, type, quantity, from, to, reason, actor, instance | The ledger; monthly partitions (see `backend.md`). |

### Flags, collection, crafts and companions (region database)

| Table | Layer | Key columns | Notes |
| --- | --- | --- | --- |
| `learned_flag` | Base | character_id, flag, learned_at | Hairstyles, dyes, recipes, Rune unlocks, emotes, attunements, titles, quest progress, once-per-character items. |
| `item_collection` | Game | character_id, item_type, first_obtained_at | One entry per item type. |
| `craft_entry` | Base | character_id, craft, xp, highest_level | Like `class_entry`. |
| `auto_craft_unlock` | Game | character_id, item_type, unlocked_at | Fast crafting unlocked at the quality threshold. |
| `gathering_node_use` | Base | character_id, node, used_at | Per-player nodes; could expire like the chat buffer. |
| `companion` | Game | id, species, kind, name, holder_character_id, location (active, carried, stabled), slot, soulbound_to, xp, dye (JSONB), released_at | Mirrors `item`. |

### Social, market and housing (region database)

| Table | Layer | Key columns | Notes |
| --- | --- | --- | --- |
| `friendship` / `friend_request` | Base | character pair, created_at | Mutual. |
| `block` | Base | character_id, blocked_id | Also feeds moderator conflict-of-interest checks. |
| `guild` | Base | id, name (unique per region), leader_id, created_at | |
| `guild_rank` | Base | guild_id, rank, name, permissions | |
| `guild_member` | Base; one primary guild is game policy | guild_id, character_id, rank, joined_at, is_primary | Partial unique index: one primary per character. |
| `party` / `party_member` | Base | party id, leader; character_id (unique) | One party at a time. |
| `mail` | Base | id, from (null = system), to, subject, body, sent_at, read_at, expires_at | Attachments are items with `location = mail`. |
| `market_listing` | Game | id, seller_id, item_id, price_type, price_amount, listed_at, expires_at | Sales go to the ledger. |
| `house` | Game | id, owner (character or guild), zone | Housing and guild halls are instances. |
| `item_placement` | Game | item_id, position, rotation | Placed furniture. |

### World and moderation (region database)

| Table | Layer | Key columns | Notes |
| --- | --- | --- | --- |
| `game_server` | Base | id, address, status, capacity, heartbeat_at | Live game-server processes. |
| `instance` | Base; `constellation` is game | id, zone, kind (channel, dungeon, house), name, constellation, owner (party or house), game_server_id, status (starting, running, draining, closed), created_at, closed_at | Placement is one atomic write. |
| `report` | Base | id, reporter_id, reported_id, category, text, zone, instance, position, created_at, status, keep_evidence, resolved_at, resolved_by, outcome, sanction_id | History kept after its chat is deleted; `resolved_by` and `sanction_id` point into the account database as plain numbers. |
| `report_chat` | Base | report_id, at, sender, chat kind, text | |
| `chat_buffer` | Base | at, chat kind, sender, target, text | Hourly partitions, 24 hours. |
| `violation` | Base | id, character_id, kind, details (JSONB), at | Monthly partitions, about 6 months. |

## Open

- All areas have a first pass. The project lead turns these into the final schema.
- Quest state beyond flags (multi-step progress, counters) has no table yet.
- Settings (account and character client settings, likely JSONB) have no table yet.
- How the Region server watches `character_owner` for presence (PostgreSQL LISTEN/NOTIFY, logical replication, or polling) is open.
