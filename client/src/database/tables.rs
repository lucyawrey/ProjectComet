use serde::{Deserialize, Serialize};

#[derive(Debug, Serialize, Deserialize, Clone)]
pub struct GameInfo {
    pub created_at: i64,
    pub updated_at: i64,
    pub game_id: String,
    pub game_version: String,
    pub supported_client_game_ids: Vec<String>,
    pub game_display_name: String,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct Asset {
    pub id: i64,           // Snowflake ID, alias of rowid
    pub updated_at: i64,   // Unix timestamp in seconds
    pub path: String, // Case insensitive indexed name, should be a valid unix path with no spaces, used in the virtual filesystem
    pub file_type: String, // Must be a valid MIME type, needed to understand `data`
    pub data: AssetData, // Binary blob or string representation of file saved to virtual filesystem
    pub size: i64,    // Size of data in bytes
    pub is_user_generated: bool,
    pub creator_user_handle: Option<i64>, // Should not be exposed to client. Snowflake ID, referances an `User`
}

// TODO share content field tyoes with API
#[derive(Debug, Serialize, Deserialize)]
pub struct Content {
    pub id: i64,         // Snowflake ID, alias of rowid
    pub updated_at: i64, // Unix timestamp in seconds
    pub name: String,    // Unique no case
    pub content_type: u16,
    pub content_subtype: u16,
    pub data: ContentData,       // None when item does not have extra data
    pub asset_id_0: Option<i64>, // Snowflake ID, referances an `Asset`
    pub asset_id_1: Option<i64>, // Snowflake ID, referances an `Asset`
    pub asset_id_2: Option<i64>, // Snowflake ID, referances an `Asset`
    pub asset_id_3: Option<i64>, // Snowflake ID, referances an `Asset`
    pub asset_id_4: Option<i64>, // Snowflake ID, referances an `Asset`
    pub is_user_generated: bool,
    pub base_content_id: Option<i64>,
    pub creator_user_handle: Option<i64>, // Should not be exposed to client. Snowflake ID, referances an `User`
}

#[derive(Debug, Serialize, Deserialize)]
pub enum AssetData {
    None,
    Blob(Vec<u8>),
    String(String),
}

#[derive(Debug, Serialize, Deserialize)]
pub enum Ref {
    Id(i64),
    Name(String),
}

#[derive(Debug, Serialize, Deserialize)]
pub struct ContentData {
    pub stack_size: i64,
    pub is_unique: bool,
    pub is_soulbound: bool,
    pub tradability: u16,
}
