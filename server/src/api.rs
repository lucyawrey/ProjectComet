tonic::include_proto!("api");

use crate::api::users_client::UsersClient;
use std::env;
use tonic::{
    metadata::MetadataValue, service::interceptor::InterceptedService, transport::Channel, Request,
    Status,
};

pub async fn get_api_client(
) -> UsersClient<InterceptedService<Channel, impl Fn(Request<()>) -> Result<Request<()>, Status>>> {
    let api_address =
        env::var("API_ADDRESS").expect("Environment variable 'API_ADDRESS' not found.");
    let authorization_token = env::var("AUTHORIZATION_TOKEN")
        .expect("Environment variable 'AUTHORIZATION_TOKEN' not found.");

    let channel = Channel::from_shared(format!("http://{}", api_address))
        .expect("Unable to parse API address.")
        .connect()
        .await
        .expect("Unable connect to API.");
    let meta: MetadataValue<_> = authorization_token
        .parse()
        .expect("Unable to parse auth token.");
    let client = UsersClient::with_interceptor(channel, move |mut req: Request<()>| {
        req.metadata_mut().insert("authorization", meta.clone());
        Ok(req)
    });
    client
}
