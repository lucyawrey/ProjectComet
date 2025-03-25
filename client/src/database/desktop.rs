use crate::config::DEFAULT_CLIENT_DATABASE_PATH;
use rusqlite::{Connection, OpenFlags};

pub struct ClientDatabase {
    conn: Connection,
}

impl ClientDatabase {
    pub fn new() -> Result<ClientDatabase, String> {
        let conn = connect_to_database()?;
        Ok(ClientDatabase { conn })
    }

    pub fn query_content_names(&self) -> String {
        let db = &self.conn;

        let mut query = db.prepare("SELECT name FROM content").unwrap();
        let content_names = query
            .query_map([], |row| {
                let name: String = row.get("name")?;
                Ok(name)
            })
            .unwrap();
        let mut out = String::new();
        for name in content_names {
            out = out + "\n" + &name.unwrap();
        }
        out
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
