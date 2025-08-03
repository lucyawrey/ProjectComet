use super::{Asset, Content, GameInfo};
use bevy::prelude::*;
use bevy::utils::HashMap;
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
    Asset(Asset),
    Assets(Vec<Asset>),
    Content(Vec<Content>),
}

impl Plugin for DatabasePlugin {
    fn build(&self, app: &mut App) {
        let database = ClientDatabase::new().expect("Failed to initialize client database.");
        app.insert_resource(database);
        app.insert_resource(Data::default());
        app.add_systems(Startup, fetch_data);
    }
}

pub fn fetch_data(db: Res<ClientDatabase>, mut data: ResMut<Data>) {}

#[derive(Resource)]
pub struct ClientDatabase {}

impl ClientDatabase {
    pub fn new() -> Result<ClientDatabase, String> {
        Ok(ClientDatabase {})
    }

    pub fn query_game_info(&self) -> GameInfo {
        GameInfo {
            created_at: 1,
            updated_at: 1,
            game_id: "".to_string(),
            game_version: "".to_string(),
            supported_client_game_ids: vec!["".to_string()],
            game_display_name: "".to_string(),
        }
    }
}
