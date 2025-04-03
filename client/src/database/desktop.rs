use super::{query_contents, query_game_info, Content, Data, DatabasePlugin, GameInfo};
use crate::config::DEFAULT_CLIENT_DATABASE_PATH;
use bevy::prelude::*;
use rusqlite::{Connection, OpenFlags};
use std::sync::Mutex;

/* Plugin */
impl Plugin for DatabasePlugin {
    fn build(&self, app: &mut App) {
        let database = ClientDatabase::new().expect("Failed to initialize client database.");
        app.insert_resource(database);
        app.insert_resource(Data::default());
        app.add_systems(Startup, fetch_data);
    }
}

pub fn fetch_data(db: Res<ClientDatabase>, mut data: ResMut<Data>) {
    let game_info = db.query_game_info();
    data.game_info = Some(game_info);
    let content = db.query_content();
    for item in content {
        data.content.insert(item.id, item);
    }
}
/* End Plugin */

#[derive(Resource)]
pub struct ClientDatabase {
    conn: Mutex<Connection>,
}

impl ClientDatabase {
    pub fn new() -> Result<ClientDatabase, String> {
        let conn = connect_to_database()?;
        Ok(ClientDatabase {
            conn: Mutex::new(conn),
        })
    }

    pub fn query_game_info(&self) -> GameInfo {
        query_game_info(&self.conn.lock().unwrap()).unwrap()
    }

    pub fn query_content(&self) -> Vec<Content> {
        query_contents(&self.conn.lock().unwrap()).unwrap()
    }
}

pub fn connect_to_database() -> Result<Connection, String> {
    Connection::open_with_flags(
        DEFAULT_CLIENT_DATABASE_PATH,
        OpenFlags::SQLITE_OPEN_READ_WRITE
            | OpenFlags::SQLITE_OPEN_URI
            | OpenFlags::SQLITE_OPEN_NO_MUTEX,
    )
    .map_err(|e| e.to_string())
}
