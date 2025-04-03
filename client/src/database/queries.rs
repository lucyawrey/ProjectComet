use super::{Content, GameInfo, Ref};
use rusqlite::Connection;

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

pub fn query_contents(db: &Connection) -> Result<Vec<Content>, String> {
    let mut query = db
        .prepare("SELECT * FROM content")
        .map_err(|e| e.to_string())?;
    let mut content = Vec::new();
    let rows = query
        .query_map([], |row| {
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
