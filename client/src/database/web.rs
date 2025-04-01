use super::{query_all_content, query_game_info, Data, DatabasePlugin, DatabaseResult};
use crate::config::{
    CLIENT_GAME_ID, CLIENT_VERSION, DEFAULT_CLIENT_DATABASE_PATH, DEFAULT_WASM_VFS_NAME,
};
use bevy::prelude::*;
use rusqlite::{
    ffi::{self, OpfsSAHPoolCfgBuilder},
    Connection, OpenFlags,
};
use serde::{Deserialize, Serialize};
use serde_wasm_bindgen::Serializer;
use std::sync::{mpsc, mpsc::Receiver};
use wasm_bindgen::{
    prelude::{wasm_bindgen, Closure},
    JsCast, JsValue,
};
use web_sys::{console, js_sys, DedicatedWorkerGlobalScope, MessageEvent, Worker};

/* Plugin */
impl Plugin for DatabasePlugin {
    fn build(&self, app: &mut App) {
        app.insert_resource(Data::default());
        app.add_systems(Startup, (setup, fetch_data).chain());
        app.add_systems(Update, process_messages);
    }
}

pub fn setup(world: &mut World) {
    let database = ClientDatabase::new().expect("Failed to initialize client database.");
    world.insert_non_send_resource(database);
}

pub fn fetch_data(db: NonSend<ClientDatabase>) {
    db.query_content();
}

pub fn process_messages(db: NonSend<ClientDatabase>, mut data: ResMut<Data>) {
    loop {
        match db.receiver.try_recv() {
            Ok(result) => match result {
                DatabaseResult::GameInfo(game_info) => data.game_info = Some(game_info),
                DatabaseResult::Content(content) => {
                    for item in content {
                        data.content.insert(item.id, item);
                    }
                }
            },
            Err(_) => break,
        }
    }
}
/* End Plugin */

pub struct ClientDatabase {
    pub receiver: Receiver<DatabaseResult>,
    worker: Worker,
    _callback: Closure<dyn FnMut(MessageEvent)>,
}

impl ClientDatabase {
    pub fn new() -> Result<ClientDatabase, String> {
        let (sender, receiver) = mpsc::channel();
        let callback = Closure::new(move |event: MessageEvent| {
            let data = event.data();
            if let Some(text) = data.as_string() {
                if text.as_str() == "loading" {
                    console::log_1(&"WASM - Worker loading...".into());
                    return;
                }
            }
            let msg: DatabaseResult = serde_wasm_bindgen::from_value(data).unwrap();
            sender.send(msg).expect("Failed to send message.");
        });
        let worker = spawn_worker(&callback)
            .map_err(|_e| "Failed to spawn web worker used for client database.")?;

        Ok(ClientDatabase {
            receiver,
            worker,
            _callback: callback,
        })
    }

    pub fn query_content(&self) {
        let msg = serde_wasm_bindgen::to_value(&WorkerDatabaseRequest {
            func: "query_all_content_bind".to_string(),
            args: Vec::new(),
        })
        .unwrap();
        let _ = &self.worker.post_message(&msg);
    }
}

#[derive(Serialize, Deserialize)]
pub struct WorkerDatabaseRequest {
    pub func: String,
    pub args: Vec<String>,
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

#[wasm_bindgen]
pub fn query_all_content_bind() -> Result<(), JsValue> {
    let db = connect_to_database()?;
    let content = query_all_content(&db)?;

    let serializer = Serializer::new().serialize_large_number_types_as_bigints(true);
    let msg = DatabaseResult::Content(content)
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

            let serializer = Serializer::new().serialize_large_number_types_as_bigints(true);
            let msg = DatabaseResult::GameInfo(game_info.clone())
                .serialize(&serializer)
                .unwrap();
            let _ = get_worker_scope().post_message(&msg);

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
