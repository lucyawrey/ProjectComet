use crate::{
    model::{
        authentication::{AuthStatus, AuthType},
        fields::AccessLevel,
    },
    queries::authentication::{
        validate_access_token_query, validate_recovery_code_query, validate_session_query,
    },
};
use argon2::{password_hash::SaltString, Argon2, PasswordHash, PasswordHasher, PasswordVerifier};
use base32ct::Encoding;
use base64ct::Encoding as Encoding64;
use rand::{rngs::OsRng, TryRngCore};
use sha2::{Digest, Sha256};
use sqlx::{Pool, Sqlite};

// TODO precise encoding buffer sizes

pub fn hash_password(password: &str) -> Option<String> {
    let salt = SaltString::generate(argon2::password_hash::rand_core::OsRng);
    let argon2 = Argon2::default();
    Some(
        argon2
            .hash_password(password.as_bytes(), salt.as_salt())
            .ok()?
            .to_string(),
    )
}

pub fn verify_password(password: &str, stored_hash: &str) -> bool {
    if let Ok(hash) = PasswordHash::try_from(stored_hash) {
        Argon2::default()
            .verify_password(password.as_bytes(), &hash)
            .is_ok()
    } else {
        false
    }
}

/// Used to hash `session_token`, `access_token`, and `recovery_code`.
pub fn hash_token(token: &str) -> Option<String> {
    let hash = Sha256::digest(token);
    let mut buf = [0u8; 100];
    let hex_hash = base16ct::lower::encode_str(&hash, &mut buf).ok()?;
    Some(hex_hash.to_string())
}

/// Used to verify hashes of `session_token`, `access_token`, and `recovery_code`.
pub fn verify_token(token: &str, stored_hash: &str) -> bool {
    let hashed_token_bytes = Sha256::digest(token);
    let mut buf = [0u8; 100];
    let stored_hash_bytes = match base16ct::lower::decode(stored_hash, &mut buf) {
        Ok(bytes) => bytes,
        Err(_) => return false,
    };
    hashed_token_bytes[..] == stored_hash_bytes[..]
}

pub fn generate_password() -> Option<String> {
    let bytes: [u8; 16] = get_random_bytes()?;
    Some(base64ct::Base64Unpadded::encode_string(&bytes))
}

pub fn get_random_bytes<const COUNT: usize>() -> Option<[u8; COUNT]> {
    let mut bytes = [0u8; COUNT];
    OsRng.try_fill_bytes(&mut bytes).ok()?;
    Some(bytes)
}

pub fn generate_session_token() -> Option<(String, String)> {
    let bytes: [u8; 20] = get_random_bytes()?;
    let mut buf = [0u8; 100];
    let token_base32 = base32ct::Base32Unpadded::encode(&bytes, &mut buf).ok()?;
    let token_hash = hash_token(token_base32)?;
    Some((token_base32.to_string(), token_hash))
}

/// Returned tokens have the format `default|server:gameserverid|admin_IdBase32Representation_secret`.
pub fn generate_access_token(
    access_token_id: i64,
    access_level: &AccessLevel,
    game_server_id: Option<&str>,
) -> Option<(String, String)> {
    let bytes: [u8; 20] = get_random_bytes()?;
    let mut buf = [0u8; 100];
    let secret_base32 = base32ct::Base32Unpadded::encode(&bytes, &mut buf).ok()?;
    let mut buf2 = [0u8; 100];
    let id_base32 =
        base32ct::Base32Unpadded::encode(&access_token_id.to_be_bytes(), &mut buf2).ok()?;
    let access_level_str = match access_level {
        AccessLevel::Default => "default",
        AccessLevel::GameServer => &format!("server:{}", game_server_id?),
        AccessLevel::Administrator => "admin",
    };
    let token = format!("{}_{}_{}", access_level_str, id_base32, secret_base32);
    let token_hash = hash_token(&token)?;
    Some((token, token_hash))
}

pub fn parse_access_token_id(access_token: &str) -> Option<i64> {
    let split: Vec<&str> = access_token.splitn(3, '_').collect();
    let base_32_id = split.get(1)?;
    let mut buf = [0u8; 8];
    base32ct::Base32Unpadded::decode(base_32_id, &mut buf).ok()?;
    Some(i64::from_be_bytes(buf))
}

pub fn generate_recovery_code() -> Option<(String, String)> {
    let bytes: [u8; 32] = get_random_bytes()?;
    let mut buf = [0u8; 100];
    let code_base32 = base32ct::Base32Unpadded::encode(&bytes, &mut buf).ok()?;
    let code_hash = hash_token(code_base32)?;
    Some((code_base32.to_string(), code_hash))
}

pub async fn authenticate_from_token(db: &Pool<Sqlite>, token: Option<&str>) -> AuthStatus {
    match token {
        Some(token) => {
            if token.chars().count() < 33 {
                match validate_session_query(db, token).await {
                    Ok((user, session)) => {
                        AuthStatus::Authenticated(AuthType::UserSession(user, session))
                    }
                    Err(_) => AuthStatus::Unauthenticated,
                }
            } else if token.contains('_') {
                match validate_access_token_query(db, token).await {
                    Ok(access_token) => {
                        AuthStatus::Authenticated(AuthType::AccessToken(access_token))
                    }
                    Err(_) => AuthStatus::Unauthenticated,
                }
            } else {
                match validate_recovery_code_query(db, token).await {
                    Ok((user, recovery_code)) => {
                        AuthStatus::Authenticated(AuthType::UserRecoveryCode(user, recovery_code))
                    }
                    Err(_) => AuthStatus::Unauthenticated,
                }
            }
        }
        None => AuthStatus::Unauthenticated,
    }
}
