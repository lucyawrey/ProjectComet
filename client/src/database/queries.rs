use super::{Content, GameInfo, Ref};
use rusqlite::Connection;

/* game_info */
pub fn query_game_info(db: &Connection) -> Result<GameInfo, String> {
    let mut query = db
        .prepare("SELECT * FROM game_info WHERE id = 0")
        .map_err(|e| e.to_string())?;
    let game_info = query
        .query_row((), |row| {
            let supported_client_game_ids: String = row.get("supported_client_game_ids")?;
            let game_info = GameInfo {
                game_id: row.get("game_id")?,
                game_version: row.get("game_version")?,
                supported_client_game_ids: serde_json::from_str(&supported_client_game_ids)
                    .map_err(|_e| rusqlite::Error::ExecuteReturnedResults)?,
                game_display_name: row.get("game_display_name")?,
                created_at: row.get("created_at")?,
                updated_at: row.get("updated_at")?,
            };
            return Ok(game_info);
        })
        .map_err(|e| e.to_string())?;
    Ok(game_info)
}

pub fn query_upsert_game_info(db: &Connection, game_info: GameInfo) -> Result<i64, String> {
    let client_ids =
        serde_json::to_string(&game_info.supported_client_game_ids).map_err(|e| e.to_string())?;
    db
        .execute(
            "INSERT INTO game_info (id, game_id, game_version, game_display_name, supported_client_game_ids) VALUES (0, $1, $2, $3, $4) ON CONFLICT(id) DO UPDATE SET game_id=excluded.game_id, game_version=excluded.game_version, game_display_name=excluded.game_display_name, supported_client_game_ids=excluded.supported_client_game_ids, updated_at=(unixepoch()) RETURNING *",
            (game_info.game_id, game_info.game_version, game_info.game_display_name, client_ids),
        )
        .map_err(|e| e.to_string())?;
    Ok(0)
}

/* content */
pub fn query_content_by_ref(db: &Connection, referance: Ref) -> Result<Content, String> {
    let (mut query, ref_string) = match referance {
        Ref::Id(id) => (
            db.prepare("SELECT * FROM content WHERE (id) = (?1)")
                .map_err(|e| e.to_string())?,
            id.to_string(),
        ),
        Ref::Name(name) => (
            db.prepare("SELECT * FROM content WHERE (name) = (?1)")
                .map_err(|e| e.to_string())?,
            name,
        ),
    };
    let row = query
        .query_row([ref_string], |row| {
            let data: String = row.get("data")?;
            Ok(Content {
                id: row.get("id")?,
                updated_at: row.get("updated_at")?,
                name: row.get("name")?,
                content_type: row.get("content_type")?,
                content_subtype: row.get("content_subtype")?,
                data: serde_json::from_str(&data)
                    .map_err(|_e| rusqlite::Error::ExecuteReturnedResults)?,
                asset_id_0: row.get("asset_id_0")?,
                asset_id_1: row.get("asset_id_1")?,
                asset_id_2: row.get("asset_id_2")?,
                asset_id_3: row.get("asset_id_3")?,
                asset_id_4: row.get("asset_id_4")?,
                is_user_generated: row.get("is_user_generated")?,
                base_content_id: row.get("base_content_id")?,
                creator_user_handle: row.get("creator_user_handle")?,
            })
        })
        .map_err(|e| e.to_string())?;
    Ok(row)
}

pub fn query_upsert_content(db: &Connection, content: Content) -> Result<i64, String> {
    let data = serde_json::to_string(&content.data).map_err(|e| e.to_string())?;
    db
        .execute(
            "INSERT INTO content (id, name, content_type, content_subtype, data, asset_id_0, asset_id_1, asset_id_2, asset_id_3, asset_id_4) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10) ON CONFLICT(id) DO UPDATE SET name=excluded.name, content_type=excluded.content_type, content_subtype=excluded.content_subtype, data=excluded.data, asset_id_0=excluded.asset_id_0, asset_id_1=excluded.asset_id_1, asset_id_2=excluded.asset_id_2, asset_id_3=excluded.asset_id_3, asset_id_4=excluded.asset_id_4, updated_at=(unixepoch()) RETURNING id",
            (content.id, content.name, content.content_type, content.content_subtype, data, content.asset_id_0, content.asset_id_1, content.asset_id_2, content.asset_id_3, content.asset_id_4),
        )
        .map_err(|e| e.to_string())?;
    Ok(content.id)
}

pub fn query_contents_by_refs(db: &Connection, refs: Vec<Ref>) -> Result<Vec<Content>, String> {
    let mut ids: String = String::new();
    let mut names: String = String::new();
    for referance in refs {
        match referance {
            Ref::Id(id) => {
                ids.push_str(&id.to_string());
                ids.push_str(",");
            }
            Ref::Name(name) => {
                names.push_str(&name);
                names.push_str(",");
            }
        }
    }
    ids.pop();
    names.pop();
    let mut query = db
        .prepare("SELECT * FROM content WHERE id IN (?1) OR name IN (?2)")
        .map_err(|e| e.to_string())?;
    let mut content = Vec::new();
    let rows = query
        .query_map([ids, names], |row| {
            let data: String = row.get("data")?;
            Ok(Content {
                id: row.get("id")?,
                updated_at: row.get("updated_at")?,
                name: row.get("name")?,
                content_type: row.get("content_type")?,
                content_subtype: row.get("content_subtype")?,
                data: serde_json::from_str(&data)
                    .map_err(|_e| rusqlite::Error::ExecuteReturnedResults)?,
                asset_id_0: row.get("asset_id_0")?,
                asset_id_1: row.get("asset_id_1")?,
                asset_id_2: row.get("asset_id_2")?,
                asset_id_3: row.get("asset_id_3")?,
                asset_id_4: row.get("asset_id_4")?,
                is_user_generated: row.get("is_user_generated")?,
                base_content_id: row.get("base_content_id")?,
                creator_user_handle: row.get("creator_user_handle")?,
            })
        })
        .map_err(|e| e.to_string())?;
    for row in rows {
        content.push(row.map_err(|e| e.to_string())?);
    }
    Ok(content)
}
