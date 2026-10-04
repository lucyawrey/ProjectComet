# Database tables

Agent notes on table design, per area. **The project lead decides the final schema**; the table drafts here are input. Architecture-level decisions (account and region databases, conventions, ledger, registry, chat buffer) live in `backend.md`.

Depth for this phase: tables, key columns and relationships, and which rules the database enforces. No full DDL.

Starting material: the project lead's table list in `.claude/notes/+ Quick Notes.md` ("Database": Character, CharacterStatus, CharacterCraft, CharacterClass, CharacterAppearance, CharacterSkillset, CharacterGearset, CharacterOutfit, UnlockCollection, CompanionCollection, ItemCollection); the old schemas on the archive branches (`archive/dotnet-datacenter`: `docs/reference.sql`); the data-model suggestions under Considering in `backend.md`; FishMMO's ~30 entities (see `backend.md`, Research).

Areas, in discussion order: accounts and staff; characters; classes, crystals, loadouts, gear and outfit sets; items, containers and the ledger; flags, collection, crafts, companions; social (friends, guilds, constellations, mail, market); world (instances and placement); moderation (region side).

## Decided

### Accounts and staff (account database)

- **Accounts are built on ASP.NET Core Identity's tables** (2026-10-04, adopted from an agent suggestion), renamed to fit our conventions: password hashing, lockout, email confirmation and two-factor come built in.
- **Staff roles can be scoped to one region or to all regions** (2026-10-04, adopted from an agent suggestion).
- **Character select queries the region databases** (2026-10-04, adopted from an agent suggestion): the Login server asks each region database for the account's characters through the Data library, with no copied directory. A region that's down shows as unavailable.

### Characters (region database)

- **Character data is split by how often it changes** (2026-10-04, adopted from an agent suggestion): identity, status, ownership and progress are separate tables, so periodic saves and ownership claims only rewrite small rows (PostgreSQL writes a new row version on every update).
- **Anima is stored as an amount plus a timestamp** (2026-10-04, adopted from an agent suggestion); regeneration is computed when it's read or spent. Works whether or not Anima builds up offline (open in `classes.md`).
- **Character names are unique per region, ignoring case** (2026-10-04, adopted from an agent suggestion). **Soft-deleted characters hold their name for a retention period** before it's freed. Names can contain spaces, which count for uniqueness: trimmed, with no leading or trailing spaces and at most one space between words; length limits apply (project lead).
- **Base appearance is one JSONB document per character** (2026-10-04, adopted from an agent suggestion). Crystal appearance overrides belong to the classes area.

### Classes, crystals, loadouts, gear and outfit sets (region database)

- **A crystal is an `item` row plus a one-to-one `crystal` row** (2026-10-04, adopted from an agent suggestion). Location, owner and soulbinding come from the item; the crystal row holds the class entry, bought or granted, loadout, gear and outfit sets, and the appearance override.
- **Unique-equipped is enforced by the database** (2026-10-04, adopted from an agent suggestion): `set_slot` also stores the item's type (safe, since an item's type never changes), with a unique index on (set, item type).
- **Loadout slots are rows** (2026-10-04, adopted from an agent suggestion), with foreign keys to the content lookup tables.

### Items, containers and the ledger (region database)

- **One `item` table for every container** (2026-10-04, adopted from an agent suggestion): one row is one physical item or stack in exactly one place (location, container_id, slot), so being in two places is impossible by construction. A partial unique index on (location, container, slot) protects slotted positions; Storage has no slots. Moves within one owner are plain updates and aren't logged; owner changes also write a ledger row in the same transaction.
- **Dropped items are persisted** (2026-10-04, adopted from an agent suggestion): location = ground, with the instance. Pickups are ordinary owner changes in the ledger; despawn and channel restarts delete them (soulbound items return). Dungeon instance items and temporary structures stay in memory.
- **Per-item state is columns** (durability, quality, crafter signature, soulbound_to), with JSONB only for dye colours, and **gear Rune mastery in its own table** (`item_rune_mastery`) (2026-10-04, adopted from an agent suggestion).
- Storage is per character for now (multiple characters are ignored in this phase).

## Draft tables (agent proposals)

### Accounts and staff (account database)

| Table | Key columns | Notes |
| --- | --- | --- |
| `account` (Identity user) | id, email, created_at, kind (player or staff), status | Staff powers live on separate staff accounts. |
| Identity login and token tables | password hash, external logins, tokens | Renamed to snake_case. |
| `staff_role` | account_id, role (moderator, GM, admin), region_id (null = all), granted_by, granted_at, revoked_at | Instant revocation sets revoked_at. |
| `sanction` | id, account_id, kind, reason, starts_at, expires_at (null = permanent), issued_by, region_id and character_id involved, report_id, lifted_at, lifted_by | Account-wide. |
| `appeal` | id, sanction_id (unique), text, status, reviewed_by | Reviewer must differ from the issuer (app rule or trigger). |
| `moderator_action` | id, actor_account_id, action, target, region_id, details (JSONB), at | The moderator action log; append-only. |
| `pending_approval` | id, action, requested_by, approved_by, status | Configurable two-person rule. |
| `region` | id, name, endpoints, status | Region list handed out by the Login server. |

### Characters (region database)

| Table | Key columns | Notes |
| --- | --- | --- |
| `character` | id, account_id, name, created_at, ancestry (content number), body_type, constellation (nullable), constellation_hidden, deleted_at | Identity; rarely changes. |
| `character_appearance` | character_id, appearance (JSONB) | Base form, read as a whole. |
| `character_status` | character_id, equipped_crystal_id, zone, instance, position, facing, hp, buffs (JSONB), cooldowns (JSONB), saved_at | Saved periodically and on handoff. |
| `character_owner` | character_id, game_server_id, instance_id, version, claimed_at | Versioned ownership row (handoff and fencing); the Region server watches it for presence. |
| `character_progress` | character_id, soul_xp, anima, anima_updated_at, anima_capacity | Immediate transactions. |
| `anima_hold` | id, character_id, instance_id, amount, created_at, status | Escrow for locked content. |

### Classes, crystals, loadouts, gear and outfit sets (region database)

| Table | Key columns | Notes |
| --- | --- | --- |
| `class_entry` | id, character_id, class (content number), xp, highest_level, unlocked_at | The Crystal Archives; current level derived from XP. |
| `class_rune_mastery` | class_entry_id, rune (content number), progress | Class Rune mastery lives on the class entry. |
| `crystal` | item_id (1:1 with `item`), class_entry_id, acquired (bought or granted), loadout_id, gear_set_id, outfit_set_id, appearance_override (JSONB) | |
| `loadout` | id, character_id, name | Survives a crystal's deletion. |
| `loadout_slot` | loadout_id, index, kind (Skill or Rune), colour, content number, binding, locked | |
| `gear_set` / `outfit_set` | id, character_id | Reference lists, not containers. |
| `set_slot` | set_id, slot (body, ring, tool…), item_id, item_type | Unique (set_id, item_type). Slots read as empty when the item isn't in Storage; never cleared. |

### Items, containers and the ledger (region database)

| Table | Key columns | Notes |
| --- | --- | --- |
| `item` | id, type, quantity, holder_character_id, location (inventory, Storage, bag, ground, house, market listing, mail, guild storage), container_id, slot, soulbound_to, durability, quality, crafter_id, dye (JSONB), created_at | |
| `item_rune_mastery` | item_id, rune, progress | |
| `item_movement` | id, at, item_id, item_type, quantity, from, to, reason, actor, instance | The ledger; monthly partitions (see `backend.md`). |

## Open

- Remaining areas (see the order above).
