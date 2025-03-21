use crate::types::Role;
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

#[table(name = message, public)]
pub struct Message {
    pub sender: Identity,
    pub sent: Timestamp,
    pub text: String,
}
