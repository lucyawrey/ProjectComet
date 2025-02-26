use crate::{
    model::{
        fields::AccessLevel,
        tables::{
            AccessToken, Asset, Content, GameInfo, GameServer, User, UserPassword,
            UserRecoveryCode, World,
        },
    },
    queries::game_info::get_game_info_query,
    utils::{
        append_secret_to_file,
        authentication::{
            generate_access_token, generate_password, generate_recovery_code, get_random_id,
            hash_password,
        },
        get_magic_cookie, read_asset_file, read_dir_recursive,
    },
};
use sqlx::{query_as, Pool, Sqlite};
use std::fs;
use toml::{map::Map, Table, Value};

const NO_VALUE: Value = Value::Boolean(false);

pub async fn data_import(
    db: &Pool<Sqlite>,
    set_table_keys: Option<&[&str]>,
) -> Result<GameVersion, String> {
    let table_keys = set_table_keys.unwrap_or(&[
        "asset",
        "game_server",
        "world",
        "content",
        "user",
        "access_token",
    ]);
    let magic_cookie = get_magic_cookie();
    let mut data_toml_string = String::new();
    let files = read_dir_recursive("../data/content").unwrap();
    for file in files {
        data_toml_string = data_toml_string + &fs::read_to_string(file.path()).unwrap();
    }
    let data = data_toml_string.parse::<Table>().unwrap();

    let err_text = "Definiton invalid for: game_data.";
    let version = update_game_info(
        db,
        data.get("game_info")
            .unwrap_or(&NO_VALUE)
            .as_table()
            .ok_or(err_text)?,
    )
    .await?;
    if !version.is_new_version {
        return Ok(version);
    }

    for key in table_keys {
        let err_text = format!("Table definiton invalid for table: '{}'", key);
        let value = data.get(*key).ok_or(&err_text)?;
        for entry in value.as_array().ok_or(&err_text)? {
            let row = entry.as_table().ok_or(&err_text)?;

            match *key {
                "asset" => import_asset_row(db, &magic_cookie, row)
                    .await
                    .map_err(|e| format!("{}. {}", &err_text, e))?,
                "game_server" => import_game_server_row(db, row)
                    .await
                    .map_err(|e| format!("{}. {}", &err_text, e))?,
                "world" => import_world_row(db, row)
                    .await
                    .map_err(|e| format!("{}. {}", &err_text, e))?,
                "content" => import_content_row(db, row)
                    .await
                    .map_err(|e| format!("{}. {}", &err_text, e))?,
                "user" => import_user_row(db, row)
                    .await
                    .map_err(|e| format!("{}. {}", &err_text, e))?,
                "access_token" => import_access_token_row(db, row)
                    .await
                    .map_err(|e| format!("{}. {}", &err_text, e))?,
                _ => return Err("Unsupported table name.".to_owned()),
            };
        }
    }
    Ok(version)
}

pub struct GameVersion {
    pub is_new_version: bool,
    pub game_id: String,
    pub game_version: String,
}

pub async fn update_game_info(
    db: &Pool<Sqlite>,
    game_info: &Map<String, Value>,
) -> Result<GameVersion, String> {
    let game_id = game_info
        .get("game_id")
        .unwrap_or(&NO_VALUE)
        .as_str()
        .ok_or("Missing game_id.")?;
    let game_version = game_info
        .get("game_version")
        .unwrap_or(&NO_VALUE)
        .as_str()
        .ok_or("Missing game_version.")?;

    if !game_version.starts_with("dev") {
        if let Some(current_info) = get_game_info_query(&db).await {
            if game_id == current_info.game_id && game_version == current_info.game_version {
                return Ok(GameVersion {
                    is_new_version: false,
                    game_id: current_info.game_id,
                    game_version: current_info.game_version,
                });
            }
        }
    }

    let supported_client_game_ids = game_info
        .get("supported_client_game_ids")
        .unwrap_or(&NO_VALUE)
        .as_array()
        .map(|a| serde_json::to_string(a).ok())
        .flatten();
    let new_info = query_as::<_, GameInfo>(
            "INSERT INTO game_info (id, game_id, game_version, game_display_name, supported_client_game_ids) VALUES (0, $1, $2, $3, $4) ON CONFLICT(id) DO UPDATE SET game_id=excluded.game_id, game_version=excluded.game_version, game_display_name=excluded.game_display_name, supported_client_game_ids=excluded.supported_client_game_ids, updated_at=(unixepoch()) RETURNING *",
        )
        .bind(game_id)
        .bind(game_version)
        .bind(game_info.get("game_display_name").unwrap_or(&NO_VALUE).as_str())
        .bind(supported_client_game_ids)
        .fetch_one(db)
        .await
        .map_err(|e| e.to_string())?;
    print!("  Updated GameInfo\n");
    Ok(GameVersion {
        is_new_version: true,
        game_id: new_info.game_id,
        game_version: new_info.game_version,
    })
}

pub async fn import_content_row(db: &Pool<Sqlite>, row: &Map<String, Value>) -> Result<(), String> {
    let data = row
        .get("data")
        .unwrap_or(&NO_VALUE)
        .as_table()
        .map(|map| serde_json::to_string(map).ok())
        .flatten();
    let asset_ids: Vec<i64> = match row.get("asset_ids").unwrap_or(&NO_VALUE).as_array() {
        Some(s) => s.iter().map(|id| id.as_integer()).flatten().collect(),
        None => Vec::new(),
    };
    let new_row = query_as::<_, Content>(
            "INSERT INTO content (id, name, content_type, content_subtype, data, asset_id_0, asset_id_1, asset_id_2, asset_id_3, asset_id_4) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10) ON CONFLICT(id) DO UPDATE SET name=excluded.name, content_type=excluded.content_type, content_subtype=excluded.content_subtype, data=excluded.data, asset_id_0=excluded.asset_id_0, asset_id_1=excluded.asset_id_1, asset_id_2=excluded.asset_id_2, asset_id_3=excluded.asset_id_3, asset_id_4=excluded.asset_id_4, updated_at=(unixepoch()) RETURNING *",
        )
        .bind(row.get("id").unwrap_or(&NO_VALUE).as_integer())
        .bind(row.get("name").unwrap_or(&NO_VALUE).as_str())
        .bind(row.get("content_type").unwrap_or(&NO_VALUE).as_integer())
        .bind(row.get("content_subtype").unwrap_or(&NO_VALUE).as_integer())
        .bind(data)
        .bind(asset_ids.get(0))
        .bind(asset_ids.get(1))
        .bind(asset_ids.get(2))
        .bind(asset_ids.get(3))
        .bind(asset_ids.get(4))
        .fetch_one(db)
        .await
        .map_err(|e| e.to_string())?;
    print!("  Imported Content: {}\n", new_row.id);
    Ok(())
}

pub async fn import_user_row(db: &Pool<Sqlite>, row: &Map<String, Value>) -> Result<(), String> {
    let id = row
        .get("id")
        .unwrap_or(&NO_VALUE)
        .as_integer()
        .ok_or("Missing ID.")?;
    let is_id_conflict = sqlx::query("SELECT id from user WHERE id = $1")
        .bind(id)
        .fetch_one(db)
        .await
        .is_ok();
    // TODO poll for uniqueness
    let handle = get_random_id();

    let user_row = query_as::<_, User>(
            "INSERT INTO user (id, handle, username, role) VALUES ($1, $2, $3, $4) ON CONFLICT(id) DO UPDATE SET username=excluded.username, role=excluded.role, updated_at=(unixepoch()) RETURNING *",
        )
        .bind(id)
        .bind(handle)
        .bind(row.get("username").unwrap_or(&NO_VALUE).as_str())
        .bind(row.get("role").unwrap_or(&NO_VALUE).as_integer())
        .fetch_one(db)
        .await
        .map_err(|e| e.to_string())?;
    print!("  Imported User: {}\n", user_row.id);

    if row
        .get("generate_credentials")
        .unwrap_or(&NO_VALUE)
        .as_bool()
        .unwrap_or(false)
        && !is_id_conflict
    {
        let password = generate_password().ok_or("Failed to generate password.")?;
        let password_hash = hash_password(&password).ok_or("Failed to hash password.")?;
        let (recovery_code, recovery_code_hash) =
            generate_recovery_code().ok_or("Failed to generate recovery code.")?;

        let password_row = query_as::<_, UserPassword>(
            "INSERT INTO user_password (id, user_id, password_hash) VALUES ($1, $2, $3) RETURNING *",
        )
            .bind(id + 1)
            .bind(id)
            .bind(password_hash)
            .fetch_one(db)
            .await
            .map_err(|e| e.to_string())?;
        print!("  New UserPassword: {}\n", password_row.id);

        query_as::<_, UserRecoveryCode>(
            "INSERT INTO user_recovery_code (id, user_id) VALUES ($1, $2) RETURNING *",
        )
        .bind(recovery_code_hash)
        .bind(id)
        .fetch_one(db)
        .await
        .map_err(|e| e.to_string())?;
        print!("  New UserRecoveryCode\n");

        append_secret_to_file(format!(
            "username:password={}:{}\nusername:recovery_code={}:{}",
            user_row.username, password, user_row.username, recovery_code
        ));
    }

    Ok(())
}

pub async fn import_access_token_row(
    db: &Pool<Sqlite>,
    row: &Map<String, Value>,
) -> Result<(), String> {
    let id = row
        .get("id")
        .unwrap_or(&NO_VALUE)
        .as_integer()
        .ok_or("Missing id.")?;
    let access_level = row
        .get("access_level")
        .unwrap_or(&NO_VALUE)
        .as_integer()
        .map(|a| AccessLevel::try_from(a as i32).ok())
        .flatten()
        .ok_or("Missing access_level.")?;
    let game_server_id = if access_level == AccessLevel::GameServer {
        row.get("game_server_id").unwrap_or(&NO_VALUE).as_str()
    } else {
        None
    };

    let is_id_conflict = sqlx::query("SELECT id from access_token WHERE id = $1")
        .bind(id)
        .fetch_one(db)
        .await
        .is_ok();

    let (token, token_hash) = generate_access_token(id, &access_level, game_server_id)
        .ok_or("Failed to generate token.")?;

    let new_row = query_as::<_, AccessToken>(
            "INSERT INTO access_token (id, access_token_hash, access_level, game_server_id, expires_at) VALUES ($1, $2, $3, $4, $5) ON CONFLICT(id) DO UPDATE SET access_level=excluded.access_level, game_server_id=excluded.game_server_id, expires_at=excluded.expires_at RETURNING *",
        )
        .bind(id)
        .bind(token_hash)
        .bind(access_level)
        .bind(game_server_id)
        .bind(row.get("expires_at").unwrap_or(&NO_VALUE).as_integer())
        .fetch_one(db)
        .await
        .map_err(|e| e.to_string())?;
    print!("  Imported AccessToken: {}\n", new_row.id);

    if !is_id_conflict {
        append_secret_to_file(format!("access_token={}", token));
    }
    Ok(())
}

pub async fn import_game_server_row(
    db: &Pool<Sqlite>,
    row: &Map<String, Value>,
) -> Result<(), String> {
    let new_row = query_as::<_, GameServer>(
            "INSERT INTO game_server (id, region_code, display_name) VALUES ($1, $2, $3) ON CONFLICT(id) DO UPDATE SET region_code=excluded.region_code, display_name=excluded.display_name, updated_at=(unixepoch()) RETURNING *",
        )
        .bind(
            row.get("id")
                .unwrap_or(&NO_VALUE)
                .as_str()
                .ok_or("Missing ID.")?,
        )
        .bind(row.get("region_code").unwrap_or(&NO_VALUE).as_str())
        .bind(row.get("display_name").unwrap_or(&NO_VALUE).as_str())
        .fetch_one(db)
        .await
        .map_err(|e| e.to_string())?;
    print!("  Imported GameServer: {}\n", new_row.id);
    Ok(())
}

pub async fn import_world_row(db: &Pool<Sqlite>, row: &Map<String, Value>) -> Result<(), String> {
    let new_row = query_as::<_, World>(
            "INSERT INTO world (id, game_server_id, display_name) VALUES ($1, $2, $3) ON CONFLICT(id) DO UPDATE SET game_server_id=excluded.game_server_id, display_name=excluded.display_name, updated_at=(unixepoch()) RETURNING *",
        )
        .bind(
            row.get("id")
                .unwrap_or(&NO_VALUE)
                .as_str()
                .ok_or("Missing ID.")?,
        )
        .bind(row.get("game_server_id").unwrap_or(&NO_VALUE).as_str())
        .bind(row.get("display_name").unwrap_or(&NO_VALUE).as_str())
        .fetch_one(db)
        .await
        .map_err(|e| e.to_string())?;
    print!("  Imported World: {}\n", new_row.id);
    Ok(())
}

pub async fn import_asset_row(
    db: &Pool<Sqlite>,
    magic_cookie: &magic::Cookie<magic::cookie::Load>,
    row: &Map<String, Value>,
) -> Result<(), String> {
    let temp_path = row
        .get("source_path")
        .unwrap_or(&NO_VALUE)
        .as_str()
        .ok_or("Missing source path.")?;
    let source_path = format!("../data/{}", temp_path.trim().trim_matches('/'));

    let id = row
        .get("id")
        .unwrap_or(&NO_VALUE)
        .as_integer()
        .ok_or("Missing id.")?;
    let path = row
        .get("path")
        .unwrap_or(&NO_VALUE)
        .as_str()
        .ok_or("Missing path.")?;

    let (asset_data, file_size, file_type) =
        read_asset_file(&source_path, &magic_cookie).map_err(|e| e.to_string())?;
    let new_row = query_as::<_, Asset>(
            "INSERT INTO asset (id, path, file_type, data, size) VALUES ($1, $2, $3, $4, $5) ON CONFLICT(id) DO UPDATE SET path=excluded.path, file_type=excluded.file_type, data=excluded.data, size=excluded.size, updated_at=(unixepoch()) RETURNING *",
        )
        .bind(id)
        .bind(path)
        .bind(file_type)
        .bind(asset_data)
        .bind(file_size)
        .fetch_one(db)
        .await
        .map_err(|e| e.to_string())?;
    print!("  Imported Asset: {}\n", new_row.id);
    Ok(())
}
