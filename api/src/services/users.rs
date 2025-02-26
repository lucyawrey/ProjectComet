use crate::{
    api::{
        users_server::Users, Character, CreateCharacterRequest, LogInCharacterReply,
        LogInCharacterRequest, LogInReply, LogInRequest, Message, User,
    },
    model::fields::Role,
    queries::{authentication::user_login_query, character::create_character_query},
    utils::{authentication::id_to_base32, transport::authenticate},
};
use sonyflake::Sonyflake;
use sqlx::pool::Pool;
use sqlx::Sqlite;
use tonic::{Request, Response, Status};

pub struct UsersService {
    db: Pool<Sqlite>,
    sf: Sonyflake,
}

impl UsersService {
    pub fn new(db: Pool<Sqlite>, sf: Sonyflake) -> UsersService {
        UsersService { db, sf }
    }
}

#[tonic::async_trait]
impl Users for UsersService {
    /// TODO
    async fn sign_up(&self, request: Request<Message>) -> Result<Response<Message>, Status> {
        Ok(Response::new(Message {
            message: format!("Request: {:?}", request),
        }))
    }

    async fn log_in(&self, request: Request<LogInRequest>) -> Result<Response<LogInReply>, Status> {
        let args = request.into_inner();

        let (session_token, session_user) =
            user_login_query(&self.db, &args.username, &args.password)
                .await
                .map_err(|_e| Status::unauthenticated("Invalid username or password."))?;
        Ok(Response::new(LogInReply {
            session_token,
            session_user: Some(User {
                id: session_user.id,
                handle: id_to_base32(session_user.handle)
                    .ok_or(Status::internal("Failed to encode handle."))?,
                updated_at: session_user.updated_at.and_utc().timestamp(),
                username: session_user.username,
                role: session_user.role.into(),
            }),
        }))
    }
    /// TODO
    async fn log_out(&self, request: Request<Message>) -> Result<Response<Message>, Status> {
        Ok(Response::new(Message {
            message: format!("Request: {:?}", request),
        }))
    }
    /// TODO
    async fn delete_user(&self, request: Request<Message>) -> Result<Response<Message>, Status> {
        Ok(Response::new(Message {
            message: format!("Request: {:?}", request),
        }))
    }
    /// TODO
    async fn update_username(
        &self,
        request: Request<Message>,
    ) -> Result<Response<Message>, Status> {
        Ok(Response::new(Message {
            message: format!("Request: {:?}", request),
        }))
    }
    /// TODO
    async fn update_password(
        &self,
        request: Request<Message>,
    ) -> Result<Response<Message>, Status> {
        Ok(Response::new(Message {
            message: format!("Request: {:?}", request),
        }))
    }
    /// TODO
    async fn verify_recovery_code(
        &self,
        request: Request<Message>,
    ) -> Result<Response<Message>, Status> {
        Ok(Response::new(Message {
            message: format!("Request: {:?}", request),
        }))
    }
    /// TODO
    async fn recover_user(&self, request: Request<Message>) -> Result<Response<Message>, Status> {
        Ok(Response::new(Message {
            message: format!("Request: {:?}", request),
        }))
    }

    async fn create_character(
        &self,
        request: Request<CreateCharacterRequest>,
    ) -> Result<Response<Character>, Status> {
        let (user, _) = authenticate(&self.db, &request).await.auth_session_or()?;
        let args = request.into_inner();

        let new = create_character_query(
            &self.db,
            &self.sf,
            user,
            args.home_world_id,
            args.name,
            match args.role {
                Some(role) => Role::try_from(role).ok(),
                None => None,
            },
        )
        .await
        .map_err(|e| Status::internal(e.to_string()))?;

        println!("New DB Character: {:?}", new);
        Ok(Response::new(Character {
            id: new.id,
            handle: id_to_base32(new.handle).ok_or(Status::internal("Failed to encode handle."))?,
            updated_at: new.updated_at.and_utc().timestamp(),
            name: new.name,
            role: new.role.into(),
            home_world_id: new.home_world_id,
            user_id: new.user_id,
            ancestry: new.ancestry.into(),
            gender: new.gender.into(),
            customization: "TODO: Serialize Json<Customization>".to_owned(),
            data: "TODO: Serialize Json<CharacterData>".to_owned(),
        }))
    }
    /// TODO
    async fn get_character(&self, request: Request<Message>) -> Result<Response<Message>, Status> {
        Ok(Response::new(Message {
            message: format!("Request: {:?}", request),
        }))
    }
    /// TODO
    async fn get_characters(&self, request: Request<Message>) -> Result<Response<Message>, Status> {
        Ok(Response::new(Message {
            message: format!("Request: {:?}", request),
        }))
    }
    /// TODO
    async fn delete_character(
        &self,
        request: Request<Message>,
    ) -> Result<Response<Message>, Status> {
        Ok(Response::new(Message {
            message: format!("Request: {:?}", request),
        }))
    }
    async fn get_worlds(&self, request: Request<Message>) -> Result<Response<Message>, Status> {
        Ok(Response::new(Message {
            message: format!("Request: {:?}", request),
        }))
    }
    /// TODO
    async fn log_in_character(
        &self,
        request: Request<LogInCharacterRequest>,
    ) -> Result<Response<LogInCharacterReply>, Status> {
        Ok(Response::new(LogInCharacterReply {
            server_address: format!("Request: {:?}", request),
        }))
    }
}
