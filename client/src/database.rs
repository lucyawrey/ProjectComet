use crate::config::DEFAULT_DB_HOST;
use crate::{config::DEFAULT_DB_NAME, database_bindings::*};
use bevy::prelude::*;
use spacetimedb_sdk::credentials::File;

pub struct DatabasePlugin;

impl Plugin for DatabasePlugin {
    fn build(&self, app: &mut App) {
        let conn = connect_to_db();
        app.insert_resource(Database::new(conn));
    }
}

#[derive(Resource)]
pub struct Database(pub DbConnection);

impl Database {
    fn new(conn: DbConnection) -> Database {
        Database(conn)
    }
}

fn connect_to_db() -> DbConnection {
    DbConnection::builder()
        // Register our `on_connect` callback, which will save our auth token.
        .on_connect(|_db, _id, token| {
            if let Err(e) = creds_store().save(token) {
                eprintln!("Failed to save credentials: {:?}", e);
            }
        })
        // Register our `on_connect_error` callback, which will print a message, then exit the process.
        .on_connect_error(|_error_ctx, error| {
            eprintln!("Connection error: {:?}", error);
            std::process::exit(1);
        })
        // Our `on_disconnect` callback, which will print a message, then exit the process.
        .on_disconnect(|_error_ctx, error| {
            if let Some(err) = error {
                eprintln!("Disconnected: {}", err);
                std::process::exit(1);
            } else {
                println!("Disconnected.");
                std::process::exit(0);
            }
        })
        // If the user has previously connected, we'll have saved a token in the `on_connect` callback.
        // In that case, we'll load it and pass it to `with_token`, so we can re-authenticate as the same `Identity`.
        .with_token(creds_store().load().expect("Error loading credentials"))
        // Set the database name we chose when we called `spacetime publish`.
        .with_module_name(DEFAULT_DB_NAME)
        // Set the URI of the SpacetimeDB host that's running our database.
        .with_uri(DEFAULT_DB_HOST)
        // Finalize configuration and connect!
        .build()
        .expect("Failed to connect")
}

fn creds_store() -> File {
    File::new("project_comet_credentials")
}
