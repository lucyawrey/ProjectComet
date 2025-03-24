use crate::config::{
    CLIENT_GAME_ID, CLIENT_VERSION, DEFAULT_CLIENT_DATABASE_PATH, DEFAULT_WASM_VFS_NAME,
};
use rusqlite::{
    ffi::{self, OpfsSAHPoolCfgBuilder},
    Connection, OpenFlags,
};
use wasm_bindgen::{prelude::Closure, JsCast, JsValue};
use web_sys::{console, js_sys, DedicatedWorkerGlobalScope, MessageEvent, Worker};

pub struct CrossPlatformDatabase {
    //worker: Mutex<Worker>,
}

impl CrossPlatformDatabase {
    pub fn new() -> Result<CrossPlatformDatabase, ()> {
        let callback = get_callback_closure();
        let worker = spawn_worker(callback).map_err(|_e| ())?;
        Ok(CrossPlatformDatabase {
            //worker: Mutex::new(worker),
        })
    }

    pub fn query_content_names(&self) -> String {
        "TODO: Client Database on the Web".to_string()
    }
}

pub fn spawn_worker(
    callback: Closure<dyn FnMut(web_sys::MessageEvent)>,
) -> Result<web_sys::Worker, wasm_bindgen::JsValue> {
    let worker = Worker::new("./worker.js")?;
    console::log_1(&"WASM - Creating worker.".into());

    worker.set_onmessage(Some(callback.as_ref().unchecked_ref()));

    // With a worker spun up send it the module/memory so it can start instantiating the Wasm module. Later it might receive further messages about code to run on the Wasm module.
    let array = js_sys::Array::new();
    array.push(&wasm_bindgen::module());
    array.push(&wasm_bindgen::memory());
    worker.post_message(&array)?;

    Ok(worker)
}

pub fn get_callback_closure() -> Closure<dyn FnMut(web_sys::MessageEvent)> {
    Closure::new(move |event: MessageEvent| {
        let data = event.data();
        if let Some(text) = data.as_string() {
            if text.as_str() == "loading" {
                console::log_1(&"WASM - Worker loading...".into());
                return;
            }
        }
        console::log_2(&"WASM -".into(), &data);
    })
}

pub fn game_info_query(db: &Connection) -> Result<(String, String), String> {
    let mut query = db
        .prepare("SELECT game_id, game_version FROM game_info WHERE id = 0")
        .map_err(|e| e.to_string())?;
    Ok(query
        .query_row((), |row| {
            let game_id: String = row.get("game_id")?;
            let game_version: String = row.get("game_version")?;
            Ok((game_id, game_version))
        })
        .map_err(|e| e.to_string())?)
}

pub fn query() {
    let db = Connection::open_with_flags_and_vfs(
        DEFAULT_CLIENT_DATABASE_PATH,
        OpenFlags::SQLITE_OPEN_READ_WRITE
            | OpenFlags::SQLITE_OPEN_URI
            | OpenFlags::SQLITE_OPEN_NO_MUTEX,
        DEFAULT_WASM_VFS_NAME,
    )
    .unwrap();

    let name = get_worker_scope().crypto().unwrap().random_uuid();

    db.execute("INSERT INTO content (name) VALUES ($1)", [name])
        .unwrap();

    let mut query = db.prepare("SELECT name FROM content").unwrap();
    let content_names = query
        .query_map([], |row| {
            let name: String = row.get("name")?;
            Ok(name)
        })
        .unwrap();
    let mut out = String::new();
    for name in content_names {
        out = out + &name.unwrap() + "\n";
    }
    console::log_1(&format!("WASM - Database content table names\n{}", out).into());
}

pub fn get_worker_scope() -> DedicatedWorkerGlobalScope {
    js_sys::global().unchecked_into::<DedicatedWorkerGlobalScope>()
}

#[wasm_bindgen::prelude::wasm_bindgen]
pub async fn install_opfs_sahpool(initial_db_file: Vec<u8>) -> Result<(), JsValue> {
    let opfs_options = OpfsSAHPoolCfgBuilder::new()
        .vfs_name(DEFAULT_WASM_VFS_NAME)
        .build();
    let opfs_util = ffi::install_opfs_sahpool(Some(&opfs_options), false)
        .await
        .map_err(|e| e.to_string())?;

    if let Ok(db) = Connection::open_with_flags_and_vfs(
        DEFAULT_CLIENT_DATABASE_PATH,
        OpenFlags::SQLITE_OPEN_READ_WRITE
            | OpenFlags::SQLITE_OPEN_URI
            | OpenFlags::SQLITE_OPEN_NO_MUTEX,
        DEFAULT_WASM_VFS_NAME,
    ) {
        if let Ok((game_id, game_version)) = game_info_query(&db) {
            if game_id == CLIENT_GAME_ID && game_version.contains(CLIENT_VERSION) {
                console::log_1(&"WASM - Loaded existng database from the VFS.".into());
                return Ok(());
            }
        }
    }

    opfs_util
        .import_db(DEFAULT_CLIENT_DATABASE_PATH, &initial_db_file)
        .unwrap();
    console::log_1(&"WASM - Imported new database into the VFS.".into());

    Ok(())
}
