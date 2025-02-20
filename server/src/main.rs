mod api;
use api::get_api_client;
use api::CreateCharacterRequest;
use api::Role;
use std::env;

#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    let mut client = get_api_client().await?;

    let args: Vec<String> = env::args().collect();
    let character_name = args.get(1).cloned();
    let home_world_id_default = "clockwork".to_string();
    let home_world_id = args.get(2).cloned().unwrap_or(home_world_id_default);

    let request = tonic::Request::new(CreateCharacterRequest {
        home_world_id,
        role: Some(Role::MembershipPlayer.into()),
        name: character_name,
    });
    let response = client.create_character(request).await?;
    println!("RESPONSE={:?}\n", response);

    Ok(())
}
