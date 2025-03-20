use crate::{
    model::{
        fields::Role,
        tables::{Character, User},
    },
    utils::{
        authentication::get_random_id, generate_random_name, next_id, validate_and_format_name,
    },
};
use sonyflake::Sonyflake;
use sqlx::{query_as, Pool, Sqlite};

pub async fn create_character_query(
    db: &Pool<Sqlite>,
    sf: &Sonyflake,
    user: User,
    home_world_id: String,
    name: Option<String>,
    role: Option<Role>,
) -> Result<Character, String> {
    let name = match name {
        Some(name) => validate_and_format_name(name).ok_or("Character name is invalid.")?,
        None => generate_random_name(),
    };

    let role = match role {
        Some(role) => {
            if role > user.role {
                return Err(
                    "Character role cannot have higher access level than user role.".to_owned(),
                );
            }
            role
        }
        None => user.role,
    };

    let (id, created_at, _) = next_id(&sf)?;
    // TODO poll for uniqueness
    let handle = get_random_id();
    let new_character = query_as::<_, Character>(
            "INSERT INTO character (id, handle, updated_at, name, role, home_world_id, user_id) VALUES ($1, $2, $3, $4, $5, $6, $7) RETURNING *",
        )
        .bind(id)
        .bind(handle)
        .bind(created_at)
        .bind(name)
        .bind(role)
        .bind(home_world_id)
        .bind(user.id)
        .fetch_one(db)
        .await
        .map_err(|e| e.to_string())?;

    Ok(new_character)
}
