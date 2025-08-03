# Game Client

## Requirements
- Rust toolchain
- Python
- Web Browser with WebGPU support

## Installing Runtime Dependancies
### Most Linux Systems
```sh
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
rustup target add wasm32-unknown-unknown
cargo install -f wasm-bindgen-cli --version 0.2.100
```

## Build and run WASM target for development
```sh
cargo build --target wasm32-unknown-unknown
wasm-bindgen --no-typescript --target no-modules \
    --out-dir ./www \
    --out-name "wasm" \
    ./target/wasm32-unknown-unknown/debug/project_comet_client.wasm
python3 server.py
```

## Building WASM target for release
```sh
cargo build --release --target wasm32-unknown-unknown
wasm-bindgen --no-typescript --target web \
    --out-dir ../out/client \
    --out-name "wasm" \
    ./target/wasm32-unknown-unknown/release/project_comet_client.wasm
cp ./www/index.html ../out/client/index.html
cp ./www/worker.js ../out/client/worker.js
```

## To do
- [ ] Setup Client API access
- [ ] Create HTTP asset download endpoints in API
- [ ] Load content from API
- [ ] Load and cache assets from API
