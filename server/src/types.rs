use spacetimedb::SpacetimeType;

#[derive(SpacetimeType)]
pub enum Role {
    NewPlayer,
    Player,
    MembershipPlayer,
    GameModerator,
    GameAdministrator,
}

#[derive(SpacetimeType)]
pub enum CharacterAncestry {
    Cat = 0,
    Human = 1,
}

#[derive(SpacetimeType)]
pub enum CharacterGender {
    Neutral = 0,   // they/them
    Feminine = 1,  // she/her
    Masculine = 2, // he/him
    None = 3,      // it/it's
    Fluid = 4, // based on current presentation--active glamour and customization from either your base character or current class.
    Advanced = 5, // custom pronouns
}

#[derive(Default, SpacetimeType)]
pub struct Customization {
    pub gender_details: GenderDetails,
}

#[derive(Default, SpacetimeType)]
pub struct GenderDetails {}

#[derive(Default, SpacetimeType)]
pub struct CharacterData {
    pub character_history: CharacterHistory,
    pub npc_relationships: NpcRelationships,
}

#[derive(Default, SpacetimeType)]
pub struct CharacterHistory {}

#[derive(Default, SpacetimeType)]
pub struct NpcRelationships {}
