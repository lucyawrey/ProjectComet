pub mod authentication;
pub mod fields;
pub mod fields_impl;
pub mod tables;
use sqlx::FromRow;

#[derive(Debug)]
pub enum Ref {
    Id(i64),
    Name(String),
}

#[derive(Debug, FromRow)]
pub struct IdWrapper {
    pub id: i64,
}
