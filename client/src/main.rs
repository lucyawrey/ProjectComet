use bevy::{diagnostic::FrameTimeDiagnosticsPlugin, prelude::*};
use database::DatabasePlugin;
use debug::DebugPlugin;
use hello::HelloPlugin;
mod components;
mod config;
mod database;
mod debug;
mod hello;
mod platform;

fn main() {
    let mut app = App::new();
    app.insert_resource(GameState::default());
    app.add_plugins(DefaultPlugins.set(WindowPlugin {
        primary_window: Some(Window {
            canvas: Some("#project-comet-canvas".into()),
            ..default()
        }),
        ..default()
    }));
    app.add_plugins(FrameTimeDiagnosticsPlugin);
    app.add_plugins(DatabasePlugin);
    app.add_plugins(DebugPlugin);
    app.add_plugins(HelloPlugin);
    app.run();
}

#[derive(Resource, Default)]
pub struct GameState {}
