use crate::model::tables::GameInfo;
use sqlx::{query_as, Pool, Sqlite};

pub async fn get_game_info_query(db: &Pool<Sqlite>) -> Option<GameInfo> {
    let game_info = query_as::<_, GameInfo>("SELECT * FROM game_info WHERE id = 0")
        .fetch_one(db)
        .await
        .ok()?;
    Some(game_info)
}

pub async fn upsert_game_info_query(_db: &Pool<Sqlite>) -> Result<GameInfo, String> {
    todo!()
}
