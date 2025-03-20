use spacetimedb::{
    rand::{
        distributions::{Alphanumeric, DistString},
        RngCore,
    },
    ReducerContext,
};

pub fn validate_username(username: &str) -> Result<(), String> {
    let length = username.chars().count();
    if length < 4 {
        Err("Username too short".to_string())
    } else {
        Ok(())
    }
}

pub fn validate_message(text: &str) -> Result<(), String> {
    if text.is_empty() {
        Err("Message empty".to_string())
    } else {
        Ok(())
    }
}

pub fn get_random_name(ctx: &ReducerContext) -> String {
    Alphanumeric.sample_string(&mut ctx.rng(), 12)
}

pub fn get_random_id(ctx: &ReducerContext) -> u64 {
    ctx.rng().next_u64()
}
