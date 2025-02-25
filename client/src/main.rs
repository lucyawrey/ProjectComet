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
    use platform::run;
    use wasm_bindgen::{prelude::Closure, JsCast};
    use web_sys::{console, MessageEvent, Worker, WorkerOptions, WorkerType};

    let options = WorkerOptions::new();
    options.set_type(WorkerType::Module);
    let worker = Worker::new_with_options("./worker.js", &options).unwrap();
    console::log_1(&"WASM - Creating worker.".into());

    let callback: Closure<dyn FnMut(MessageEvent)> = Closure::new(move |event: MessageEvent| {
        let data = event.data();
        if let Some(text) = data.as_string() {
            if text.as_str() == "loading" {
                console::log_1(&"WASM - Worker loading...".into());
                return;
            }
        }
        console::log_1(&"WASM".into());
        console::log_1(&data);
    });
    worker.set_onmessage(Some(callback.as_ref().unchecked_ref()));

    let _ = worker.post_message(&wasm_bindgen::module());

    let _ = worker.post_message(&"query".into());
    let _ = run(worker, || {
        console::log_1(&"WASM - Worker closure test.".into());
    });

    app();
}
