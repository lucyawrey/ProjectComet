use crate::{
    model::tables::{User, UserPassword, UserSession},
    utils::{
        authentication::{generate_session_token, hash_token, verify_password},
        current_timestamp,
    },
};
use sqlx::{query_as, Pool, Sqlite};

const DEFAULT_LIFETIME: i64 = 60 * 60 * 24 * 14;

pub async fn user_login_query(
    db: &Pool<Sqlite>,
    username: &str,
    password: &str,
) -> Result<(String, User), ()> {
    let user = query_as::<_, User>("SELECT * FROM user WHERE username = $1")
        .bind(username)
        .fetch_one(db)
        .await
        .map_err(|_e| ())?;
    let stored_password =
        query_as::<_, UserPassword>("SELECT * FROM user_password WHERE user_id = $1")
            .bind(user.id)
            .fetch_one(db)
            .await
            .map_err(|_e| ())?;
    if verify_password(password, &stored_password.password_hash) {
        let session_token = create_user_session_query(db, user.id, DEFAULT_LIFETIME).await?;
        Ok((session_token, user))
    } else {
        Err(())
    }
}

pub async fn validate_session_query(db: &Pool<Sqlite>, session_token: &str) -> Result<User, ()> {
    let session_token_hash = hash_token(session_token).ok_or(())?;
    let session = query_as::<_, UserSession>("SELECT * FROM session WHERE id = $1")
        .bind(session_token_hash)
        .fetch_one(db)
        .await
        .map_err(|_e| ())?;
    let user = query_as::<_, User>("SELECT * FROM user WHERE id = $1")
        .bind(session.user_id)
        .fetch_one(db)
        .await
        .map_err(|_e| ())?;
    Ok(user)
}

pub async fn create_user_session_query(
    db: &Pool<Sqlite>,
    user_id: i64,
    lifetime_seconds: i64,
) -> Result<String, ()> {
    let (session_token, session_token_hash) = generate_session_token().ok_or(())?;
    let expires_at = current_timestamp() + lifetime_seconds;
    query_as::<_, UserSession>(
        "INSERT INTO user_session (id, expires_at, user_id) VALUES ($1, $2, $3) RETURNING *",
    )
    .bind(session_token_hash)
    .bind(expires_at)
    .bind(user_id)
    .fetch_one(db)
    .await
    .map_err(|_e| ())?;
    Ok(session_token)
}
