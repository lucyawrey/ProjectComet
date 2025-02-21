use bevy::{diagnostic::FrameTimeDiagnosticsPlugin, prelude::*};
use database::DatabasePlugin;
use fps::FpsPlugin;
use hello::HelloPlugin;
mod components;
mod database;
mod fps;
mod hello;

fn main() {
    App::new()
        .add_plugins((DefaultPlugins, FrameTimeDiagnosticsPlugin))
        .add_plugins(DatabasePlugin)
        .add_plugins(FpsPlugin)
        .add_plugins(HelloPlugin)
        .run();
}
