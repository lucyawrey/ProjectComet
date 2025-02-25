use crate::{debug::DebugState, platform::get_database};
use bevy::prelude::*;
use rusqlite::Connection;
use std::{fmt::Debug, sync::Mutex};

pub struct DatabasePlugin;

impl Plugin for DatabasePlugin {
    fn build(&self, app: &mut App) {
        // TODO download db and persist in file storage in browsers
        let conn = Mutex::new(get_database().expect("Failed to open database."));
        app.insert_resource(Database(conn));
        app.add_systems(Startup, init_db);
    }
}

#[derive(Resource)]
pub struct Database(pub Mutex<Connection>);

pub fn init_db(db: Res<Database>, mut debug: ResMut<DebugState>) {
    let db = db.0.lock().unwrap();
    let _err = db.execute(
        "CREATE TABLE person (
            id   INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            data BLOB
        )",
        (), // empty list of parameters.
    );
    debug.print("Database Ready.", Some(Color::linear_rgb(1.0, 0.2, 0.2)));
}
