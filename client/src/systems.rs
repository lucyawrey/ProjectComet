use crate::components::Name;
use crate::components::PlayerCharacter;
use crate::resources::GreetTimer;
use bevy::prelude::*;

pub fn add_people(mut commands: Commands) {
    commands.spawn((PlayerCharacter, Name("Stefanie".to_string())));
    commands.spawn((PlayerCharacter, Name("Laura".to_string())));
    commands.spawn((PlayerCharacter, Name("Lucy".to_string())));
}

pub fn greet_people(
    time: Res<Time>,
    mut timer: ResMut<GreetTimer>,
    query: Query<&Name, With<PlayerCharacter>>,
) {
    if timer.0.tick(time.delta()).just_finished() {
        for name in &query {
            println!("hello {}!", name.0);
        }
    }
}

pub fn update_people(mut query: Query<&mut Name, With<PlayerCharacter>>) {
    for mut name in &mut query {
        if name.0 == "Stefanie" {
            name.0 = "Stefieany".to_string();
            break;
        }
    }
}
