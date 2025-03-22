use crate::config::DEFAULT_DB_HOST;
use crate::{config::DEFAULT_DB_NAME, database_bindings::*};
use bevy::prelude::*;
use spacetimedb_sdk::credentials::File;
use spacetimedb_sdk::DbContext;

pub struct DatabasePlugin;

impl Plugin for DatabasePlugin {
    fn build(&self, app: &mut App) {
        // Connect to the database
        let conn = connect_to_db();
        // Subscribe to SQL queries in order to construct a local partial replica of the database.
        subscribe_to_tables(&conn);
        // Spawn a thread, where the connection will process messages and invoke callbacks.
        conn.run_threaded();

        app.insert_resource(DatabaseConnection::new(conn));
    }
}

#[derive(Resource)]
pub struct DatabaseConnection(pub DbConnection);

impl DatabaseConnection {
    fn new(conn: DbConnection) -> DatabaseConnection {
        DatabaseConnection(conn)
    }
}

fn connect_to_db() -> DbConnection {
    DbConnection::builder()
        // Register our `on_connect` callback, which will save our auth token.
        .on_connect(|_conn, _id, token| {
            if let Err(e) = creds_store().save(token) {
                eprintln!("Failed to save credentials: {:?}", e);
            }
        })
        // Register our `on_connect_error` callback, which will print a message, then exit the process.
        .on_connect_error(|_ctx, err| {
            eprintln!("Connection error: {:?}", err);
            std::process::exit(1);
        })
        // Our `on_disconnect` callback, which will print a message, then exit the process.
        .on_disconnect(|_ctx, err| {
            if let Some(err) = err {
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

fn subscribe_to_tables(conn: &DbConnection) {
    conn.subscription_builder()
        .on_applied(|_ctx| {})
        .on_error(|_ctx, _err| {})
        .subscribe([
            "SELECT * FROM user",
            "SELECT * FROM message",
            "SELECT * FROM character",
        ]);
}
