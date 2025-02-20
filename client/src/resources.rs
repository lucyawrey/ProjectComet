use bevy::{ecs::system::Resource, time::Timer};

#[derive(Resource)]
pub struct GreetTimer(pub Timer);
