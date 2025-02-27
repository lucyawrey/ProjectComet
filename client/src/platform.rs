use crate::config::DEFAULT_CLIENT_DATABASE_PATH;
use rusqlite::{Connection, OpenFlags};

#[cfg(any(target_family = "unix", target_family = "windows"))]
pub fn get_database() -> Result<Connection, ()> {
    Connection::open_with_flags(
        DEFAULT_CLIENT_DATABASE_PATH,
        OpenFlags::SQLITE_OPEN_READ_WRITE
            | OpenFlags::SQLITE_OPEN_URI
            | OpenFlags::SQLITE_OPEN_NO_MUTEX,
    )
    .map_err(|_e| ())
}

#[cfg(all(target_family = "wasm", target_os = "unknown"))]
pub fn get_database() -> Result<Connection, ()> {
    Connection::open_with_flags(
        DEFAULT_CLIENT_DATABASE_PATH,
        OpenFlags::SQLITE_OPEN_READ_WRITE
            | OpenFlags::SQLITE_OPEN_URI
            | OpenFlags::SQLITE_OPEN_NO_MUTEX,
    )
    .map_err(|_e| ())
}

#[cfg(all(target_family = "wasm", target_os = "unknown"))]
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

#[cfg(all(target_family = "wasm", target_os = "unknown"))]
pub fn query() {
    use crate::config::DEFAULT_WASM_VFS_NAME;
    use web_sys::console;

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

#[cfg(all(target_family = "wasm", target_os = "unknown"))]
pub struct Work {
    func: Box<dyn FnOnce() + Send>,
}

#[cfg(all(target_family = "wasm", target_os = "unknown"))]
pub fn get_callback_closure() -> wasm_bindgen::prelude::Closure<dyn FnMut(web_sys::MessageEvent)> {
    use wasm_bindgen::prelude::Closure;
    use web_sys::{console, MessageEvent};

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

#[cfg(all(target_family = "wasm", target_os = "unknown"))]
pub fn spawn_worker(
    callback: &wasm_bindgen::prelude::Closure<dyn FnMut(web_sys::MessageEvent)>,
) -> Result<web_sys::Worker, wasm_bindgen::JsValue> {
    use wasm_bindgen::JsCast;
    use web_sys::{console, js_sys, Worker};

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

#[cfg(all(target_family = "wasm", target_os = "unknown"))]
pub fn get_worker_scope() -> web_sys::DedicatedWorkerGlobalScope {
    use wasm_bindgen::JsCast;
    use web_sys::{js_sys, DedicatedWorkerGlobalScope};

    js_sys::global().unchecked_into::<DedicatedWorkerGlobalScope>()
}

#[cfg(all(target_family = "wasm", target_os = "unknown"))]
pub fn run_in_worker(
    worker: &web_sys::Worker,
    f: impl FnOnce() + Send + 'static,
) -> Result<&web_sys::Worker, wasm_bindgen::JsValue> {
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
pub async fn install_opfs_sahpool(initial_db_file: Vec<u8>) -> Result<(), wasm_bindgen::JsValue> {
    use crate::config::{CLIENT_GAME_ID, CLIENT_VERSION, DEFAULT_WASM_VFS_NAME};
    use rusqlite::ffi::{self, OpfsSAHPoolCfgBuilder};
    use web_sys::console;

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

    // TODO fetch db file with worker.
    opfs_util
        .import_db(DEFAULT_CLIENT_DATABASE_PATH, &initial_db_file)
        .unwrap();
    console::log_1(&"WASM - Imported new database into the VFS.".into());

    Ok(())
}

#[cfg(all(target_family = "wasm", target_os = "unknown"))]
#[wasm_bindgen::prelude::wasm_bindgen]
pub fn child_entry_point(ptr: u32) -> Result<(), wasm_bindgen::JsValue> {
    let ptr = unsafe { Box::from_raw(ptr as *mut Work) };
    (ptr.func)();

    let worker_scope = get_worker_scope();
    worker_scope.post_message(&"callback_done".into())?;
    Ok(())
}
