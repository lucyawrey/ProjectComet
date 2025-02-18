use bevy::prelude::*;
use plugins::HelloPlugin;
mod components;
mod plugins;
mod systems;

fn main() {
    App::new()
        .add_plugins(DefaultPlugins)
        .add_plugins(HelloPlugin)
        .run();
}
