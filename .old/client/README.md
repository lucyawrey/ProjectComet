# Game Client

## Requirements
- Rust toolchain
- Python

## Installing Runtime Dependancies
### Most Linux Systems
```sh
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
rustup default nightly
rustup component add rust-src --toolchain nightly-x86_64-unknown-linux-gnu
```

## Running the Client
```sh
cargo run
```

## Building WASM Target for Development
```sh
RUSTFLAGS='-C target-feature=+atomics,+bulk-memory,+mutable-globals' cargo build --target wasm32-unknown-unknown -Z build-std=panic_abort,std
wasm-bindgen --no-typescript --target no-modules \
    --out-dir ./www \
    --out-name "wasm" \
    ./target/wasm32-unknown-unknown/debug/project_comet_client.wasm
python3 server.py
```

## Building
```sh
cargo build --release
mkdir -p ../out/client && cp ./target/release/project_comet_client "$_"
```

## Building WASM Target
```sh
RUSTFLAGS='-C target-feature=+atomics,+bulk-memory,+mutable-globals' cargo build --release --target wasm32-unknown-unknown -Z build-std=panic_abort,std
wasm-bindgen --no-typescript --target web \
    --out-dir ../out/client \
    --out-name "wasm" \
    ./target/wasm32-unknown-unknown/release/project_comet_client.wasm
cp ./www/index.html ../out/client/index.html
cp ./www/worker.js ../out/client/worker.js
```

## Installing WASM Target Runtime Dependancies
### Most Linux Systems
```sh
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
rustup target add wasm32-unknown-unknown
cargo install -f wasm-bindgen-cli --version 0.2.100
```

## To do
- [ ] Create database API on top of Native SQLite and WASM Worker SQLite. Download `client_data.sqlite` on Native if it does not exist instead of creating it.
- [ ] Setup Client API access
- [ ] Load content + assets from SQLite database
- [ ] Sync content + assets from API database
