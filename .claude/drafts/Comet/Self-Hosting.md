# Self-Hosting

Anyone should be able to run their own server easily, without a large proprietary dependency. This is a commitment rather than a pillar: it shapes the technology and the business model more than how the game feels.

## What a private server runs

- The same Login, Region and game server code as the official servers.
- One PostgreSQL database, with the account and region data as two schemas.
- A static host for the web client build and downloads (the website itself is served by the Login server).

One region per private server is the expected setup; a small region needs no extra infrastructure such as Valkey.

Hosts get the admin panel, moderation tools, ledger and retention jobs automatically. They're responsible for their own players' data, and Comet's privacy-friendly defaults are switched on.

## Licences

| Part | Licence |
| --- | --- |
| Comet and ShapeLand | MIT |
| Project Anima's code | Source-available, non-commercial (exact licence chosen with legal advice before release) |
| Project Anima's content and art | Usable on private servers non-commercially |

## Hosting Project Anima

Private servers may run Project Anima with its official content, art and client, as long as they:

- don't charge players or sell items;
- don't present themselves as official;
- host only the free parts of the game (in the initial version).

Private-server packages ship only the free parts' content, and the licence forbids hosting the rest. Membership and bonds don't exist on private servers.

## Open questions

- Packaging. One idea under consideration: all three servers in one process, shipped as a single docker-compose file with PostgreSQL and a reverse proxy that handles TLS automatically.
- The exact licence text for Project Anima.
- Whether private servers can ever host member content.
