use crate::{
    model::tables::{AccessToken, User, UserPassword, UserRecoveryCode, UserSession},
    utils::{
        authentication::{
            generate_session_token, hash_token, parse_access_token_id, verify_password,
            verify_token,
        },
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

pub async fn validate_session_query(
    db: &Pool<Sqlite>,
    session_token: &str,
) -> Result<(User, UserSession), ()> {
    let session_token_hash = hash_token(session_token).ok_or(())?;
    let session = query_as::<_, UserSession>("SELECT * FROM user_session WHERE id = $1")
        .bind(session_token_hash)
        .fetch_one(db)
        .await
        .map_err(|_e| ())?;
    let user = query_as::<_, User>("SELECT * FROM user WHERE id = $1")
        .bind(session.user_id)
        .fetch_one(db)
        .await
        .map_err(|_e| ())?;
    Ok((user, session))
}

pub async fn validate_recovery_code_query(
    db: &Pool<Sqlite>,
    recovery_code: &str,
) -> Result<(User, UserRecoveryCode), ()> {
    let recovery_code_hash = hash_token(recovery_code).ok_or(())?;
    let code = query_as::<_, UserRecoveryCode>("SELECT * FROM user_recovery_code WHERE id = $1")
        .bind(recovery_code_hash)
        .fetch_one(db)
        .await
        .map_err(|_e| ())?;
    let user = query_as::<_, User>("SELECT * FROM user WHERE id = $1")
        .bind(code.user_id)
        .fetch_one(db)
        .await
        .map_err(|_e| ())?;
    Ok((user, code))
}

pub async fn validate_access_token_query(
    db: &Pool<Sqlite>,
    access_token: &str,
) -> Result<AccessToken, ()> {
    let id = parse_access_token_id(access_token).ok_or(())?;
    let access = query_as::<_, AccessToken>("SELECT * FROM access_token WHERE id = $1")
        .bind(id)
        .fetch_one(db)
        .await
        .map_err(|_e| ())?;
    if verify_token(access_token, &access.access_token_hash) {
        Ok(access)
    } else {
        Err(())
    }
}
