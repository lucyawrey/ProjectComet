-- Add migration script here

-- Game Info Schema
CREATE TABLE game_info (
    id                 INTEGER  NOT NULL PRIMARY KEY CHECK (id = 0),
    created_at         INTEGER  DEFAULT (unixepoch()) NOT NULL, -- Unix timestamp in seconds
    updated_at         INTEGER  DEFAULT (unixepoch()) NOT NULL, -- Unix timestamp in seconds
    game_id            TEXT     NOT NULL,
    game_version       TEXT     NOT NULL,
    supported_client_game_ids   TEXT  DEFAULT "[]" NOT NULL, -- JSON array
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
    creator_user_handle         INTEGER -- NULL when is_user_generated is false
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
    creator_user_handle         INTEGER, -- NULL when is_user_generated is false
    UNIQUE(name, content_type)
) STRICT;
-- End Game Content Service Schema
