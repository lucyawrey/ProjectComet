use crate::config::{CLIENT_GAME_ID, CLIENT_VERSION, DEFAULT_FONT};
use bevy::app::Plugin;
use bevy::prelude::*;

pub struct ChatPlugin;

impl Plugin for ChatPlugin {
    fn build(&self, app: &mut App) {
        app.insert_resource(ChatState::default());
        app.add_systems(Startup, setup);
        app.add_systems(Update, chat_text_update_system);
    }
}

#[derive(Resource, Default)]
pub struct ChatState {
    pub chat_text: String,
}

impl ChatState {
    pub fn print(&mut self, text: &str) {
        self.chat_text = format!("{}{}\n", self.chat_text, text);
    }
}

// A unit struct to help identify the color-changing Text component
#[derive(Component)]
struct ChatText;

fn setup(mut commands: Commands, asset_server: Res<AssetServer>, mut chat: ResMut<ChatState>) {
    chat.print(&format!(
        "client_game_id: {}, client_version: {}",
        CLIENT_GAME_ID, CLIENT_VERSION
    ));

    // Chat text with one section
    commands.spawn((
        // Accepts a `String` or any type that converts into a `String`, such as `&str`
        Text::default(),
        TextFont {
            // This font is loaded and will be used instead of the default font.
            font: asset_server.load(DEFAULT_FONT),
            font_size: 18.0,
            ..default()
        },
        // Set the style of the Node itself.
        Node {
            position_type: PositionType::Absolute,
            bottom: Val::Px(5.0),
            right: Val::Px(5.0),
            ..default()
        },
        ChatText,
    ));
}

fn chat_text_update_system(
    state: Res<ChatState>,
    mut text_query: Query<&mut Text, With<ChatText>>,
) {
    // Update the text content of the ChatText span.
    for mut text in &mut text_query {
        if text.0 != state.chat_text {
            **text = state.chat_text.clone();
        }
    }
}
