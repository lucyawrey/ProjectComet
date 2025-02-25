# Game API Service

## Requirements
- Rust toolchain
- libssl
- protobuf compiler
- libmagic

## Running Migrations
```sh
cargo run --bin script migrate
```

## Running the API
```sh
cargo run --bin script migrate
cargo run
```
## Building
```sh
cargo run --bin script migrate
cargo build --release
mkdir -p ../out/api && cp ./target/release/project_comet_api "$_"
mkdir -p ../out/api && cp ./target/release/script "$_"
mkdir -p ../out/api && cp ./.env "$_"
mkdir -p ../out/api && cp ./secrets "$_"
mkdir -p ../out/api && cp ./game_data.sqlite "$_"
mkdir -p ../out/data/content && cp ../data/content/game.toml "$_"
```

## Installing Runtime Dependancies
### Most Linux Systems
```sh
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
sudo apt-get install libssl-dev
sudo apt-get install protobuf-compiler
sudo apt-get install libmagic1 libmagic-dev
```

## Setting Up gRPC UI
```sh
sudo apt  install golang-go

go run github.com/fullstorydev/grpcui/cmd/grpcui@latest -plaintext 127.0.0.1:50051
```

## To do Goals
### Current Phase
- [ ] Implement business logic, queries, and API endpoints for `user`s, `access_tokens`s, and general authetication.
- [ ] Potentially migrate from SQLx to rusqlite.
- [ ] Move database migrations into main API server startup.
- [ ] Fix standalone release build breakage, likey results from writing to the SQLite database.
- [ ] Fix dockerfile build.
- [ ] Support serialization of SQLx Json convenience types.
- [ ] Implement support for enum string names in TOML data files.
- [ ] Build out remaining API data model for database and Rust--Classes, Quest Progress, Character Status(Current Zone, Position, HP, Status Effects), etc.
- [ ] Implement business logic, queries, and API endpoints for `characters`s, `items`s, and character supporting data.
- [ ] Add Rust defaults for datbase tables and fields.
- [ ] Add more CHECK constraints and TRIGGERs to SQLite database to ensure data resiliency.
- [ ] More consistant Rust error structure from individual functions to API responses.
- [ ] Proper server-wide dependancy injection (for things like Database and ID Generator).
- [ ] Add a waiting period for User and Character deletion.
- [ ] Implement a way to return Soulbound items to binder after a timer on the API side.
- [ ] Refactor TOML data importers to only update changed data.
- [ ] Refactor JSON `FromRow` struct fields to support optional string passthrough without serialization.
