use super::CrossPlatformDatabase;
use crate::chat::ChatState;
use bevy::prelude::*;

pub struct DatabasePlugin;

impl Plugin for DatabasePlugin {
    fn build(&self, app: &mut App) {
        let cross_platform_db =
            CrossPlatformDatabase::new().expect("Failed to initialize client database.");
        app.insert_resource(Database(cross_platform_db));
        app.add_systems(Startup, setup);
    }
}

#[derive(Resource)]
pub struct Database(pub CrossPlatformDatabase);

pub fn setup(db: Res<Database>, mut chat: ResMut<ChatState>) {
    chat.print("Testing database. Content table name rows:");
    let names = db.0.query_content_names();
    chat.print(&names);
}
