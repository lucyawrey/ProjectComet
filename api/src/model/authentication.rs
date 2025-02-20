use super::tables::{AccessToken, User};

pub enum AuthStatus {
    Authenticated(AuthType),
    Unauthenticated,
}

pub enum AuthType {
    AccessToken(AccessToken),
    User(User),
}
