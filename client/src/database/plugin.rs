use super::{ClientDatabase, Content, DatabaseResult, GameInfo};
use bevy::{prelude::*, utils::HashMap};

pub struct DatabasePlugin;

impl Plugin for DatabasePlugin {
    fn build(&self, app: &mut App) {
        app.insert_resource(Data::default());
        app.add_systems(Startup, (setup, fetch_data).chain());
        app.add_systems(Update, process_messages);
    }
}

#[derive(Resource, Default)]
pub struct Data {
    pub game_info: Option<GameInfo>,
    pub content: HashMap<i64, Content>,
}

pub fn setup(world: &mut World) {
    let database = ClientDatabase::new().expect("Failed to initialize client database.");
    world.insert_non_send_resource(database);
}

pub fn fetch_data(db: NonSend<ClientDatabase>) {
    db.query_content();
}

pub fn process_messages(db: NonSend<ClientDatabase>, mut data: ResMut<Data>) {
    loop {
        match db.receiver.try_recv() {
            Ok(result) => match result {
                DatabaseResult::GameInfo(game_info) => data.game_info = Some(game_info),
                DatabaseResult::Content(contents) => {
                    for content in contents {
                        data.content.insert(content.id, content);
                    }
                }
            },
            Err(_) => break,
        }
    }
}
