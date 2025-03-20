# Game Client

## Requirements
- Rust toolchain
- Python

## Installing Requirements (Linux)
```sh
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
sudo apt update && sudo apt install python3
```

## Installing WASM Target Requirements
```sh
rustup target add wasm32-unknown-unknown
cargo install -f wasm-bindgen-cli --version 0.2.100
```

## Running the Client
```sh
cargo run
```

## Building WASM Target for Development
```sh
cargo build --target wasm32-unknown-unknown
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
cargo build --release --target wasm32-unknown-unknown
wasm-bindgen --no-typescript --target web \
    --out-dir ../out/client \
    --out-name "wasm" \
    ./target/wasm32-unknown-unknown/release/project_comet_client.wasm
cp ./www/index.html ../out/client/index.html
```
