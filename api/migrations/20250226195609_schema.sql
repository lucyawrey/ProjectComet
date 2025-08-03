-- Initial Migration for Creating Database Schema

-- Game Info Schema
CREATE TABLE game_info (
    id                 INTEGER  NOT NULL PRIMARY KEY CHECK (id = 0),
    created_at         INTEGER  DEFAULT (unixepoch()) NOT NULL, -- Unix timestamp in seconds
    updated_at         INTEGER  DEFAULT (unixepoch()) NOT NULL, -- Unix timestamp in seconds
    game_id            TEXT     NOT NULL,
    game_version       TEXT     NOT NULL,
    supported_client_game_ids   TEXT  DEFAULT "[]" NOT NULL, -- JSON array
    supported_client_game_versions    TEXT  DEFAULT "[]" NOT NULL, -- JSON array
    game_display_name  TEXT     NOT NULL
) STRICT;
-- End Game Ino Schema

-- Game Content Service Schema
CREATE TABLE asset (
    id                 INTEGER  NOT NULL PRIMARY KEY, -- Snowflake ID, alias of rowid
    updated_at         INTEGER  DEFAULT (unixepoch()) NOT NULL, -- Unix timestamp in seconds
    path               TEXT     NOT NULL UNIQUE COLLATE NOCASE, -- Case insensitive indexed name, should be a valid unix path with no spaces, used in the virtual filesystem
    file_type          TEXT     NOT NULL, -- Must be a valid MIME type, needed to understand `blob`
    data               ANY      NOT NULL, -- Binary blob or string representation of file saved to virtual filesystem
    size               INTEGER  NOT NULL, -- Size of data in bytes
    is_user_generated  INTEGER  DEFAULT FALSE NOT NULL, -- Boolean
    creator_user_id    INTEGER  REFERENCES user(id) -- NULL when is_user_generated is false
) STRICT;

CREATE TABLE content (
    id                 INTEGER  NOT NULL PRIMARY KEY, -- Snowflake ID, alias of rowid
    updated_at         INTEGER  DEFAULT (unixepoch()) NOT NULL, -- Unix timestamp in seconds
    name               TEXT     NOT NULL COLLATE NOCASE, -- Case insensitive indexed name, should be between 2 and 30 legal characters with at most 4 spaces
    content_type       INTEGER  DEFAULT 0 NOT NULL, -- Enum(Item=0, Companion=1, Unlock=2)
    content_subtype    INTEGER  DEFAULT 0 NOT NULL, -- Enum(Variable=X)
    data               TEXT     DEFAULT "{}" NOT NULL,
    asset_id_0         INTEGER  REFERENCES asset(id),
    asset_id_1         INTEGER  REFERENCES asset(id),
    asset_id_2         INTEGER  REFERENCES asset(id),
    asset_id_3         INTEGER  REFERENCES asset(id),
    asset_id_4         INTEGER  REFERENCES asset(id),
    is_user_generated  INTEGER  DEFAULT FALSE NOT NULL, -- Boolean
    base_content_id    INTEGER  REFERENCES content(id), -- NULL when is_user_generated is false, Non user generated base content that `is_user_generated` content inherits from.
    creator_user_id    INTEGER  REFERENCES user(id), -- NULL when is_user_generated is false
    UNIQUE(name, content_type)
) STRICT;
-- End Game Content Service Schema

-- User Service Schema
CREATE TABLE user (
    id                 INTEGER  NOT NULL PRIMARY KEY, -- Snowflake ID, alias of rowid
    handle             INTEGER  NOT NULL UNIQUE, -- Secondary user ID used for anonymity and friend requests.
    updated_at         INTEGER  DEFAULT (unixepoch()) NOT NULL, -- Unix timestamp in seconds, updates when a login method is updated
    username           TEXT     NOT NULL UNIQUE COLLATE NOCASE, -- Case insensitive indexed name, should be between 2 and 20 legal characters with no whitespace
    role               INTEGER  DEFAULT 0 NOT NULL -- Enum(NewPlayer=0, Player=1, MembershipPlayer=2, GameModerator=3, GameAdministrator=4)
) STRICT;

CREATE TABLE user_password (
    id                 INTEGER  NOT NULL PRIMARY KEY, -- Snowflake ID, alias of rowid
    user_id            INTEGER  NOT NULL UNIQUE REFERENCES user(id),
    password_hash      TEXT     NOT NULL
) STRICT;

CREATE TABLE user_session (
    id                 TEXT     NOT NULL PRIMARY KEY, -- Hash of the generated user session token
    expires_at         INTEGER  NOT NULL, -- Unix timestamp in seconds a certain time in the future
    user_id            INTEGER  NOT NULL REFERENCES user(id)
) STRICT;

CREATE TABLE user_recovery_code (
    id                 TEXT     NOT NULL PRIMARY KEY, -- Hash of the generated user account recovery code
    user_id            INTEGER  NOT NULL UNIQUE REFERENCES user(id),
    is_temporary       INTEGER  DEFAULT FALSE NOT NULL -- Boolean
) STRICT;
-- End User Service Schema

-- Administration Service Schema
CREATE TABLE access_token (
    id                 INTEGER  NOT NULL PRIMARY KEY, -- Snowflake ID, alias of rowid
    access_token_hash  TEXT     NOT NULL, -- Hash of the generated access token. Token format is: `default|server:gameserverid|admin_IdBase32Representation_secret`
    access_level       INTEGER  NOT NULL, -- Enum(Default=0, GameServer=1, Administrator=2)
    game_server_id     TEXT     REFERENCES game_server(id), -- NULL when access_level is not `GameServer`
    expires_at         INTEGER -- Unix timestamp in seconds a certain time in the future. If NULL, token does not expire
) STRICT;

CREATE TABLE game_server (
    id                 TEXT     NOT NULL PRIMARY KEY COLLATE NOCASE, -- Case insensitive String ID, should be lowercase, short, and have no whitespace or special characters
    created_at         INTEGER  DEFAULT (unixepoch()) NOT NULL, -- Unix timestamp in seconds
    updated_at         INTEGER  DEFAULT (unixepoch()) NOT NULL, -- Unix timestamp in seconds
    region_code        TEXT     NOT NULL, -- Server location represented by a timezone, using case sensitive tz database identifiers. Ex: 'US/Eastern'
    display_name       TEXT     NOT NULL -- Server name for end user display
) STRICT;

CREATE TABLE world (
    id                 TEXT     NOT NULL PRIMARY KEY COLLATE NOCASE, -- Case insensitive String ID, should be lowercase, short, and have no whitespace or special characters
    created_at         INTEGER  DEFAULT (unixepoch()) NOT NULL, -- Unix timestamp in seconds
    updated_at         INTEGER  DEFAULT (unixepoch()) NOT NULL, -- Unix timestamp in seconds
    game_server_id     TEXT     NOT NULL REFERENCES game_server(id),
    display_name       TEXT     NOT NULL -- World name for end user display
) STRICT;
CREATE INDEX world_game_server_id_index ON game_server(id);
-- End Administration Service Schema

-- Game Data Service Schema
CREATE TABLE character (
    id                 INTEGER  NOT NULL PRIMARY KEY, -- Snowflake ID, alias of rowid
    handle             INTEGER  NOT NULL UNIQUE, -- Secondary character ID used for anonymity and friend requests.
    updated_at         INTEGER  DEFAULT (unixepoch()) NOT NULL, -- Unix timestamp in seconds
    name               TEXT     NOT NULL COLLATE NOCASE, -- Case insensitive indexed name, should be between 2 and 30 legal characters with at most 4 spaces
    role               INTEGER  DEFAULT 0 NOT NULL, -- Enum(NewPlayer=0, Player=1, MembershipPlayer=2, GameModerator=3, GameAdministrator=4)
    home_world_id      TEXT     NOT NULL REFERENCES world(id),
    user_id            INTEGER  NOT NULL REFERENCES user(id),
    ancestry           INTEGER  DEFAULT 0 NOT NULL, -- Enum(Cat=0, Human=1)
    gender             INTEGER  DEFAULT 0 NOT NULL, -- Enum(Neutral=0, Feminine=1, Masculine=2, None=3, Fluid=4, Advanced=5)
    customization      TEXT     DEFAULT '{"gender_details":{}}' NOT NULL, -- JSON object
    data               TEXT     DEFAULT '{"character_history":{},"npc_relationships":{}}' NOT NULL, -- JSON object
    UNIQUE(name, home_world_id)
) STRICT;

CREATE TABLE game_options (
    id                 INTEGER  NOT NULL PRIMARY KEY, -- Snowflake ID, alias of rowid
    updated_at         INTEGER  DEFAULT (unixepoch()) NOT NULL, -- Unix timestamp in seconds
    game_options_type  INTEGER  DEFAULT 0 NOT NULL, -- Enum(User=0, Character=1, System=2)
    data               TEXT     DEFAULT "{}" NOT NULL, -- JSON object
    user_id            INTEGER  UNIQUE REFERENCES user(id),
    character_id       INTEGER  UNIQUE REFERENCES character(id)
) STRICT;

CREATE TABLE character_status (
    id                 INTEGER  NOT NULL PRIMARY KEY, -- Snowflake ID, alias of rowid
    updated_at         INTEGER  DEFAULT (unixepoch()) NOT NULL, -- Unix timestamp in seconds
    character_id       INTEGER  NOT NULL UNIQUE REFERENCES character(id),
    active_class_id    INTEGER  NOT NULL REFERENCES class(id),
    statistics         TEXT     DEFAULT "{}" NOT NULL, -- JSON object
    data               TEXT     DEFAULT "{}" NOT NULL, -- JSON object
    base_gearset_id             INTEGER  NOT NULL REFERENCES gearset(id), -- Base Gearset that will be used if there is no 'active_gearset' and that empty slots will fall through to. Associated with directly equipped gear and BaseClass 0.
    base_outfit_id              INTEGER  NOT NULL REFERENCES outfit(id), -- Base Outfit that will be used if there is no 'active_outfit' and that empty slots will fall through to. Associated with directly applied glamours.
    active_gearset_id           INTEGER  REFERENCES gearset(id), -- NULL if no overlayed gearset is active. Overlayed gearsets are usually associated with classes.
    active_outfit_id            INTEGER  REFERENCES outfit(id), -- NULL if no outfit is active from any source (ClassItem, OutfitItem, Transmog, etc.)
    active_class_item_id        INTEGER  REFERENCES item(id) -- NULL if active class is being used without a ClassItem (BaseClass is active, no ClassItems in game, etc.)
) STRICT;

CREATE TABLE class (
    id                 INTEGER  NOT NULL PRIMARY KEY, -- Snowflake ID, alias of rowid
    updated_at         INTEGER  DEFAULT (unixepoch()) NOT NULL, -- Unix timestamp in seconds
    character_id       INTEGER  NOT NULL REFERENCES character(id),
    class_content_id   INTEGER  DEFAULT 0 NOT NULL REFERENCES content(id), -- Snowflake ID or '0', '0' is for the BaseClass which has special rules.
    experience         INTEGER  DEFAULT 0 NOT NULL,
    level              INTEGER  DEFAULT 1 NOT NULL,
    is_unlocked        INTEGER  DEFAULT TRUE NOT NULL, -- Boolean
    statistics         TEXT     DEFAULT "{}" NOT NULL, -- JSON object
    data               TEXT     DEFAULT "{}" NOT NULL, -- JSON object
    class_item_id      INTEGER  REFERENCES item(id), -- NULL if game has no class containers or if this class does not require one
    UNIQUE(character_id, class_content_id)
) STRICT;

CREATE TABLE gearset (
    id                 INTEGER  NOT NULL PRIMARY KEY, -- Snowflake ID, alias of rowid
    updated_at         INTEGER  DEFAULT (unixepoch()) NOT NULL, -- Unix timestamp in seconds
    character_id       INTEGER  NOT NULL REFERENCES character(id),
    name               TEXT     DEFAULT "BASE" NOT NULL COLLATE NOCASE, -- Case insensitive indexed name, special value BASE means this is the default gearset that is directly modified when equipping gear.
    statistics         TEXT     DEFAULT "{}" NOT NULL, -- JSON object
    linked_class_id    INTEGER  REFERENCES outfit(id), -- NULL if no Class is linked.
    linked_outfit_id   INTEGER  REFERENCES outfit(id), -- NULL if no Outfit is linked.
    item_id_0          INTEGER  REFERENCES item(id), -- NULL if slot is empty or not implemented.
    item_id_1          INTEGER  REFERENCES item(id), -- NULL if slot is empty or not implemented.
    item_id_2          INTEGER  REFERENCES item(id), -- NULL if slot is empty or not implemented.
    item_id_3          INTEGER  REFERENCES item(id), -- NULL if slot is empty or not implemented.
    item_id_4          INTEGER  REFERENCES item(id), -- NULL if slot is empty or not implemented.
    item_id_5          INTEGER  REFERENCES item(id), -- NULL if slot is empty or not implemented.
    item_id_6          INTEGER  REFERENCES item(id), -- NULL if slot is empty or not implemented.
    item_id_7          INTEGER  REFERENCES item(id), -- NULL if slot is empty or not implemented.
    item_id_8          INTEGER  REFERENCES item(id), -- NULL if slot is empty or not implemented.
    item_id_9          INTEGER  REFERENCES item(id), -- NULL if slot is empty or not implemented.
    item_id_10         INTEGER  REFERENCES item(id), -- NULL if slot is empty or not implemented.
    item_id_11         INTEGER  REFERENCES item(id), -- NULL if slot is empty or not implemented.
    item_id_12         INTEGER  REFERENCES item(id), -- NULL if slot is empty or not implemented.
    item_id_13         INTEGER  REFERENCES item(id), -- NULL if slot is empty or not implemented.
    item_id_14         INTEGER  REFERENCES item(id), -- NULL if slot is empty or not implemented.
    item_id_15         INTEGER  REFERENCES item(id), -- NULL if slot is empty or not implemented.
    UNIQUE(character_id, name)
) STRICT;

CREATE TABLE outfit (
    id                 INTEGER  NOT NULL PRIMARY KEY, -- Snowflake ID, alias of rowid
    updated_at         INTEGER  DEFAULT (unixepoch()) NOT NULL, -- Unix timestamp in seconds
    character_id       INTEGER  NOT NULL REFERENCES character(id),
    name               TEXT     DEFAULT "BASE" NOT NULL COLLATE NOCASE, -- Case insensitive indexed name, special value BASE means this is the default outfit that is directly modified when applying glamours
    customization      TEXT, -- JSON object, NULL when outfit includes no character customization overrides
    data      TEXT, -- JSON object, NULL when outfit includes no extra outfit data (such as gear dye clors)
    item_content_id_0  INTEGER  REFERENCES content(id), -- NULL if slot is empty or not implemented.
    item_content_id_1  INTEGER  REFERENCES content(id), -- NULL if slot is empty or not implemented.
    item_content_id_2  INTEGER  REFERENCES content(id), -- NULL if slot is empty or not implemented.
    item_content_id_3  INTEGER  REFERENCES content(id), -- NULL if slot is empty or not implemented.
    item_content_id_4  INTEGER  REFERENCES content(id), -- NULL if slot is empty or not implemented.
    item_content_id_5  INTEGER  REFERENCES content(id), -- NULL if slot is empty or not implemented.
    item_content_id_6  INTEGER  REFERENCES content(id), -- NULL if slot is empty or not implemented.
    item_content_id_7  INTEGER  REFERENCES content(id), -- NULL if slot is empty or not implemented.
    item_content_id_8  INTEGER  REFERENCES content(id), -- NULL if slot is empty or not implemented.
    item_content_id_9  INTEGER  REFERENCES content(id), -- NULL if slot is empty or not implemented.
    item_content_id_10 INTEGER  REFERENCES content(id), -- NULL if slot is empty or not implemented.
    item_content_id_11 INTEGER  REFERENCES content(id), -- NULL if slot is empty or not implemented.
    item_content_id_12 INTEGER  REFERENCES content(id), -- NULL if slot is empty or not implemented.
    item_content_id_13 INTEGER  REFERENCES content(id), -- NULL if slot is empty or not implemented.
    item_content_id_14 INTEGER  REFERENCES content(id), -- NULL if slot is empty or not implemented.
    item_content_id_15 INTEGER  REFERENCES content(id), -- NULL if slot is empty or not implemented.
    UNIQUE(character_id, name)
) STRICT;

CREATE TABLE friendship (
    id                 INTEGER  NOT NULL PRIMARY KEY, -- Snowflake ID, alias of rowid
    character_id_0     INTEGER  NOT NULL REFERENCES character(id),
    character_id_1     INTEGER  NOT NULL REFERENCES character(id),
    UNIQUE(character_id_0, character_id_1)
) STRICT;

CREATE TABLE guild (
    id                 INTEGER  NOT NULL PRIMARY KEY, -- Snowflake ID, alias of rowid
    updated_at         INTEGER  DEFAULT (unixepoch()) NOT NULL, -- Unix timestamp in seconds
    name               TEXT     NOT NULL COLLATE NOCASE, -- Case insensitive indexed name, should be between 2 and 30 legal characters with at most 4 spaces
    home_world_id      TEXT     NOT NULL REFERENCES world(id),
    UNIQUE(name, home_world_id)
) STRICT;

CREATE TABLE guild_membership (
    id                 INTEGER  NOT NULL PRIMARY KEY, -- Snowflake ID, alias of rowid
    guild_id           INTEGER  NOT NULL REFERENCES guild(id),
    character_id       INTEGER  NOT NULL REFERENCES character(id),
    role               INTEGER  DEFAULT 0 NOT NULL, -- Enum(Member=0, Trustee=1)
    UNIQUE(guild_id, character_id)
) STRICT;

CREATE TABLE item (
    id                 INTEGER  NOT NULL PRIMARY KEY, -- Snowflake ID, alias of rowid
    character_id       INTEGER  NOT NULL REFERENCES character(id),
    item_content_id    INTEGER  NOT NULL REFERENCES content(id), -- Never stores user generated content, stores it's base_content instead.
    quantity           INTEGER  DEFAULT 1 NOT NULL, -- Tracks quantity of non-unique, non container item. Quanitity cannot exeed an item's `stack_size` unless it is in the Box. Otherwise, a second instance will need to be made for a new stack. Item instances can be merged if they have the same `location`, `quality`, `craft_character_id`, no `data`, and the new total quantity is legal. Instances of `is_unique` items can never merge or have a quantity other than 1 even in the Box. When two instances are merged the one target location is prioritized first and the one with the older ID is prioritized next. The other instance is deleted. Instances with quanitity 0 are deleted. Tracks number of contained instances for ClassItem and InventoryContainers (when in Inventory).
    location           INTEGER  DEFAULT 3 NOT NULL, -- Enum(Other=0, Dropped=1, NpcMerchant=2, Market=3, Inventory=4, Equipped=5, InventoryContainer=6, ClassItem=7, Box=8)
    quality            INTEGER  DEFAULT 0 NOT NULL, -- Enum(Normal=0, Silver=1, Gold=2)
    container_item_id  INTEGER  REFERENCES item(id), -- NULL when item can't have a signature or wasn't crafted by a user
    extra_character_id_0        INTEGER  REFERENCES character(id), -- NULL when item can't have a signature or wasn't crafted by a user
    extra_character_id_1        INTEGER  REFERENCES character(id), -- NULL when item can't be or currently is not Soulbound. Soulbound items will always return to bound character
    data                        TEXT, -- JSON object, NULL when item can't have or currently does not have data, Non-NULL data prevents stacking
    extra_content_id            INTEGER  REFERENCES content(id) -- NULL when item type does not need another content referance. Mostly used for user generated content.
) STRICT;
CREATE INDEX item_character_id_index ON item(character_id);

CREATE TABLE item_collection_entry (
    id                 INTEGER  NOT NULL PRIMARY KEY, -- Snowflake ID, alias of rowid
    character_id       INTEGER  NOT NULL REFERENCES character(id),
    item_content_id    INTEGER  NOT NULL REFERENCES content(id),
    location           INTEGER  DEFAULT 0 NOT NULL, -- Enum(NotTracked=0, Soulbound=1, OnCharacter=2, ClassItem=3, Box=4), a location of NotTracked means this character had the item at some point (because the collection entry exists) but either no longer owns at least 1 instance of it or chose to not add it to their collection. NotTracked entries will have their `quality` set to Normal and will not be updated at all until the item is anually added to the collection. These lost items may be able to be duplicated or reobtained. A location of Soulbound means the item is Soulbound but not currently in the binder's possession.
    quality            INTEGER  DEFAULT 0 NOT NULL, -- Enum(Normal=0, Silver=1, Gold=2), highest quality of item in the highest priority localtion
    UNIQUE(character_id, item_content_id)
) STRICT;
CREATE INDEX item_collection_entry_character_id_index ON item_collection_entry(character_id);

CREATE TABLE companion_collection_entry (
    id                 INTEGER  NOT NULL PRIMARY KEY, -- Snowflake ID, alias of rowid
    character_id       INTEGER  NOT NULL REFERENCES character(id),
    companion_content_id        INTEGER  NOT NULL REFERENCES content(id),
    data               TEXT, -- JSON object, NULL when companion does not have special data (e.g. modified color or name)
    UNIQUE(character_id, companion_content_id)
) STRICT;
CREATE INDEX companion_collection_entry_character_id_index ON companion_collection_entry(character_id);

CREATE TABLE collection_entry (
    id                 INTEGER  NOT NULL PRIMARY KEY, -- Snowflake ID, alias of rowid
    character_id       INTEGER  NOT NULL REFERENCES character(id),
    content_id         INTEGER  NOT NULL REFERENCES content(id),
    UNIQUE(character_id, content_id)
) STRICT;
CREATE INDEX collection_entry_character_id_index ON collection_entry(character_id);
-- End Game Data Service Schema
