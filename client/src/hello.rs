use crate::api::Data;
use crate::chat::ChatState;
use crate::components::Name;
use crate::components::PlayerCharacter;
use bevy::prelude::*;

pub struct HelloPlugin;

impl Plugin for HelloPlugin {
    fn build(&self, app: &mut App) {
        app.insert_resource(GreetTimer(Timer::from_seconds(1.0, TimerMode::Once)));
        app.add_systems(Startup, spawn_people);
        app.add_systems(Update, greet_people_and_content);
    }
}

#[derive(Resource)]
pub struct GreetTimer(pub Timer);

pub fn spawn_people(mut commands: Commands) {
    commands.spawn((PlayerCharacter, Name("Stef".to_string())));
    commands.spawn((PlayerCharacter, Name("Laura".to_string())));
    commands.spawn((PlayerCharacter, Name("Lucy".to_string())));
}

pub fn greet_people_and_content(
    time: Res<Time>,
    data: Res<Data>,
    mut timer: ResMut<GreetTimer>,
    mut chat: ResMut<ChatState>,
    query: Query<&Name, With<PlayerCharacter>>,
) {
    if timer.0.tick(time.delta()).just_finished() {
        chat.print(&format!(
            "game_id: {}, gane_version: {}",
            data.game_info.clone().unwrap().game_id,
            data.game_info.clone().unwrap().game_version
        ));
        for name in &query {
            chat.print(&format!("hello {}!", name.0));
        }
    }
}
