#[cfg_attr(
    any(target_family = "unix", target_family = "windows"),
    path = "desktop.rs"
)]
#[cfg_attr(all(target_family = "wasm", target_os = "unknown"), path = "web.rs")]
pub mod platform;
pub mod plugin;
pub mod queries;
pub mod tables;
pub use platform::*;
pub use plugin::*;
pub use queries::*;
pub use tables::*;
