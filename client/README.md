# Game Client

## Requirements
- Rust tooolshain

## Running the Client
```sh
cargo run
```

## Running the Client for a WASM Target in the Browser
```sh
CARGO_TARGET_WASM32_UNKNOWN_UNKNOWN_RUNNER=wasm-server-runner cargo run --target wasm32-unknown-unknown
```

## Building
```sh
cargo build --release
mkdir -p ../out/client && cp ./target/release/project_comet_client "$_"
```

## Building WASM Target
```sh
cargo build --release --target wasm32-unknown-unknown
wasm-bindgen --no-typescript --target web \
    --out-dir ../out/client/ \
    --out-name "project_comet_client" \
    ./target/wasm32-unknown-unknown/release/project_comet_client.wasm
cp ./www/index.html ../out/client/index.html
```

## Installing WASM Target Runtime Dependancies
### Most Linux Systems
```sh
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
rustup target add wasm32-unknown-unknown
cargo install wasm-server-runner
cargo install -f wasm-bindgen-cli --version 0.2.100
```
