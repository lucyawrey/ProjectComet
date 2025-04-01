use super::{Content, GameInfo};
use bevy::{prelude::*, utils::HashMap};
use serde::{Deserialize, Serialize};

pub struct DatabasePlugin;

#[derive(Resource, Default)]
pub struct Data {
    pub game_info: Option<GameInfo>,
    pub content: HashMap<i64, Content>,
}

#[derive(Debug, Serialize, Deserialize)]
pub enum DatabaseResult {
    GameInfo(GameInfo),
    Content(Vec<Content>),
}
