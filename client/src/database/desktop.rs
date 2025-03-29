use crate::config::DEFAULT_CLIENT_DATABASE_PATH;
use rusqlite::{Connection, OpenFlags};

use super::query_all_content;

pub struct ClientDatabase {
    conn: Connection,
}

impl ClientDatabase {
    pub fn new() -> Result<ClientDatabase, String> {
        let conn = connect_to_database()?;
        Ok(ClientDatabase { conn })
    }

    pub fn query_content(&self) -> String {
        let content = query_all_content(&self.conn).unwrap();
        let mut out = String::new();
        for item in content {
            out = out + &item.name + "\n";
        }
        out.trim().to_string()
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
