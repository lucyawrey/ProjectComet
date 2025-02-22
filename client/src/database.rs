use crate::{config::DEFAULT_CLIENT_DATABASE_PATH, debug::DebugState};
use bevy::prelude::*;
use rusqlite::Connection;
use std::{fmt::Debug, sync::Mutex};

pub struct DatabasePlugin;

impl Plugin for DatabasePlugin {
    fn build(&self, app: &mut App) {
        // TODO download db and persist in file storage in browsers
        let conn = Mutex::new(
            Connection::open(DEFAULT_CLIENT_DATABASE_PATH).expect("Failed to open database."),
        );
        app.insert_resource(Database(conn))
            .add_systems(Startup, init_db);
    }
}

#[derive(Resource)]
pub struct Database(pub Mutex<Connection>);

#[derive(Debug)]
pub struct Person {
    pub _id: i32,
    pub _name: String,
    pub _data: Option<Vec<u8>>,
}

pub fn init_db(db: Res<Database>, mut debug: ResMut<DebugState>) {
    let db = db.0.lock().unwrap();
    let _ignore_err = db.execute(
        "CREATE TABLE person (
            id   INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            data BLOB
        )",
        (), // empty list of parameters.
    );
    db.execute("INSERT INTO person (name) VALUES (?1)", ("Lucy",))
        .unwrap();

    let mut query = db.prepare("SELECT id, name, data FROM person").unwrap();
    let person_iter = query
        .query_map([], |row| {
            Ok(Person {
                _id: row.get(0)?,
                _name: row.get(1)?,
                _data: row.get(2)?,
            })
        })
        .unwrap();
    for person in person_iter {
        debug.print(person.unwrap(), Some(Color::linear_rgb(1.0, 0.2, 0.2)));
    }
}
