#![allow(dead_code)]
mod api;
mod model;
mod queries;
mod services;
mod utils;
use api::{game_data_server::GameDataServer, users_server::UsersServer};
use queries::game_info::get_game_info_query;
use services::game_data::GameDataService;
use services::users::UsersService;
use sqlx::SqlitePool;
use std::{env, net::SocketAddr};
use tonic::{transport::Server, Request, Status};
use utils::{new_sonyflake, parse_range};

#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    dotenvy::dotenv()?;

    let address = env::var("ADDRESS").expect("Environment variable 'ADDRESS' not found");
    let database_url =
        env::var("DATABASE_URL").expect("Environment variable 'DATABASE_URL' not found");
    let machine_id_range =
        env::var("MACHINE_ID_RANGE").expect("Environment variable 'MACHINE_ID_RANGE' not found");
    let mut machine_ids =
        parse_range(machine_id_range).expect("'MACHINE_ID_RANGE' must be a pair of integers");

    let db0 = SqlitePool::connect(&database_url)
        .await
        .expect("Could not load SQLite database");
    let db1 = SqlitePool::connect(&database_url)
        .await
        .expect("Could not load SQLite database");

    let game_info = get_game_info_query(&db0)
        .await
        .expect("No game_info in database");
    println!(
        "  Loaded database for game version: '{} {}'.",
        game_info.game_id, game_info.game_version
    );

    let game_data_service = GameDataService::new(db0, new_sonyflake(&mut machine_ids).unwrap());
    let users_service = UsersService::new(db1, new_sonyflake(&mut machine_ids).unwrap());
    let reflection_service = tonic_reflection::server::Builder::configure()
        .register_encoded_file_descriptor_set(api::FILE_DESCRIPTOR_SET)
        .build_v1()
        .unwrap();

    let addr: SocketAddr = address.parse().expect("Unable to parse socket address");
    println!(
        "  ☄️ Starting Project Comet Game Data API Service on {}\n",
        addr
    );
    Server::builder()
        .add_service(reflection_service)
        .add_service(GameDataServer::with_interceptor(game_data_service, echo))
        .add_service(UsersServer::with_interceptor(users_service, echo))
        .serve(addr)
        .await?;

    Ok(())
}

fn echo(req: Request<()>) -> Result<Request<()>, Status> {
    println!("Request: {:?}\n", req);
    Ok(req)
}
