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
    use web_sys::{console, js_sys, MessageEvent, Worker};

    let worker = Worker::new("./worker.js").unwrap();
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

    // With a worker spun up send it the module/memory so it can start instantiating the Wasm module. Later it might receive further messages about code to run on the Wasm module.
    let array = js_sys::Array::new();
    array.push(&wasm_bindgen::module());
    array.push(&wasm_bindgen::memory());
    let _ = worker.post_message(&array);

    let _ = worker.post_message(&"query".into());
    let _ = run(worker, move || {
        console::log_1(&"WASM - Inside callback.".into());
    });

    app();
}
