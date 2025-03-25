use super::ClientDatabase;
use crate::chat::ChatState;
use bevy::prelude::*;

pub struct DatabasePlugin;

impl Plugin for DatabasePlugin {
    fn build(&self, app: &mut App) {
        app.add_systems(Startup, (setup, debug).chain());
    }
}

pub fn setup(world: &mut World) {
    let database = ClientDatabase::new().expect("Failed to initialize client database.");
    world.insert_non_send_resource(database);
}

pub fn debug(db: NonSend<ClientDatabase>, mut chat: ResMut<ChatState>) {
    chat.print("Testing database. Content table name rows:");
    let names = db.query_content_names();
    chat.print(&names);
}
