use crate::types::{CharacterAncestry, CharacterData, CharacterGender, Customization, Role};
use spacetimedb::{table, Identity, Timestamp};

#[table(name = user)]
pub struct User {
    #[primary_key]
    pub id: Identity,
    #[unique]
    pub handle: u64,
    #[unique]
    pub name: String,
    pub role: Role,
    pub online: bool,
}

#[table(name = character)]
pub struct Character {
    #[primary_key]
    pub id: u64,
    #[unique]
    pub handle: u64,
    #[unique]
    pub name: String,
    pub role: Role,
    pub home_world_id: String,
    pub user_id: Identity,
    pub ancestry: CharacterAncestry,
    pub gender: CharacterGender,
    pub customization: Customization,
    pub data: CharacterData,
    pub online: bool,
}

#[table(name = message, public)]
pub struct Message {
    pub sender_user_id: Identity,
    pub sent: Timestamp,
    pub text: String,
}
