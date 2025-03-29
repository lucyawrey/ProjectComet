use num_enum::{IntoPrimitive, TryFromPrimitive};
use serde::{Deserialize, Serialize};
use sqlx::prelude::Type;

#[derive(Debug, Eq, PartialEq, PartialOrd, IntoPrimitive, TryFromPrimitive, Type)]
#[repr(u16)]
pub enum Role {
    NewPlayer = 0,
    Player = 1,
    MembershipPlayer = 2,
    GameModerator = 3,
    GameAdministrator = 4,
}

#[derive(Debug, Eq, PartialEq, PartialOrd, IntoPrimitive, TryFromPrimitive, Type)]
#[repr(u16)]
pub enum AccessLevel {
    Default = 0,
    GameServer = 1,
    Administrator = 2,
}

#[derive(Debug, Eq, PartialEq, PartialOrd, IntoPrimitive, TryFromPrimitive, Type)]
#[repr(u16)]
pub enum CharacterAncestry {
    Cat = 0,
    Human = 1,
}

#[derive(Debug, Eq, PartialEq, PartialOrd, IntoPrimitive, TryFromPrimitive, Type)]
#[repr(u16)]
pub enum CharacterGender {
    Neutral = 0,   // they/them
    Feminine = 1,  // she/her
    Masculine = 2, // he/him
    None = 3,      // it/it's
    Fluid = 4, // based on current presentation--active glamour and customization from either your base character or current class.
    Advanced = 5, // custom pronouns
}

#[derive(Debug, Serialize, Deserialize)]
pub struct Customization {
    pub gender_details: GenderDetails,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct GenderDetails {}

#[derive(Debug, Serialize, Deserialize)]
pub struct CharacterData {
    pub character_history: CharacterHistory,
    pub npc_relationships: NpcRelationships,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct CharacterHistory {}

#[derive(Debug, Serialize, Deserialize)]
pub struct NpcRelationships {}

#[derive(Debug, Eq, PartialEq, PartialOrd, IntoPrimitive, TryFromPrimitive, Type)]
#[repr(u16)]
pub enum GameOptionsType {
    User = 0,
    Character = 1,
    System = 2,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct GameOptionsData {}

#[derive(Debug, Serialize, Deserialize)]
pub struct CharacterStatusData {}

#[derive(Debug, Serialize, Deserialize)]
pub struct Statistics {}

#[derive(Debug, Serialize, Deserialize)]
pub struct ClassData {}

#[derive(Debug, Serialize, Deserialize)]
pub struct OutfitData {}

#[derive(Debug, Eq, PartialEq, PartialOrd, IntoPrimitive, TryFromPrimitive, Type)]
#[repr(u16)]
pub enum GuildRole {
    Member = 0,
    Trustee = 1,
}

#[derive(Debug, Eq, PartialEq, PartialOrd, IntoPrimitive, TryFromPrimitive, Type)]
#[repr(u16)]
pub enum ItemLocation {
    Other = 0,
    Dropped = 1,
    NpcMerchant = 2,
    Market = 3,
    Inventory = 4,
    Equipped = 5,
    InventoryContainer = 6,
    ClassItem = 7,
    Box = 8,
}

#[derive(Debug, Eq, PartialEq, PartialOrd, IntoPrimitive, TryFromPrimitive, Type)]
#[repr(u16)]
pub enum ItemQuality {
    Normal = 0,
    Silver = 1,
    Gold = 2,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct ItemInstanceData {}

#[derive(Debug, Eq, PartialEq, PartialOrd, IntoPrimitive, TryFromPrimitive, Type)]
#[repr(u16)]
pub enum ItemCollectionEntryLocation {
    NotTracked = 0,
    Soulbound = 1,
    OnCharacter = 2,
    ClassItem = 3,
    Box = 4,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct CompanionCollectionEntryData {}

#[derive(Debug, PartialEq, Clone)]
pub enum AssetData {
    Blob(Vec<u8>),
    String(String),
}

#[derive(Debug, Eq, PartialEq, PartialOrd, IntoPrimitive, TryFromPrimitive, Type)]
#[repr(u16)]
pub enum ContentType {
    None = 0,

    Class = 1,
    GameFeature = 2,

    Item = 100,
    UserGeneratedItem = 101,
    Companion = 200,
    CharacterOption = 300,
    UserGeneratedOption = 301,
}

#[derive(Debug, Eq, PartialEq, PartialOrd, IntoPrimitive, TryFromPrimitive, Type)]
#[repr(u16)]
pub enum ContentSubtype {
    None = 0,

    Currency = 100,
    Material = 101,
    Consumable = 102,
    QuestItem = 103,
    UnlockItem = 104,
    Equipment = 105,
    InventoryContainer = 106,
    ClassItem = 107,

    Mount = 200,
    Pet = 201,

    Color = 300,
    BodyType = 301,
    Hairstyle = 302,
    Makeup = 303,
    Underclothes = 304,
}

// TODO implement enum support with serde deserialization and toml importing
#[derive(Debug, Serialize, Deserialize)]
pub struct ContentData {
    pub stack_size: i64,
    pub is_unique: bool,
    pub is_soulbound: bool,
    // TODO fix enum serde serialization
    // pub tradability: ItemTradability,
    pub tradability: u16,
}

// pub enum ContentData {
//     Class {},
//     Item {
//         stack_size: i64,
//         is_unique: bool,
//         is_soulbound: bool,
//         tradability: ItemTradability,
//     },
//     Companion {},
//     Unlock {},
// }

#[derive(
    Debug, Eq, PartialEq, PartialOrd, IntoPrimitive, TryFromPrimitive, Serialize, Deserialize,
)]
#[repr(u16)]
pub enum ItemTradability {
    Untradeable = 0,
    Droppable = 1,
    NpcTradable = 2,
    PlayerTradable = 3,
    PlayerMarketable = 4,
}
