tonic::include_proto!("api");

use crate::api::users_client::UsersClient;
use tonic::transport::Channel;

pub async fn get_api_client() -> Result<UsersClient<Channel>, tonic::transport::Error> {
    UsersClient::connect("http://127.0.0.1:50051").await
}
