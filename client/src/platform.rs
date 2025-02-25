use crate::config::DEFAULT_CLIENT_DATABASE_PATH;
use rusqlite::Connection;

#[cfg(any(target_family = "unix", target_family = "windows"))]
pub fn get_database() -> Result<Connection, ()> {
    Connection::open(DEFAULT_CLIENT_DATABASE_PATH).map_err(|_e| ())
}

#[cfg(all(target_family = "wasm", target_os = "unknown"))]
pub fn get_database() -> Result<Connection, ()> {
    Connection::open(DEFAULT_CLIENT_DATABASE_PATH).map_err(|_e| ())
}

#[cfg(all(target_family = "wasm", target_os = "unknown"))]
#[wasm_bindgen::prelude::wasm_bindgen]
pub async fn install_opfs_sahpool() -> String {
    use rusqlite::{
        ffi::{self, OpfsSAHPoolCfgBuilder},
        OpenFlags,
    };

    let config = OpfsSAHPoolCfgBuilder::new()
        .vfs_name("project_comet_vfs")
        .directory("")
        .build();
    if let Err(e) = ffi::install_opfs_sahpool(Some(&config), false).await {
        return e.to_string();
    }
    let db = match Connection::open_with_flags_and_vfs(
        DEFAULT_CLIENT_DATABASE_PATH,
        OpenFlags::default(),
        "project_comet_vfs",
    ) {
        Ok(db) => db,
        Err(e) => return e.to_string(),
    };

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
            Ok(crate::database::Person {
                _id: row.get(0)?,
                _name: row.get(1)?,
                _data: row.get(2)?,
            })
        })
        .unwrap();
    let mut out = String::new();
    for person in person_iter {
        out = out + &format!("{:?}\n", person.unwrap())
    }
    out
}
