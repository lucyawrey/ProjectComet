use bevy::prelude::*;

#[derive(Component)]
#[require(Name, Transform)]
pub struct PlayerCharacter;

#[derive(Component, Default)]
pub struct Name(pub String);
