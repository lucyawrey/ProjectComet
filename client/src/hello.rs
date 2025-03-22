use crate::chat::ChatState;
use crate::components::Name;
use crate::components::PlayerCharacter;
use crate::database::Db;
use crate::database_bindings::CharacterTableAccess;
use bevy::prelude::*;
use spacetimedb_sdk::DbContext;
use spacetimedb_sdk::Table;

pub struct HelloPlugin;

impl Plugin for HelloPlugin {
    fn build(&self, app: &mut App) {
        app.insert_resource(GreetTimer(Timer::from_seconds(2.0, TimerMode::Once)));
        app.add_systems(Update, (add_people, greet_people).chain());
    }
}

#[derive(Resource)]
pub struct GreetTimer(pub Timer);
pub fn add_people(
    time: Res<Time>,
    mut timer: ResMut<GreetTimer>,
    mut commands: Commands,
    db: Res<Db>,
) {
    if timer.0.tick(time.delta()).just_finished() {
        for character in db.conn.db.character().iter() {
            commands.spawn((PlayerCharacter, Name(character.name)));
        }
    }
}

pub fn greet_people(
    time: Res<Time>,
    mut timer: ResMut<GreetTimer>,
    mut chat: ResMut<ChatState>,
    query: Query<&Name, With<PlayerCharacter>>,
) {
    if timer.0.tick(time.delta()).just_finished() {
        for name in &query {
            chat.print(&format!("hello: {}", name.0));
        }
    }
}
