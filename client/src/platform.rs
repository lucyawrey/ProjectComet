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
    use rusqlite::ffi::{self, OpfsSAHPoolCfgBuilder};

    let config = OpfsSAHPoolCfgBuilder::new()
        .vfs_name("project_comet_vfs")
        .directory("")
        .build();
    if let Err(e) = ffi::install_opfs_sahpool(Some(&config), false).await {
        e.to_string()
    } else {
        "ok".to_string()
    }
}

#[derive(Debug)]
pub struct Person {
    pub _id: i32,
    pub _name: String,
    pub _data: Option<Vec<u8>>,
}

#[cfg(all(target_family = "wasm", target_os = "unknown"))]
#[wasm_bindgen::prelude::wasm_bindgen]
pub fn query() -> String {
    use web_sys::console;

    let db = Connection::open_with_flags_and_vfs(
        DEFAULT_CLIENT_DATABASE_PATH,
        rusqlite::OpenFlags::default(),
        "project_comet_vfs",
    )
    .unwrap();

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
    let mut out = String::new();
    for person in person_iter {
        out = out + &format!("{:?}\n", person.unwrap())
    }
    out
}

#[cfg(all(target_family = "wasm", target_os = "unknown"))]
pub struct Work {
    func: Box<dyn FnOnce() + Send>,
}

#[cfg(all(target_family = "wasm", target_os = "unknown"))]
pub fn run(
    worker: web_sys::Worker,
    f: impl FnOnce() + Send + 'static,
) -> Result<(), wasm_bindgen::JsValue> {
    let _worker = execute(worker, f)?;
    // reclaim_on_message(worker); set callback
    Ok(())
}

#[cfg(all(target_family = "wasm", target_os = "unknown"))]
fn execute(
    worker: web_sys::Worker,
    f: impl FnOnce() + Send + 'static,
) -> Result<web_sys::Worker, wasm_bindgen::JsValue> {
    use wasm_bindgen::JsValue;

    let work = Box::new(Work { func: Box::new(f) });
    let ptr = Box::into_raw(work);
    match worker.post_message(&JsValue::from(ptr as u32)) {
        Ok(()) => Ok(worker),
        Err(e) => {
            unsafe {
                drop(Box::from_raw(ptr));
            }
            Err(e)
        }
    }
}

#[cfg(all(target_family = "wasm", target_os = "unknown"))]
#[wasm_bindgen::prelude::wasm_bindgen]
pub fn child_entry_point(ptr: u32) -> Result<(), wasm_bindgen::JsValue> {
    use wasm_bindgen::{JsCast, JsValue};
    use web_sys::{js_sys, DedicatedWorkerGlobalScope};

    let ptr = unsafe { Box::from_raw(ptr as *mut Work) };
    let global = js_sys::global().unchecked_into::<DedicatedWorkerGlobalScope>();
    (ptr.func)();
    global.post_message(&JsValue::undefined())?;
    Ok(())
}
