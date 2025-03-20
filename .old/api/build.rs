use std::{env, fs, path::PathBuf};

fn main() -> Result<(), Box<dyn std::error::Error>> {
    for entry in fs::read_dir("./migrations/client")? {
        let path = entry?.path();
        fs::copy(
            &path,
            "./migrations/".to_string() + &path.file_name().unwrap().to_str().unwrap(),
        )
        .unwrap();
    }

    let out_dir = PathBuf::from(env::var("OUT_DIR").unwrap());
    tonic_build::configure()
        .file_descriptor_set_path(out_dir.join("api_descriptor.bin"))
        .compile_protos(&["proto/api.proto"], &["proto"])?;
    Ok(())
}
