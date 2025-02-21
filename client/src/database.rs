use crate::debug::DebugState;
use bevy::prelude::*;
use rusqlite::Connection;
use std::sync::Mutex;

pub struct DatabasePlugin;

impl Plugin for DatabasePlugin {
    fn build(&self, app: &mut App) {
        let conn = Mutex::new(Connection::open_in_memory().expect("Failed to open database."));
        app.insert_resource(Database(conn))
            .add_systems(Startup, init_db);
    }
}

#[derive(Resource)]
pub struct Database(pub Mutex<Connection>);

#[derive(Debug)]
pub struct Person {
    pub _id: i32,
    pub name: String,
    pub data: Option<Vec<u8>>,
}

pub fn init_db(db: Res<Database>, mut state: ResMut<DebugState>) {
    let db = db.0.lock().unwrap();
    db.execute(
        "CREATE TABLE person (
            id   INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            data BLOB
        )",
        (), // empty list of parameters.
    )
    .unwrap();
    let person1 = Person {
        _id: 0,
        name: "Lucy".to_string(),
        data: None,
    };
    db.execute(
        "INSERT INTO person (name, data) VALUES (?1, ?2)",
        (&person1.name, &person1.data),
    )
    .unwrap();
    let person2 = Person {
        _id: 0,
        name: "Shannon".to_string(),
        data: None,
    };
    db.execute(
        "INSERT INTO person (name, data) VALUES (?1, ?2)",
        (&person2.name, &person2.data),
    )
    .unwrap();

    let mut statement = db.prepare("SELECT id, name, data FROM person").unwrap();
    let person_iter = statement
        .query_map([], |row| {
            Ok(Person {
                _id: row.get(0)?,
                name: row.get(1)?,
                data: row.get(2)?,
            })
        })
        .unwrap();
    for person in person_iter {
        state.debug_text = format!("{}{:?}\n", state.debug_text, person.unwrap());
    }
    state.debug_color = Color::linear_rgb(1.0, 0.2, 0.2)
}
