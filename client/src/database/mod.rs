#[cfg_attr(not(target_arch = "wasm32"), path = "desktop.rs")]
#[cfg_attr(target_arch = "wasm32", path = "web.rs")]
pub mod platform;
pub mod plugin;
pub mod queries;
pub mod tables;
pub use platform::*;
pub use plugin::*;
pub use queries::*;
pub use tables::*;
