use crate::config::DEFAULT_FONT;
use bevy::app::Plugin;
use bevy::{
    color::palettes::css::GOLD,
    diagnostic::{DiagnosticsStore, FrameTimeDiagnosticsPlugin},
    prelude::*,
};

pub struct FpsPlugin;

impl Plugin for FpsPlugin {
    fn build(&self, app: &mut App) {
        app.add_systems(Startup, setup);
        app.add_systems(Update, fps_text_update_system);
    }
}

// A unit struct to help identify the FPS UI component, since there may be many Text components
#[derive(Component)]
struct FpsText;

fn setup(mut commands: Commands, asset_server: Res<AssetServer>) {
    // FPS text with multiple sections
    commands
        .spawn((
            Text::new("FPS: "),
            TextFont {
                font: asset_server.load(DEFAULT_FONT),
                font_size: 30.0,
                ..default()
            },
        ))
        .with_child((
            TextSpan::default(),
            (
                TextFont {
                    font: asset_server.load(DEFAULT_FONT),
                    font_size: 30.0,
                    ..Default::default()
                },
                TextColor(GOLD.into()),
            ),
            FpsText,
        ));
}

fn fps_text_update_system(
    diagnostics: Res<DiagnosticsStore>,
    mut query: Query<&mut TextSpan, With<FpsText>>,
) {
    for mut span in &mut query {
        if let Some(fps) = diagnostics.get(&FrameTimeDiagnosticsPlugin::FPS) {
            if let Some(value) = fps.smoothed() {
                // Update the value of the second section
                **span = format!("{value:.2}");
            }
        }
    }
}
