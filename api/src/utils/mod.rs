pub mod authentication;
pub mod transport;

use crate::model::fields::AssetData;
use chrono::{DateTime, NaiveDateTime, Utc};
use num::{FromPrimitive, Integer, ToPrimitive};
use rand::distr::{Alphanumeric, SampleString};
use sonyflake::{decompose, Builder, Sonyflake};
use std::{
    fs::{self, DirEntry, OpenOptions},
    io::{self, prelude::*},
    ops::Range,
    path::Path,
};

/// Macro for initializing a regex struct only once and reusing a referance to it on future calls using the standard library's `OnceLock``.
macro_rules! regex {
    ($re:literal $(,)?) => {{
        static RE: std::sync::OnceLock<regex::Regex> = std::sync::OnceLock::new();
        RE.get_or_init(|| regex::Regex::new($re).unwrap())
    }};
}

pub fn decompose_id<T: Integer + FromPrimitive + ToPrimitive + Copy>(id: T) -> (i64, i64, u16) {
    let decomposed = decompose(id.to_u64().unwrap_or(0));
    (
        decomposed.id as i64,
        (decomposed.time / 100) as i64,
        decomposed.machine_id as u16,
    )
}

pub fn next_id(sf: &Sonyflake) -> Result<(i64, i64, u16), String> {
    match sf.next_id() {
        Ok(id) => Ok(decompose_id(id)),
        Err(e) => Err(e.to_string()),
    }
}

pub fn new_sonyflake<T: Iterator<Item = u16>>(
    machine_ids: &mut T,
) -> Result<Sonyflake, Box<dyn std::error::Error>> {
    let machine_id = machine_ids
        .next()
        .ok_or("Not enough machine IDs in provoded range.")?;
    Ok(Builder::new()
        .start_time(DateTime::UNIX_EPOCH)
        .machine_id(&|| Ok(machine_id))
        .finalize()?)
}

pub fn validate_and_format_name(name: String) -> Option<String> {
    let bad_char_regex = regex!("[\t\n\r_]");
    let formatted = name.trim();
    let length = formatted.len();

    if length < 2 || length > 30 || bad_char_regex.is_match(formatted) {
        return None;
    }
    let mut space_count = 0;
    let mut last_char_is_space = false;
    for c in formatted.chars() {
        if c == ' ' {
            if space_count == 3 || last_char_is_space == true {
                return None;
            }
            space_count = space_count + 1;
            last_char_is_space = true;
        } else {
            last_char_is_space = false;
        }
    }
    Some(formatted.to_owned())
}

pub fn parse_range(range: String) -> Option<Range<u16>> {
    let mut split = range.splitn(2, "..");
    let first = split.next()?.parse::<u16>().ok()?;
    let second = split.next()?.parse::<u16>().ok()?;
    Some(first..second)
}

pub fn generate_random_name() -> String {
    Alphanumeric.sample_string(&mut rand::rng(), 13)
}

/// Gets integer unix timestamp in seconds.
pub fn current_timestamp() -> i64 {
    Utc::now().timestamp()
}

pub fn current_date_time() -> NaiveDateTime {
    Utc::now().naive_utc()
}

pub fn read_dir_recursive(dir: &str) -> Result<Vec<DirEntry>, io::Error> {
    let mut files: Vec<DirEntry> = Vec::new();
    read_dir(Path::new(dir), &mut files)?;
    Ok(files)
}

fn read_dir(path: &Path, files: &mut Vec<DirEntry>) -> io::Result<()> {
    if path.is_dir() {
        for entry in fs::read_dir(path)? {
            let entry = entry?;
            let path = entry.path();
            if path.is_dir() {
                read_dir(&path, files)?;
            } else {
                files.push(entry);
            }
        }
    }
    Ok(())
}

pub fn append_secret_to_file(new_line: String) {
    let mut file = match OpenOptions::new().append(true).create(true).open("secrets") {
        Ok(f) => f,
        Err(e) => {
            eprintln!("Couldn't write to file: {}. {}", new_line, e);
            return ();
        }
    };
    if let Err(e) = writeln!(file, "{}", new_line) {
        eprintln!("Couldn't write to file: {}. {}", new_line, e);
    }
}

pub fn get_magic_cookie() -> magic::Cookie<magic::cookie::Load> {
    let cookie = magic::Cookie::open(magic::cookie::Flags::MIME_TYPE).unwrap();
    let database = Default::default();
    cookie.load(&database).unwrap()
}

pub fn read_asset_file(
    path: &str,
    magic_cookie: &magic::Cookie<magic::cookie::Load>,
) -> Result<(AssetData, i64, String), io::Error> {
    let path = Path::new(path);
    let data: Vec<u8> = fs::read(path)?;
    let file_type = magic_cookie
        .buffer(&data)
        .map_err(|e| io::Error::new(io::ErrorKind::InvalidInput, e))?;
    let size: i64 = data
        .len()
        .try_into()
        .expect("Cannot read file. Too large for 32 bit systems");
    if file_type.starts_with("text") || file_type.ends_with("json") {
        Ok((
            AssetData::String(
                String::from_utf8(data)
                    .map_err(|e| io::Error::new(io::ErrorKind::InvalidInput, e))?,
            ),
            size,
            file_type,
        ))
    } else {
        Ok((AssetData::Blob(data), size, file_type))
    }
}
