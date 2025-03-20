use crate::api::Message;
use crate::api::{game_data_server::GameData, AddItemRequest};
use sonyflake::Sonyflake;
use sqlx::pool::Pool;
use sqlx::Sqlite;
use tonic::{Request, Response, Status};

pub struct GameDataService {
    db: Pool<Sqlite>,
    sf: Sonyflake,
}

impl GameDataService {
    pub fn new(db: Pool<Sqlite>, sf: Sonyflake) -> GameDataService {
        GameDataService { db, sf }
    }
}

#[tonic::async_trait]
impl GameData for GameDataService {
    /// TODO
    async fn get_game_server_startup_data(
        &self,
        request: Request<Message>,
    ) -> Result<Response<Message>, Status> {
        Ok(Response::new(Message {
            message: format!("Request: {:?}", request),
        }))
    }
    /// TODO
    async fn log_in_caracter(
        &self,
        request: Request<Message>,
    ) -> Result<Response<Message>, Status> {
        Ok(Response::new(Message {
            message: format!("Request: {:?}", request),
        }))
    }
    /// TODO
    async fn update_caracter(
        &self,
        request: Request<Message>,
    ) -> Result<Response<Message>, Status> {
        Ok(Response::new(Message {
            message: format!("Request: {:?}", request),
        }))
    }
    /// TODO
    async fn add_item(
        &self,
        request: Request<AddItemRequest>,
    ) -> Result<Response<Message>, Status> {
        Ok(Response::new(Message {
            message: format!("Request: {:?}", request),
        }))
    }
}
