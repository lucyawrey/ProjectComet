# Project Comet, an experimental MMORPG
This branch of Project Comet is experimenting with a SpacetimeDB backend.

## Projects
- Client - Bevy game client. Interacts with both the game Sever and the API project over RPC. Uses a local `SQLite` DB to cache data from the API.
- Server - Spacetime DB module and associated scripts
- Data - Collection of game data (.toml documents) and assets stored in this repo. Both are consumed by the API project and imported into an SQLite database.

## To do
- [ ] Create SpacetimeDB module and server script
- [ ] Modify bevy game to depend on SpacetimeDB server
- [ ] Setup cargo workspace
