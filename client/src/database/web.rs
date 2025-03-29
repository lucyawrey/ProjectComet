use super::{Content, GameInfo};
use crate::config::{
    CLIENT_GAME_ID, CLIENT_VERSION, DEFAULT_CLIENT_DATABASE_PATH, DEFAULT_WASM_VFS_NAME,
};
use rusqlite::{
    ffi::{self, OpfsSAHPoolCfgBuilder},
    Connection, OpenFlags,
};
use serde::{Deserialize, Serialize};
use serde_wasm_bindgen::Serializer;
use wasm_bindgen::{
    prelude::{wasm_bindgen, Closure},
    JsCast, JsValue,
};
use web_sys::{console, js_sys, DedicatedWorkerGlobalScope, MessageEvent, Worker};

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
        let msg = serde_wasm_bindgen::to_value(&WorkerMessage {
            func: "query_all_content".to_string(),
            args: Vec::new(),
        })
        .unwrap();
        let _ = &self.worker.post_message(&msg);
        "Check Terminal Output".to_string()
    }
}

#[derive(Serialize, Deserialize)]
pub struct WorkerMessage {
    pub func: String,
    pub args: Vec<String>,
}

#[derive(Serialize, Deserialize)]
pub enum ReturnMessage {
    GameInfo(GameInfo),
    AllContent(Vec<Content>),
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

pub fn get_worker_scope() -> DedicatedWorkerGlobalScope {
    js_sys::global().unchecked_into::<DedicatedWorkerGlobalScope>()
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

pub fn query_game_info(db: &Connection) -> Result<GameInfo, String> {
    let mut query = db
        .prepare("SELECT * FROM game_info WHERE id = 0")
        .map_err(|e| e.to_string())?;
    Ok(query
        .query_row((), |row| {
            let supported_client_game_ids: String = row.get("supported_client_game_ids")?;
            let game_info = GameInfo {
                game_id: row.get("game_id")?,
                game_version: row.get("game_version")?,
                supported_client_game_ids: serde_json::from_str(&supported_client_game_ids)
                    .map_err(|_e| rusqlite::Error::ExecuteReturnedResults)?,
                game_display_name: row.get("game_display_name")?,
                created_at: row.get("created_at")?,
                updated_at: row.get("updated_at")?,
            };

            let msg =
                serde_wasm_bindgen::to_value(&ReturnMessage::GameInfo(game_info.clone())).unwrap();
            let _ = get_worker_scope().post_message(&msg);

            return Ok(game_info);
        })
        .map_err(|e| e.to_string())?)
}

#[wasm_bindgen]
pub fn query_all_content() -> Result<(), JsValue> {
    let db = connect_to_database().unwrap();

    let mut query = db
        .prepare("SELECT * FROM content")
        .map_err(|e| e.to_string())?;
    let mut content = Vec::new();
    let rows = query
        .query_map([], |row| {
            let data: String = row.get("data")?;
            Ok(Content {
                id: row.get("id")?,
                updated_at: row.get("updated_at")?,
                name: row.get("name")?,
                content_type: row.get("content_type")?,
                content_subtype: row.get("content_subtype")?,
                data: serde_json::from_str(&data)
                    .map_err(|_e| rusqlite::Error::ExecuteReturnedResults)?,
                asset_id_0: row.get("asset_id_0")?,
                asset_id_1: row.get("asset_id_1")?,
                asset_id_2: row.get("asset_id_2")?,
                asset_id_3: row.get("asset_id_3")?,
                asset_id_4: row.get("asset_id_4")?,
                is_user_generated: row.get("is_user_generated")?,
                base_content_id: row.get("base_content_id")?,
                creator_user_handle: row.get("creator_user_handle")?,
            })
        })
        .map_err(|e| e.to_string())?;
    for row in rows {
        content.push(row.map_err(|e| e.to_string())?);
    }

    let serializer = Serializer::new().serialize_large_number_types_as_bigints(true);
    let msg = ReturnMessage::AllContent(content)
        .serialize(&serializer)
        .unwrap();
    let _ = get_worker_scope().post_message(&msg);

    Ok(())
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
        if let Ok(game_info) = query_game_info(&db) {
            console::log_1(
                &format!(
                    "WASM - Client database info. game_id: {}, game_version: {}",
                    game_info.game_id, game_info.game_version
                )
                .into(),
            );
            // TODO Better database update process
            if game_info.game_id == CLIENT_GAME_ID
                && game_info.game_version.contains(CLIENT_VERSION)
            {
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
