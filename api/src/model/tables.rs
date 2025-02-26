use super::fields::{
    AccessLevel, AssetData, CharacterAncestry, CharacterData, CharacterGender, CharacterStatusData,
    ClassData, CompanionCollectionEntryData, ContentData, ContentSubtype, ContentType,
    Customization, GameOptionsData, GameOptionsType, GuildRole, ItemCollectionEntryLocation,
    ItemInstanceData, ItemLocation, ItemQuality, OutfitData, Role, Statistics,
};
use chrono::NaiveDateTime;
use sqlx::{types::Json, FromRow};

#[derive(Debug, FromRow)]
pub struct GameInfo {
    pub id: i64,
    pub created_at: NaiveDateTime, // Unix timestamp in seconds
    pub updated_at: NaiveDateTime, // Unix timestamp in seconds
    pub game_id: String,
    pub game_version: String,
    pub supported_client_game_ids: Json<Vec<String>>,
    pub game_display_name: String,
}

/* User Service Schema */
#[derive(Debug, FromRow)]
pub struct User {
    pub id: i64,     // Should not be exposed to other clients. Snowflake ID, alias of rowid
    pub handle: i64, // Secondary user ID used for anonymity and friend requests.
    pub updated_at: NaiveDateTime, // Unix timestamp in seconds
    pub username: String, // Should not be exposed to other clients. // Unique no case
    pub role: Role,
}

#[derive(Debug, FromRow)]
pub struct UserPassword {
    pub id: i64,      // Snowflake ID, alias of rowid
    pub user_id: i64, // Should not be exposed to the client. Snowflake ID, referances a `User`
    pub password_hash: String,
}

#[derive(Debug, FromRow)]
pub struct UserSession {
    pub id: String, // Should not be exposed to the client. Hash of the generated user session token
    pub expires_at: NaiveDateTime, // Unix timestamp in seconds a certain time in the future
    pub user_id: i64, // Should not be exposed to the client. Snowflake ID, referances a `User`
}

#[derive(Debug, FromRow)]
pub struct UserRecoveryCode {
    pub id: String, // Should not be exposed to the client. Hash of the generated user account recovery code
    pub user_id: i64, // Should not be exposed to the client. Snowflake ID, referances a `User`
    pub is_temporary: bool,
}
/* End User Service Schema */

/* Administration Service Schema */
#[derive(Debug, FromRow)]
pub struct AccessToken {
    pub id: i64,                   // Snowflake ID, alias of rowid
    pub access_token_hash: String, // Hash of the generated access token. Token format is: `default|server:gameserverid|admin_IdBase32Representation_secret`
    pub access_level: AccessLevel,
    pub game_server_id: String,  // String ID, referances a 'GameServer'
    pub expires_at: Option<i64>, // Unix timestamp in seconds a certain time in the future
}

#[derive(Debug, FromRow)]
pub struct GameServer {
    pub id: String, // Case insensitive String ID, should be input in lowercase with no spaces
    pub created_at: NaiveDateTime, // Unix timestamp in seconds
    pub updated_at: NaiveDateTime, // Unix timestamp in seconds
    pub region_code: String, // Server location represented by a timezone, using case sensitive tz database identifiers. Ex: 'US/Eastern'
    pub display_name: String, // Server name for end user display
}

#[derive(Debug, FromRow)]
pub struct World {
    pub id: String, // Case insensitive String ID, should be input in lowercase with no spaces
    pub created_at: NaiveDateTime, // Unix timestamp in seconds
    pub updated_at: NaiveDateTime, // Unix timestamp in seconds
    pub game_server_id: String, // String ID, referances a 'GameServer'
    pub display_name: String, // Server name for end user display
}
/* End Administration Service Schema */

/* Game Data Service Schema */
#[derive(Debug, FromRow)]
pub struct Character {
    pub id: i64,                   // Snowflake ID, alias of rowid
    pub handle: i64,               // Secondary character ID used for anonymity and friend requests.
    pub updated_at: NaiveDateTime, // Unix timestamp in seconds
    pub name: String, // Unique no case with `home_world_id`. Character is initially created with a silly random name.
    pub role: Role, // Same type as `User.role`, `Character.role` can be a lower rank than `User.role` but should never be higher than it.
    pub home_world_id: String, // String ID, referances a 'World'
    pub user_id: i64, // Should not be exposed to the client. Snowflake ID, referances a `User`
    pub ancestry: CharacterAncestry,
    pub gender: CharacterGender,
    pub customization: Json<Customization>,
    pub data: Json<CharacterData>,
}

#[derive(Debug, FromRow)]
pub struct GameOptions {
    pub id: i64,                   // Snowflake ID, alias of rowid
    pub updated_at: NaiveDateTime, // Unix timestamp in seconds
    pub game_options_type: GameOptionsType,
    pub data: Json<GameOptionsData>,
    pub user_id: Option<i64>, // Should not be exposed to the client. Snowflake ID, referances a `User`
    pub character_id: Option<i64>, // Snowflake ID, referances a `Character`
}

#[derive(Debug, FromRow)]
pub struct CharacterStatus {
    pub id: i64,                   // Snowflake ID, alias of rowid
    pub updated_at: NaiveDateTime, // Unix timestamp in seconds
    pub character_id: i64,         // Snowflake ID, referances a `Character`
    pub active_class_id: i64,      // Snowflake ID, referances a `Class`
    pub statistics: Json<Statistics>,
    pub data: Json<CharacterStatusData>,
    pub base_gearset_id: i64, // Snowflake ID, referances a `Gearset`
    pub base_outfit_id: i64,  // Snowflake ID, referances an `Item`
    pub active_gearset_id: Option<i64>, // Snowflake ID, referances a `Gearset`
    pub active_outfit_id: Option<i64>, // Snowflake ID, referances an `Item`
    pub active_class_item_id: Option<i64>, // Snowflake ID, referances an `Item`
}

#[derive(Debug, FromRow)]
pub struct Class {
    pub id: i64,                   // Snowflake ID, alias of rowid
    pub updated_at: NaiveDateTime, // Unix timestamp in seconds
    pub character_id: i64,         // Snowflake ID, referances a `Character`
    pub class_content_id: i64,     // Snowflake ID, referances a `Content`
    pub exsperience: i64,
    pub level: i64,
    pub is_unlocked: bool,
    pub statistics: Json<Statistics>,
    pub data: Json<ClassData>,
    pub class_item_id: Option<i64>, // Snowflake ID, referances an `Item`
}

#[derive(Debug, FromRow)]
pub struct Gearset {
    pub id: i64,                   // Snowflake ID, alias of rowid
    pub updated_at: NaiveDateTime, // Unix timestamp in seconds
    pub character_id: i64,         // Snowflake ID, referances a `Character`
    pub name: String, // Case insensitive indexed name, special value BASE means this is the default gearset that is directly modified when equipping gear
    pub statistics: Json<Statistics>,
    pub linked_class_id: Option<i64>, // Snowflake ID, referances a `Class`
    pub linked_outfit_id: Option<i64>, // Snowflake ID, referances a `Outfit`
    pub item_id: [Option<i64>; 16],   // Snowflake ID array, referances multiple `Item`s
}

#[derive(Debug, FromRow)]
pub struct Outfit {
    pub id: i64,                   // Snowflake ID, alias of rowid
    pub updated_at: NaiveDateTime, // Unix timestamp in seconds
    pub character_id: i64,         // Snowflake ID, referances a `Character`
    pub name: String, // Case insensitive indexed name, special value BASE means this is the default gearset that is directly modified when equipping gear
    pub customization: Json<Customization>,
    pub data: Json<OutfitData>,
    pub item_content_id: [Option<i64>; 16], // Snowflake ID array, referances multiple `Content`s
}

#[derive(Debug, FromRow)]
pub struct Friendship {
    pub id: i64,             // Snowflake ID, alias of rowid
    pub character_id_0: i64, // Snowflake ID, referances a `Character`
    pub character_id_1: i64, // Snowflake ID, referances a `Character`
}

#[derive(Debug, FromRow)]
pub struct Guild {
    pub id: i64,                   // Snowflake ID, alias of rowid
    pub updated_at: NaiveDateTime, // Unix timestamp in seconds
    pub name: String,              // Unique no case
    pub home_world_id: String,     // String ID, referances a 'World'
}

#[derive(Debug, FromRow)]
pub struct GuildMembership {
    pub id: i64,           // Snowflake ID, alias of rowid
    pub guild_id: i64,     // Snowflake ID, referances a `Guild`
    pub character_id: i64, // Snowflake ID, referances a `Character`
    pub role: GuildRole,
}

#[derive(Debug, FromRow)]
pub struct Item {
    pub id: i64,              // Snowflake ID, alias of rowid
    pub character_id: i64,    // Snowflake ID, referances a `Character`
    pub item_content_id: i64, // Snowflake ID, referances a `Content`
    pub quantity: i64, // Quantitiy can only be above item's `stack_size` when in a box. `is_unique` items never stack. Items can only stack if they have the same `location`, `quality`, `craft_character_id` and no `instance_data`.
    pub location: ItemLocation,
    pub quality: ItemQuality,
    pub container_item_id: Option<i64>, // Snowflake ID, referances a `Character`, None when item can't have a signature or wasn't crafted by a character
    pub extra_character_id_0: Option<i64>, // Snowflake ID, referances a `Character`, usually used for crafted item signatures
    pub extra_character_id_1: Option<i64>, // Snowflake ID, referances a `Character`, usually used for tracking who is bound to an item
    pub data: Option<Json<ItemInstanceData>>, // None when item can't have or currently does not have data, Some data prevents stacking
    pub extra_content_id: Option<i64>, // Snowflake ID, referances a `Content`, None when item type does not need another content referance. Mostly used for user generated content.
}

#[derive(Debug, FromRow)]
pub struct ItemCollectionEntry {
    pub id: i64,              // Snowflake ID, alias of rowid
    pub character_id: i64,    // Snowflake ID, referances a `Character`
    pub item_content_id: i64, // Snowflake ID, referances an `Item`
    pub location: ItemCollectionEntryLocation,
    pub quality: ItemQuality,
}

#[derive(Debug, FromRow)]
pub struct CompanionCollectionEntry {
    pub id: i64,                                          // Snowflake ID, alias of rowid
    pub character_id: i64,                                // Snowflake ID, referances a `Character`
    pub companion_content_id: i64,                        // Snowflake ID, referances a `Companion`
    pub data: Option<Json<CompanionCollectionEntryData>>, // None when item can't have or currently does not have data, Some data prevents stacking
}

#[derive(Debug, FromRow)]
pub struct CollectionEntry {
    pub id: i64,           // Snowflake ID, alias of rowid
    pub character_id: i64, // Snowflake ID, referances a `Character`
    pub content_id: i64,   // Snowflake ID, referances an `Content`
}
/* End Game Data Service Schema */

/* Game Content Service Schema */
#[derive(Debug, FromRow)]
pub struct Asset {
    pub id: i64,                   // Snowflake ID, alias of rowid
    pub updated_at: NaiveDateTime, // Unix timestamp in seconds
    pub path: String, // Case insensitive indexed name, should be a valid unix path with no spaces, used in the virtual filesystem
    pub file_type: String, // Must be a valid MIME type, needed to understand `data`
    pub data: AssetData, // Binary blob or string representation of file saved to virtual filesystem
    pub size: i64,    // Size of data in bytes
    pub is_user_generated: bool,
    pub creator_user_handle: Option<i64>,
}

#[derive(Debug, FromRow)]
pub struct Content {
    pub id: i64,                   // Snowflake ID, alias of rowid
    pub updated_at: NaiveDateTime, // Unix timestamp in seconds
    pub name: String,              // Unique no case
    pub content_type: ContentType,
    pub content_subtype: ContentSubtype,
    pub data: Json<ContentData>, // None when item does not have extra data
    pub asset_id_0: Option<i64>, // Snowflake ID, referances an `Asset`
    pub asset_id_1: Option<i64>, // Snowflake ID, referances an `Asset`
    pub asset_id_2: Option<i64>, // Snowflake ID, referances an `Asset`
    pub asset_id_3: Option<i64>, // Snowflake ID, referances an `Asset`
    pub asset_id_4: Option<i64>, // Snowflake ID, referances an `Asset`
    pub is_user_generated: bool,
    pub base_content_id: Option<i64>,
    pub creator_user_handle: Option<i64>,
}
