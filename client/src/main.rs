use bevy::{diagnostic::FrameTimeDiagnosticsPlugin, prelude::*};
use database::DatabasePlugin;
use debug::DebugPlugin;
use hello::HelloPlugin;
mod components;
mod config;
mod database;
mod debug;
mod hello;
mod utils;

fn main() {
    App::new()
        .insert_resource(GameState::default())
        .add_plugins((DefaultPlugins, FrameTimeDiagnosticsPlugin))
        .add_plugins(DatabasePlugin)
        .add_plugins(DebugPlugin)
        .add_plugins(HelloPlugin)
        .run();
}

#[derive(Resource, Default)]
pub struct GameState {}
