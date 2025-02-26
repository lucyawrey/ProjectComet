# Project Comet, an experimental MMORPG
## Projects
- Client - Bevy game client. Interacts with both the game Sever and the API project over RPC. Uses a local `SQLite` DB to cache data from the API.
- Server - Authorititative game server that connects with the Bevy game client and enchanges messages + syncs ECS state. Uses `bevy_replicon` over `bevy_renet2` for UDP on native and WebTransport in browsers. Interacts with API project over the RPC.
- API - Web server for handling data using gRPC (`Tonic`), `SQLX` and `SQLite`. Will support huge amounts of player data. Examples: essentially infinite 'item box' capable of holding at least one of every equipment, collectible and item in the game (each update will add more); player progress on every quest in the game; player progress on classes and professions including skill trees and cross class skills; character customization data including base character creation data as well as equipped gear; premade game assets saved to the DB as a blob; and player generated assets saved to the DB as a blob (with strict size limits).
- Data - Collection of game data (.toml documents) and assets stored in this repo. Both are consumed by the API project and imported into an SQLite database.

## To do
- [ ] Move library code to `lib.rs`.
- [ ] Setup cargo workspace.
- [ ] Create web server project, run scripts and build scripts.
- [ ] Containerize aplication services: `api`, `server`, `client` (WASM builder + Native buolder), and `grpcui`.
- [ ] Move more information to main README.
- [ ] Cache `access_token` authentication based on SSL certificate or lochost/VPN port.
- [ ] Statically link non-cargo dependancies. 
