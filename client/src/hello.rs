use crate::chat::ChatState;
use crate::components::Name;
use crate::components::PlayerCharacter;
use crate::database::Database;
use crate::database_bindings::CharacterTableAccess;
use bevy::prelude::*;
use spacetimedb_sdk::Table;

pub struct HelloPlugin;

impl Plugin for HelloPlugin {
    fn build(&self, app: &mut App) {
        app.insert_resource(GreetTimer(Timer::from_seconds(2.0, TimerMode::Once)));
        app.add_systems(Startup, add_people);
        app.add_systems(Update, greet_people);
    }
}

#[derive(Resource)]
pub struct GreetTimer(pub Timer);

pub fn add_people(mut commands: Commands, db: Res<Database>) {
    for character in db.0.db.character().iter() {
        commands.spawn((PlayerCharacter, Name(character.name)));
    }
}

pub fn greet_people(
    time: Res<Time>,
    mut chat: ResMut<ChatState>,
    mut timer: ResMut<GreetTimer>,
    query: Query<&Name, With<PlayerCharacter>>,
) {
    if timer.0.tick(time.delta()).just_finished() {
        for name in &query {
            chat.print(&format!("hello: {}", name.0));
        }
    }
}
