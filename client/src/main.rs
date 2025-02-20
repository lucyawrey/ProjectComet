use bevy::prelude::*;
use plugins::HelloPlugin;
mod components;
mod plugins;
mod resources;
mod systems;

fn main() {
    dotenvy::dotenv().unwrap();

    App::new()
        .add_plugins(DefaultPlugins)
        .add_plugins(HelloPlugin)
        .run();
}
