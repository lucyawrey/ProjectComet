use crate::config::{
    CLIENT_GAME_ID, CLIENT_VERSION, DEFAULT_CLIENT_DATABASE_PATH, DEFAULT_WASM_VFS_NAME,
};
use rusqlite::{
    ffi::{self, OpfsSAHPoolCfgBuilder},
    Connection, OpenFlags,
};
use wasm_bindgen::{
    prelude::{wasm_bindgen, Closure},
    JsCast, JsValue,
};
use web_sys::{console, js_sys, MessageEvent, Worker};

pub struct ClientDatabase {
    worker: Worker,
    _callback: Closure<dyn FnMut(MessageEvent)>,
}

impl ClientDatabase {
    pub fn new() -> Result<ClientDatabase, String> {
        let callback = get_callback_closure();
        let worker = spawn_worker(&callback)
            .map_err(|_e| "Failed to spawn web worker used for client database.")?;

        Ok(ClientDatabase {
            worker,
            _callback: callback,
        })
    }

    pub fn query_content_names(&self) -> String {
        let _ = &self.worker.post_message(&"query_content_names".into());
        "Check Terminal Output".to_string()
    }
}

pub fn spawn_worker(callback: &Closure<dyn FnMut(MessageEvent)>) -> Result<Worker, JsValue> {
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

pub fn get_callback_closure() -> Closure<dyn FnMut(MessageEvent)> {
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

pub fn connect_to_database() -> Result<Connection, String> {
    Connection::open_with_flags_and_vfs(
        DEFAULT_CLIENT_DATABASE_PATH,
        OpenFlags::SQLITE_OPEN_READ_WRITE
            | OpenFlags::SQLITE_OPEN_URI
            | OpenFlags::SQLITE_OPEN_NO_MUTEX,
        DEFAULT_WASM_VFS_NAME,
    )
    .map_err(|e| e.to_string())
}

pub fn query_game_info(db: &Connection) -> Result<(String, String), String> {
    let mut query = db
        .prepare("SELECT * FROM game_info WHERE id = 0")
        .map_err(|e| e.to_string())?;
    Ok(query
        .query_row((), |row| {
            let game_id: String = row.get("game_id")?;
            let game_version: String = row.get("game_version")?;
            Ok((game_id, game_version))
        })
        .map_err(|e| e.to_string())?)
}

#[wasm_bindgen(getter_with_clone)]
pub struct WorkerMessage {
    pub func: String,
    pub param: Option<String>,
}

#[wasm_bindgen]
pub fn query_content_names() -> String {
    let db = connect_to_database().unwrap();

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

#[wasm_bindgen]
pub async fn install_opfs_sahpool() -> Result<(), JsValue> {
    ffi::install_opfs_sahpool(
        Some(
            &OpfsSAHPoolCfgBuilder::new()
                .vfs_name(DEFAULT_WASM_VFS_NAME)
                .build(),
        ),
        false,
    )
    .await
    .map_err(|e| e.to_string())?;
    Ok(())
}

#[wasm_bindgen]
pub async fn check_database() -> bool {
    if let Ok(db) = connect_to_database() {
        if let Ok((game_id, game_version)) = query_game_info(&db) {
            console::log_1(
                &format!(
                    "WASM - Client database info. game_id: {}, game_version: {}",
                    game_id, game_version
                )
                .into(),
            );
            // TODO Better database update process
            if game_id == CLIENT_GAME_ID && game_version.contains(CLIENT_VERSION) {
                console::log_1(&"WASM - Database exists and matches game client version. Using existng database from the VFS.".into());
                return true;
            }
        }
    }
    false
}

#[wasm_bindgen]
pub async fn import_database(initial_db_file: Vec<u8>) -> Result<(), JsValue> {
    ffi::install_opfs_sahpool(
        Some(
            &OpfsSAHPoolCfgBuilder::new()
                .vfs_name(DEFAULT_WASM_VFS_NAME)
                .build(),
        ),
        false,
    )
    .await
    .map_err(|e| e.to_string())?
    .import_db(DEFAULT_CLIENT_DATABASE_PATH, &initial_db_file)
    .map_err(|e| e.to_string())?;

    console::log_1(&"WASM - Imported new database into the VFS.".into());
    Ok(())
}
