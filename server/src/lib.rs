mod utils;
use spacetimedb::{reducer, table, Identity, ReducerContext, SpacetimeType, Table, Timestamp};
use utils::{get_random_id, get_random_name, validate_message, validate_username};

#[derive(SpacetimeType)]
pub enum Role {
    NewPlayer,
    Player,
    MembershipPlayer,
    GameModerator,
    GameAdministrator,
}

#[table(name = user)]
pub struct User {
    #[primary_key]
    id: Identity,
    #[unique]
    handle: u64,
    #[unique]
    name: String,
    role: Role,
    online: bool,
}

#[table(name = message, public)]
pub struct Message {
    sender: Identity,
    sent: Timestamp,
    text: String,
}

#[reducer(init)]
pub fn init(_ctx: &ReducerContext) {
    // Called when the module is initially published
}

#[reducer(client_connected)]
pub fn client_connected(ctx: &ReducerContext) {
    if let Some(user) = ctx.db.user().id().find(ctx.sender) {
        // If this is a returning user, i.e. we already have a `User` with this `Identity`,
        // set `online: true`, but leave `name` and `identity` unchanged.
        ctx.db.user().id().update(User {
            online: true,
            ..user
        });
    } else {
        // If this is a new user, create a `User` row for the `Identity`,
        // which is online, but hasn't set a name.
        ctx.db.user().insert(User {
            id: ctx.sender,
            handle: get_random_id(&ctx),
            name: get_random_name(&ctx),
            role: Role::NewPlayer,
            online: true,
        });
    }
}

#[reducer(client_disconnected)]
pub fn client_disconnected(ctx: &ReducerContext) {
    if let Some(user) = ctx.db.user().id().find(ctx.sender) {
        ctx.db.user().id().update(User {
            online: false,
            ..user
        });
    } else {
        // This branch should be unreachable,
        // as it doesn't make sense for a client to disconnect without connecting first.
        log::warn!(
            "Disconnect event for unknown user with identity {:?}",
            ctx.sender
        );
    }
}

#[reducer]
/// Clients invoke this reducer to set their user names.
pub fn set_username(ctx: &ReducerContext, username: String) -> Result<(), String> {
    validate_username(&username)?;
    if let Some(user) = ctx.db.user().id().find(ctx.sender) {
        ctx.db.user().id().update(User {
            name: username,
            ..user
        });
        Ok(())
    } else {
        Err("Cannot set name for unknown user".to_string())
    }
}

#[reducer]
/// Clients invoke this reducer to send messages.
pub fn send_message(ctx: &ReducerContext, text: String) -> Result<(), String> {
    validate_message(&text)?;
    log::info!("{}", text);
    ctx.db.message().insert(Message {
        sender: ctx.sender,
        text,
        sent: ctx.timestamp,
    });
    Ok(())
}
