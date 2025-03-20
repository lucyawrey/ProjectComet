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
```
spacetime start
spacetime publish --project-path .
```
