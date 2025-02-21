use crate::database::GameState;
use bevy::app::Plugin;
use bevy::{
    color::palettes::css::GOLD,
    diagnostic::{DiagnosticsStore, FrameTimeDiagnosticsPlugin},
    prelude::*,
};

pub struct DebugPlugin;

impl Plugin for DebugPlugin {
    fn build(&self, app: &mut App) {
        app.add_systems(Startup, setup)
            .add_systems(Update, (fps_text_update_system, debug_text_update_system));
    }
}

// A unit struct to help identify the FPS UI component, since there may be many Text components
#[derive(Component)]
struct FpsText;

// A unit struct to help identify the color-changing Text component
#[derive(Component)]
struct DebugText;

fn setup(mut commands: Commands, asset_server: Res<AssetServer>) {
    // UI camera
    commands.spawn(Camera2d);
    // Debug text with one section
    commands.spawn((
        // Accepts a `String` or any type that converts into a `String`, such as `&str`
        Text::default(),
        TextFont {
            // This font is loaded and will be used instead of the default font.
            font: asset_server.load("FiraMono-Bold.ttf"),
            font_size: 50.0,
            ..default()
        },
        // Set the justification of the Text
        TextLayout::new_with_justify(JustifyText::Center),
        // Set the style of the Node itself.
        Node {
            position_type: PositionType::Absolute,
            bottom: Val::Px(5.0),
            right: Val::Px(5.0),
            ..default()
        },
        DebugText,
    ));
    // FPS text with multiple sections
    commands
        .spawn((
            Text::new("FPS: "),
            TextFont {
                font: asset_server.load("FiraMono-Bold.ttf"),
                font_size: 42.0,
                ..default()
            },
        ))
        .with_child((
            TextSpan::default(),
            (
                TextFont {
                    font: asset_server.load("FiraMono-Medium.ttf"),
                    font_size: 33.0,
                    ..Default::default()
                },
                TextColor(GOLD.into()),
            ),
            FpsText,
        ));

    commands.spawn((
        TextFont {
            font: asset_server.load("FiraMono-Medium.ttf"),
            ..default()
        },
        Node {
            position_type: PositionType::Absolute,
            bottom: Val::Px(5.0),
            left: Val::Px(15.0),
            ..default()
        },
    ));
}

fn debug_text_update_system(
    time: Res<Time>,
    state: Res<GameState>,
    mut color_query: Query<&mut TextColor, With<DebugText>>,
    mut text_query: Query<&mut Text, With<DebugText>>,
) {
    for mut color in &mut color_query {
        let seconds = time.elapsed_secs();
        // Update the color of the ColorText span.
        color.0 = Color::srgb(
            ops::sin(1.25 * seconds) / 2.0 + 0.5,
            ops::sin(0.75 * seconds) / 2.0 + 0.5,
            ops::sin(0.50 * seconds) / 2.0 + 0.5,
        );
    }

    for mut text in &mut text_query {
        if text.0 != state.debug_text {
            **text = state.debug_text.clone();
        }
    }
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
