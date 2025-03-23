use crate::chat::ChatState;
use crate::components::Name;
use crate::components::PlayerCharacter;
use bevy::prelude::*;

pub struct HelloPlugin;

impl Plugin for HelloPlugin {
    fn build(&self, app: &mut App) {
        app.insert_resource(GreetTimer(Timer::from_seconds(1.0, TimerMode::Once)));
        app.add_systems(Update, (update_people, greet_people).chain());
    }
}

#[derive(Resource)]
pub struct GreetTimer(pub Timer);

pub fn greet_people(
    time: Res<Time>,
    mut timer: ResMut<GreetTimer>,
    mut chat: ResMut<ChatState>,
    mut commands: Commands,
    query: Query<&Name, With<PlayerCharacter>>,
) {
    if timer.0.tick(time.delta()).just_finished() {
        commands.spawn((PlayerCharacter, Name("Stef".to_string())));
        commands.spawn((PlayerCharacter, Name("Laura".to_string())));
        commands.spawn((PlayerCharacter, Name("Lucy".to_string())));
        for name in &query {
            chat.print(&format!("hello {}!", name.0));
        }
    }
}

pub fn update_people(mut query: Query<&mut Name, With<PlayerCharacter>>) {
    for mut name in &mut query {
        if name.0 == "Stef" {
            name.0 = "Stefanie".to_string();
            break;
        }
    }
}
