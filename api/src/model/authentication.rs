use super::tables::{AccessToken, User, UserRecoveryCode, UserSession};
use tonic::Status;

#[derive(Debug)]
pub enum AuthStatus {
    Authenticated(AuthType),
    Unauthenticated,
}

#[derive(Debug)]
pub enum AuthType {
    UserSession(User, UserSession),
    UserRecoveryCode(User, UserRecoveryCode),
    AccessToken(AccessToken),
}

impl AuthStatus {
    pub fn auth_or(self) -> Result<AuthType, Status> {
        match self {
            AuthStatus::Authenticated(auth_type) => Ok(auth_type),
            AuthStatus::Unauthenticated => Err(Status::unauthenticated("Not authorized.")),
        }
    }

    pub fn auth_session_or(self) -> Result<(User, UserSession), Status> {
        match self.auth_or()? {
            AuthType::UserSession(user, user_session) => Ok((user, user_session)),
            AuthType::UserRecoveryCode(_, _) => {
                return Err(Status::unauthenticated("Not a session."))
            }
            AuthType::AccessToken(_) => return Err(Status::unauthenticated("Not a session.")),
        }
    }

    pub fn auth_recovery_code_or(self) -> Result<(User, UserRecoveryCode), Status> {
        match self.auth_or()? {
            AuthType::UserSession(_, _) => {
                return Err(Status::unauthenticated("Not a recovery code."))
            }
            AuthType::UserRecoveryCode(user, recovery_code) => Ok((user, recovery_code)),
            AuthType::AccessToken(_) => {
                return Err(Status::unauthenticated("Not a recovery code."))
            }
        }
    }

    pub fn auth_access_token_or(self) -> Result<AccessToken, Status> {
        match self.auth_or()? {
            AuthType::UserSession(_, _) => {
                return Err(Status::unauthenticated("Not an access_token."))
            }
            AuthType::UserRecoveryCode(_, _) => {
                return Err(Status::unauthenticated("Not an access_token."))
            }
            AuthType::AccessToken(access_token) => Ok(access_token),
        }
    }
}
