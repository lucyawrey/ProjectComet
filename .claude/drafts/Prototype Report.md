# Prototype Report

<!-- Agent draft of the prototype report, written up from `Prototype Results.md` (the raw numbers and the tuning history) and `.claude/design/prototype.md`. Still to gather: screenshots and a recording of both clients under the bad profile, and the limits of simulating the network on one machine. -->

Phase 0 asked one question before more time went into the proposal: can this stack carry a browser-playable action MMO? It built the riskiest parts first, on the smallest game that could test them, and measured them against thresholds written down beforehand. Everything passed, with one known issue.

ShapeLand's phase 0 can be played at https://shapeland.lucyawrey.com, in the browser, with no download.

## What was built

- **The stack benchmark:** a bare server on .NET's own web server (Kestrel) sending position-sized messages over WebSocket to 300 bots, with no game logic, to test the stack before any of our game code.
- **Shared code in Unity:** the same .NET libraries for messages, content and movement compile on the servers, in the bots and in the Unity client, and content files can be edited in the Unity editor.
- **ShapeLand, sliding, jumping and chatting:** players join with a name, a shape (cube, diamond or pyramid) and colours, move around a floating island, chat with bubbles over their shapes, and respawn when they fall off. The server checks every move, and a cheating client gets snapped back. It runs on one game server with no accounts.
- **Clients:** a web build and desktop builds (Linux and macOS) from one Unity project, plus headless bots that use the same client code.
- **Tools:** simulated network conditions in Docker, so every client (browser, desktop and bots) plays through real latency, jitter and packet loss; headless runs that measure what players would see; and a one-command public test build (a page on Vercel and a game server on a small AWS instance, about 20 cents a day).

## The stack benchmark

300 bots against one server core at 30 ticks a second, run on a clean network and on an impaired one (about 80 ms round trip and 1% loss), locally and on AWS with the server and bots on separate machines.

| Check | Limit | AWS, clean | AWS, impaired |
| --- | --- | --- | --- |
| Tick work, worst 1% | 20 ms | 2.9 ms | 3.1 ms |
| Ticks over budget | 0.1% | 0 | 0 |
| Worst garbage-collection pause | 10 ms | 8.7 ms | 10.04 ms |
| Server CPU | 0.5 cores | 0.28 | 0.47 |
| Gap between updates, worst 1% | 150 ms | 40 ms | 132 ms |
| Bandwidth down per player | 10 KB/s | 7.6 KB/s | 7.6 KB/s |

Every check passed locally. On AWS, every check passed except the worst garbage-collection pause under the impaired network: a one-off pause of 10–20 ms as 300 players join at once, which never caused a missed tick. No runtime setting fixed it; pooling each connection's state up front is the likely fix.

Server CPU is the tightest margin. It nearly doubles on the impaired network with the same traffic, mostly in the operating system's TCP handling: players far away cost the server more than nearby ones.

## ShapeLand under a simulated network

Three network profiles, each way on the server's link: **good** (about 80 ms round trip, 1% loss), **bad** (about 200 ms, 2% loss) and **awful** (about 200 ms, 5% loss, reported but not required to pass). The criteria, written down before tuning:

1. **Fairness:** honest players are never snapped back.
2. **Cheaters:** every cheating bot is caught.
3. **Smoothness:** other players rarely stop or visibly hitch.
4. **Steadiness:** the delay other players are drawn at doesn't swing.
5. **Lag:** that delay stays under 300 ms on good and 550 ms on bad.
6. **By eye:** the project lead's sign-off in the web and desktop builds, on good and bad.

Final runs (10 honest bots and 1 cheater for 3 minutes per profile):

| Profile | Honest snap-backs | Cheater caught | Delay median | Visible hitches per player per minute |
| --- | --- | --- | --- | --- |
| none | 0 | yes | 158 ms | 0.0 |
| good | 0 | yes | 239 ms | 1.4 (limit 2) |
| bad | 0 | yes | 453 ms | 2.5 (limit 3) |
| awful (reported only) | 7 | yes | 619 ms | 4.6 |

Every criterion passed on good and bad, and the project lead signed it off by eye: "it finally looks smooth, even on bad".

## What we learned

- **TCP stalls are the main problem, not average latency.** Over WebSocket, a lost packet holds up everything behind it for one or two round trips. Most of the tuning was about hiding those stalls.
- **Other players keep moving through a stall.** When updates stop arriving, a client keeps drawing other players along their last velocity for a moment, then eases into the right place when the late update comes. This is what made movement look smooth on the bad profile; World of Warcraft, also over TCP, moves other players the same way.
- **One player's bad connection shouldn't slow everyone down.** At first, a single far-away player added about 50 ms to everyone's delay. Updates now carry their sender's timing with that sender's usual latency taken out, so a far player's own stalls still show, but they no longer make everyone else's game laggier.
- **The delay adapts, but slowly downwards.** It rises at once for a stall and falls only after a quiet spell, which stopped it swinging.
- **Movement runs on the server's clock.** Each player's own movement steps on the server's ticks, so what they report matches what they simulated, which removed small speed hitches everyone saw in others.
- **Cheat checks were tuned from measurements:** the server records how much of each tolerance honest players needed, and the limits were tightened to just above that. A cheater moving 15% too fast is still caught.

## Known issues and what's next

- **The join pause** in the stack benchmark, and whether garbage-collection pauses grow on a real zone server holding far more state. A heavier benchmark is planned to test that before the full load test.
- **Far players are still visible as hitches:** with one in twelve players on a 300 ms connection, others see about 2.5 visible hitches a minute, mostly the far player's own movement being eased back into place.
- **Phase 0 has no interest management:** everyone sees everyone. Splitting the world into cells and prioritising what each player is sent comes with the full load test.
- **The full load test opens phase 1:** 100+ bots and real web clients on the real game, against thresholds still to be written down.
