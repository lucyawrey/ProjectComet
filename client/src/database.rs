use crate::debug::DebugState;
use bevy::prelude::*;

pub struct DatabasePlugin;

impl Plugin for DatabasePlugin {
    fn build(&self, app: &mut App) {
        // TODO download db and persist in file storage in browsers
        app.insert_resource(Database { loaded: true });
        app.add_systems(Startup, init_db);
    }
}

#[derive(Resource)]
pub struct Database {
    pub loaded: bool,
}

pub fn init_db(db: Res<Database>, mut debug: ResMut<DebugState>) {
    debug.print(format!("Dummy database loaded: {}", db.loaded), None);
}
