use crate::chat::ChatState;
use crate::config::DEFAULT_DB_HOST;
use crate::{config::DEFAULT_DB_NAME, database_bindings::*};
use bevy::prelude::*;
use futures::channel::mpsc::{self, UnboundedReceiver, UnboundedSender};
use spacetimedb_sdk::credentials::File;
use spacetimedb_sdk::{DbContext, Table};

pub struct DatabasePlugin;

impl Plugin for DatabasePlugin {
    fn build(&self, app: &mut App) {
        let (sender, receiver) = mpsc::unbounded();
        // Connect to the database
        let conn = connect_to_db();
        // Subscribe to SQL queries in order to construct a local partial replica of the database.
        subscribe_to_tables(&conn);
        // Register callbacks
        register_callbacks(&conn, &sender);
        // Spawn a thread, where the connection will process messages and invoke callbacks.
        conn.run_threaded();

        app.insert_resource(Db::new(conn, receiver));
        app.add_systems(Update, process_messages);
    }
}

#[derive(Resource)]
pub struct Db {
    pub conn: DbConnection,
    pub receiver: UnboundedReceiver<DatabaseMessage>,
}

impl Db {
    fn new(conn: DbConnection, receiver: UnboundedReceiver<DatabaseMessage>) -> Db {
        Db { conn, receiver }
    }
}

pub enum DatabaseMessage {
    Message { text: String },
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
            "SELECT * FROM userr",
            "SELECT * FROM message",
            "SELECT * FROM character",
        ]);
}

fn register_callbacks(conn: &DbConnection, sender: &UnboundedSender<DatabaseMessage>) {
    conn.db
        .message()
        .on_insert(on_message_inserted(sender.clone()));
}

fn on_message_inserted(
    sender: UnboundedSender<DatabaseMessage>,
) -> impl FnMut(&EventContext, &Message) + Send + 'static {
    move |_ctx, row| {
        sender
            .unbounded_send(DatabaseMessage::Message {
                text: row.text.clone(),
            })
            .unwrap();
    }
}

pub fn process_messages(mut db: ResMut<Db>, mut chat: ResMut<ChatState>) {
    loop {
        let message = db.receiver.try_next();
        if let Ok(message) = message {
            if let Some(message) = message {
                match message {
                    DatabaseMessage::Message { text } => {
                        chat.print(&text);
                    }
                }
            }
        } else {
            break;
        }
    }
}
