use super::authentication::authenticate_from_token;
use crate::model::authentication::AuthStatus;
use sqlx::{Pool, Sqlite};
use tonic::Request;

pub async fn authenticate<T>(db: &Pool<Sqlite>, req: &Request<T>) -> AuthStatus {
    let authorization = req
        .metadata()
        .get("authorization")
        .map(|m| m.to_str().ok())
        .flatten();
    println!("Token: {:?}\n", authorization);
    let auth_status = authenticate_from_token(&db, authorization).await;
    println!("Auth Status: {:?}\n", auth_status);
    auth_status
}
