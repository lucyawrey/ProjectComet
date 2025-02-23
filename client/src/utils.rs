use crate::config::DEFAULT_CLIENT_DATABASE_PATH;
use rusqlite::Connection;

pub fn get_database() -> Result<Connection, ()> {
    Connection::open(DEFAULT_CLIENT_DATABASE_PATH).map_err(|_e| ())
}
