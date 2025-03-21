use spacetimedb::SpacetimeType;

#[derive(SpacetimeType)]
pub enum Role {
    NewPlayer,
    Player,
    MembershipPlayer,
    GameModerator,
    GameAdministrator,
}
