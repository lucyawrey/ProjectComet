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
    app.add_plugins(DatabasePlugin);
    app.add_plugins(DebugPlugin);
    app.add_plugins(HelloPlugin);
    app.run();
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
    use platform::{get_callback_closure, query, run_in_worker, spawn_worker};

    // Callback ownership needs to stay here to keep it in scope for future messages.
    let callback = get_callback_closure();
    let worker = spawn_worker(&callback).expect("Failed to initialize web worker.");
    let _ = run_in_worker(&worker, query);

    app();
}
