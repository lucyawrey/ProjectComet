# Operations

Running a live MMO well matters as much as building it. Comet includes the tools a small team needs to deploy, moderate and protect a game, and private servers get all of it.

## Deploys

Updates happen in **maintenance windows**: a region goes down, the database is migrated, everything is deployed and restarted together. Processes and clients never run mixed versions, which keeps handoff and the protocol simple. Web players pick up a new build when they next log in. Rolling updates are never planned for; they're much harder for a small team.

## Moderation and admin tools

- **Who moderates:** the dev team, volunteer moderators with limited powers, and private-server owners as admins of their own servers.
- **Where:** a web admin panel inside the Login server (reports, chat review, sanctions, character and item lookups, restores), plus in-game GM powers for things that must happen in the world (invisibility, teleporting to a player, freeing a stuck character). Staff use separate staff accounts; GM characters are always clearly marked.
- **Reports** are a short form. The server attaches the evidence: who, when and where, recent chat the reporter could see, the reported player's automatic flags, and recent trades between the two.
- **Sanctions** follow a ladder: warning, timed mute, timed trade restriction, timed suspension, permanent ban. They apply to the whole account, show their reason and expiry to the player, and can be appealed once, to a different staff member.
- **Rollbacks:** normally targeted restores of specific items, traced through the ledger. A region-wide restore from backups is an admin-only last resort.

### Guarding against moderator abuse

Volunteers can only warn and mute (up to 48 hours), only see chat attached to reports, and never see emails or IP addresses. Nobody can act on their own account, friends or guildmates. Every action is logged, staff review volunteer actions, and the biggest actions can require two people.

## Chat privacy

There is **no general chat log**. All chat passes through a rolling buffer of up to 24 hours. A report saves the relevant lines; when the report is resolved, they're deleted unless kept as evidence. Staff searches of the buffer are logged.

## The ledger

Every creation, destruction and change of owner of an item or currency is written to an append-only ledger in the same transaction as the change itself, so the two can never disagree. It powers moderation, precise fixes after bugs, economy balancing and player-facing history. Detailed rows are kept for about six months, then archived; summaries are kept for good.

## Privacy

- **Minimal data:** email and login details, IP addresses for security (kept briefly), an age bracket rather than a birthdate, no real names. Analytics are first-party only, with no ad or tracking SDKs.
- **Deleting an account** takes effect after about 30 days (cancellable). Personal data is deleted; ledger and moderation records keep only an anonymous ID.
- **Guest characters** are deleted after about 30 days without play, and guests are told so.
- Retention jobs and deletion tools are on by default, for private servers too.

## Security

- **Logins:** email and password, plus passkeys. No third-party login providers, so everything is self-hostable.
- **Two-factor authentication** is optional and encouraged for players, and required for staff.
- **Anti-cheat is server authority.** No client-side anti-cheat: the web client can't run one, and the client source is open anyway. The server validates movement, hits, cooldowns and items.
- **Bots and automation** are found by server-side heuristics (play patterns, inhuman timing, suspicious ledger flows) that flag accounts for human review. Nothing is banned automatically.

## Monitoring

Servers and bots report the same metrics through OpenTelemetry. Simple dashboards cover load tests; Prometheus and Grafana come with real deployments.

## Open questions

- Legal review of privacy, retention and volunteer moderation before launch.
- How desktop builds are distributed (own patcher or a storefront).
