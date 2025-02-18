mod api;
use api::create_character_request::UserRef;
use api::get_api_client;
use api::CreateCharacterRequest;
use api::LogInRequest;
use api::Role;
use std::env;

#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    let mut client = get_api_client().await?;

    let args: Vec<String> = env::args().collect();
    let character_name = args.get(1).cloned();
    let home_world_id_default = "clockwork".to_string();
    let username_default = "CometAdmin".to_string();
    let home_world_id = args.get(2).cloned().unwrap_or(home_world_id_default);
    let username = args.get(3).cloned().unwrap_or(username_default);

    let request = tonic::Request::new(CreateCharacterRequest {
        home_world_id,
        role: Some(Role::MembershipPlayer.into()),
        name: character_name,
        user_ref: Some(UserRef::Username(username)),
    });
    let response = client.create_character(request).await?;
    println!("RESPONSE={:?}\n", response);

    Ok(())
}
