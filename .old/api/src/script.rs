#![allow(dead_code)]
mod model;
mod queries;
mod utils;
use queries::data_import::data_import;
use sqlx::{migrate::MigrateDatabase, Sqlite, SqlitePool};
use std::{env, process};
use utils::{new_sonyflake, next_id};

#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    dotenvy::dotenv()?;
    let args: Vec<String> = env::args().collect();
    match args.get(1) {
        Some(s) => match s.as_str() {
            "migrate" => {
                migrate().await?;
            }
            "id_gen" => {
                let id_count: u32 = args.get(2).unwrap_or(&"1".to_owned()).parse().unwrap_or(1);
                id_gen(id_count).await?;
            }
            _ => {
                println!(
                    "Provided script name is not valid. Valid options are 'migrate' and 'id_gen'."
                );
                process::exit(1);
            }
        },
        None => {
            println!("Please provide a valid script name as the first argument. Valid options are 'migrate' and 'id_gen'.");
            process::exit(1);
        }
    };
    Ok(())
}

async fn migrate() -> Result<(), Box<dyn std::error::Error>> {
    let database_url =
        env::var("DATABASE_URL").expect("Environment variable DATABASE_URL not found");
    let client_database_output_url = env::var("CLIENT_DATABASE_OUTPUT_URL")
        .expect("Environment variable CLIENT_DATABASE_OUTPUT_URL not found");
    let api_migrator = sqlx::migrate!("./migrations");
    let client_migrator = sqlx::migrate!("./migrations/client");

    // API Database
    if !Sqlite::database_exists(&database_url)
        .await
        .unwrap_or(false)
    {
        println!("  Creating database {}.", &database_url);
        match Sqlite::create_database(&database_url).await {
            Ok(_) => println!("  Create database success."),
            Err(error) => panic!("  error: {}", error),
        }
    } else {
        println!("  Loading existing database.");
    }
    let db = SqlitePool::connect(&database_url).await?;

    println!("  Running database migrations.");
    api_migrator.run(&db).await?;

    println!("\n  Importing data from data files.\n");
    let version = data_import(&db, None).await.unwrap();
    println!(
        "  Updated database for game version: '{} {}'.",
        version.game_id, version.game_version
    );

    //Client Database
    if !Sqlite::database_exists(&client_database_output_url)
        .await
        .unwrap_or(false)
    {
        println!(
            "  Creating client database {}.",
            &client_database_output_url
        );
        Sqlite::create_database(&client_database_output_url)
            .await
            .expect("Failed to create client database")
    }
    let cdb = SqlitePool::connect(&client_database_output_url).await?;

    client_migrator.run(&cdb).await?;

    println!("\n  Importing data from data files to client database.\n");
    let cversion = data_import(&cdb, Some(&["asset", "content"]))
        .await
        .unwrap();
    println!(
        "  Updated client database for game version: '{} {}'.",
        cversion.game_id, cversion.game_version
    );

    Ok(())
}

async fn id_gen(id_count: u32) -> Result<(), Box<dyn std::error::Error>> {
    let machine_id_range =
        env::var("MACHINE_ID_RANGE").expect("Environment variable 'MACHINE_ID_RANGE' not found");
    let mut machine_ids = machine_id_range.split("..").map(|s| {
        s.parse::<u16>()
            .expect("'MACHINE_ID_RANGE' must be a pair of integers")
    });
    let sf = new_sonyflake(&mut machine_ids).unwrap();

    println!("id,timestamp,machine_id");
    for _ in 0..id_count {
        let line = match next_id(&sf) {
            Ok((id, time, machine_id)) => {
                format!("{},{},{}", id, time, machine_id)
            }
            Err(_) => "error failed to generate id,,".to_owned(),
        };
        println!("{}", line);
    }
    Ok(())
}
