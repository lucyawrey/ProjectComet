#![allow(dead_code)]
pub const CLIENT_GAME_ID: &str = "project_comet";
pub const CLIENT_VERSION: &str = "0.1.0";
pub const DEFAULT_API_ADDRESS: &str = "127.0.0.1:50051";
pub const DEFAULT_FONT: &str = "FiraMono-Medium.ttf";

#[cfg(not(target_arch = "wasm32"))]
pub const DEFAULT_CLIENT_DATABASE_PATH: &str = "../api/client_data.sqlite";

#[cfg(target_arch = "wasm32")]
pub const DEFAULT_CLIENT_DATABASE_PATH: &str = "/client_data.sqlite";
#[cfg(target_arch = "wasm32")]
pub const DEFAULT_WASM_VFS_NAME: &str = "project_comet_vfs";
