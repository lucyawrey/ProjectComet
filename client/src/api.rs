use bevy::prelude::*;

pub struct ApiPlugin;

#[derive(Resource, Default, Clone)]
pub struct Data {
    pub game_info: Option<GameInfo>,
}

#[derive(Debug)]
pub enum ApiResult {
    GameInfo(GameInfo),
}

impl Plugin for ApiPlugin {
    fn build(&self, app: &mut App) {
        let database = ApiConnection::new().expect("Failed to initialize API connection.");
        app.insert_resource(database);
        app.insert_resource(Data::default());
        app.add_systems(Startup, fetch_data);
    }
}

pub fn fetch_data(api: Res<ApiConnection>, mut data: ResMut<Data>) {
    data.game_info = Some(api.fetch_game_info());
}

#[derive(Resource)]
pub struct ApiConnection {}

impl ApiConnection {
    pub fn new() -> Result<ApiConnection, String> {
        Ok(ApiConnection {})
    }

    pub fn fetch_game_info(&self) -> GameInfo {
        GameInfo {
            created_at: 1,
            updated_at: 1,
            game_id: "project_comet".to_string(),
            game_version: "0.1.0.0".to_string(),
            supported_client_game_ids: vec!["".to_string()],
            supported_client_game_versions: vec!["".to_string()],
            game_display_name: "".to_string(),
        }
    }
}

#[derive(Debug, Clone)]
pub struct GameInfo {
    pub created_at: i64,
    pub updated_at: i64,
    pub game_id: String,
    pub game_version: String,
    pub supported_client_game_ids: Vec<String>,
    pub supported_client_game_versions: Vec<String>,
    pub game_display_name: String,
}
