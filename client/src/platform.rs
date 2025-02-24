use crate::config::DEFAULT_CLIENT_DATABASE_PATH;
use rusqlite::Connection;

#[cfg(any(target_family = "unix", target_family = "windows"))]
pub fn get_database() -> Result<Connection, ()> {
    Connection::open(DEFAULT_CLIENT_DATABASE_PATH).map_err(|_e| ())
}

// WASM Implementation
#[cfg(all(target_family = "wasm", target_os = "unknown"))]
use rusqlite::OpenFlags;
#[cfg(all(target_family = "wasm", target_os = "unknown"))]
use sqlite_wasm_rs::export::{self as ffi, OpfsSAHPoolCfgBuilder};
#[cfg(all(target_family = "wasm", target_os = "unknown"))]
use wasm_bindgen::prelude::wasm_bindgen;

#[cfg(all(target_family = "wasm", target_os = "unknown"))]
pub fn get_database() -> Result<Connection, ()> {
    // let mut db = std::ptr::null_mut();
    // let _ret = unsafe {
    //     ffi::sqlite3_open_v2(
    //         c"project_comet_vfs.db".as_ptr().cast(),
    //         &mut db as *mut _,
    //         ffi::SQLITE_OPEN_READWRITE | ffi::SQLITE_OPEN_CREATE,
    //         std::ptr::null(),
    //     )
    // };
    // Connection::open_with_flags_and_vfs(
    //     DEFAULT_CLIENT_DATABASE_PATH,
    //     OpenFlags::default(),
    //     "project_comet_vfs",
    // )
    // .map_err(|_e| ())
    Connection::open(DEFAULT_CLIENT_DATABASE_PATH).map_err(|_e| ())
}

#[cfg(all(target_family = "wasm", target_os = "unknown"))]
#[wasm_bindgen]
pub async fn install_opfs_sahpool() -> bool {
    let config = OpfsSAHPoolCfgBuilder::new()
        .vfs_name("project_comet_vfs")
        .directory("")
        .build();
    if let Err(_) = ffi::install_opfs_sahpool(Some(&config), false).await {
        return false;
    }
    if let Err(_) = Connection::open_with_flags_and_vfs(
        DEFAULT_CLIENT_DATABASE_PATH,
        OpenFlags::default(),
        "project_comet_vfs",
    ) {
        return false;
    }
    true
}
