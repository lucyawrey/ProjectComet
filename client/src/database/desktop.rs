use crate::config::DEFAULT_CLIENT_DATABASE_PATH;
use rusqlite::{Connection, OpenFlags};
use std::sync::Mutex;

pub struct CrossPlatformDatabase {
    conn: Mutex<Connection>,
}

impl CrossPlatformDatabase {
    pub fn new() -> Result<CrossPlatformDatabase, String> {
        let conn = connect_to_database().map_err(|e| e.to_string())?;
        Ok(CrossPlatformDatabase {
            conn: Mutex::new(conn),
        })
    }

    pub fn query_content_names(&self) -> String {
        let db = self.conn.lock().unwrap();
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

pub fn connect_to_database() -> Connection {
    Connection::open_with_flags(
        DEFAULT_CLIENT_DATABASE_PATH,
        OpenFlags::SQLITE_OPEN_READ_WRITE
            | OpenFlags::SQLITE_OPEN_URI
            | OpenFlags::SQLITE_OPEN_NO_MUTEX,
    )
}
