# Game Server

## Requirements
- Rust toolchain
- SpacetimeDB

## Installing Requirements (Linux)
```sh
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
curl -sSf https://install.spacetimedb.com | sh
```

## Running SpacetimeDB Module Locally
```sh
spacetime start
spacetime publish --project-path . project-comet-server
```

## Generate Client Bindings
```sh
spacetime generate --lang rust --out-dir ../client/src/server_bindings --project-path .
```
