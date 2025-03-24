mod chat;
mod components;
mod config;
mod database;
mod fps;
mod hello;
use bevy::{diagnostic::FrameTimeDiagnosticsPlugin, prelude::*};
use chat::ChatPlugin;
use database::{get_callback_closure, spawn_worker, DatabasePlugin};
use fps::FpsPlugin;
use hello::HelloPlugin;

pub fn app() {
    let mut app = App::new();
    app.add_plugins(DefaultPlugins.set(WindowPlugin {
        primary_window: Some(Window {
            canvas: Some("#project-comet-canvas".into()),
            ..default()
        }),
        ..default()
    }));
    app.add_plugins(FrameTimeDiagnosticsPlugin);
    app.add_systems(Startup, setup_camera);
    app.add_plugins(FpsPlugin);
    app.add_plugins(DatabasePlugin);
    app.add_plugins(ChatPlugin);
    app.add_plugins(HelloPlugin);
    app.run();
}

fn setup_camera(mut commands: Commands) {
    commands.spawn(Camera2d);
}

#[cfg(any(target_family = "unix", target_family = "windows"))]
fn main() {
    app();
}

#[cfg(all(target_family = "wasm", target_os = "unknown"))]
fn main() {}

#[cfg(all(target_family = "wasm", target_os = "unknown"))]
#[wasm_bindgen::prelude::wasm_bindgen]
pub fn init_app() {
    web_sys::console::log_1(&"WASM - Initializing Bevy Game".into());
    let callback = get_callback_closure();
    let worker = spawn_worker(&callback)
        .map_err(|_e| ())
        .expect("Failed to spawn web worker used for client database.");

    app();
}
