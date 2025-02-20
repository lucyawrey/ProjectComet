use crate::{
    api::{
        create_character_request::UserRef, users_server::Users, Character, CreateCharacterRequest,
        LogInReply, LogInRequest, Message, User,
    },
    model::{fields::Role, Ref},
    queries::{authentication::user_login_query, character::create_character_query},
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
    async fn create_character(
        &self,
        request: Request<CreateCharacterRequest>,
    ) -> Result<Response<Character>, Status> {
        let args = request.into_inner();

        let new = create_character_query(
            &self.db,
            &self.sf,
            match args
                .user_ref
                .ok_or(Status::internal("Must provide user ID or Username."))?
            {
                UserRef::Id(id) => Ref::Id(id),
                UserRef::Username(name) => Ref::Name(name),
            },
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
}
