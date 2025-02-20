pub mod authentication;
pub mod fields;
pub mod fields_impl;
pub mod tables;

pub enum Ref {
    Id(i64),
    Name(String),
}
