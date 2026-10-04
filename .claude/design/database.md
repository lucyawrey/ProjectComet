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

## Open

- Remaining areas (see the order above).
