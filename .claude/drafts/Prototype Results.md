# Prototype Results

<!-- Agent draft, raw material: measurements and findings moved here from `.claude/design/prototype.md` once phase 0 was done, to be written up as the prototype report (todo 4a). The project lead copies what they need into their own report in `docs/`. Still to gather: screenshots and a recording of both clients under the bad profile, and the limits of simulating the network on one machine. -->

## Stack benchmark

Thresholds and setup are in `.claude/design/prototype.md` (Stack benchmark).

- **Local Docker runs pass every threshold** (2026-10-06; AMD Ryzen 7 3700X, server pinned to one core, 300 bots, full 10-minute windows).

  | Check | Limit | Clean | Impaired |
  | --- | --- | --- | --- |
  | Tick work, median | 5 ms | 0.32 ms | 0.36 ms |
  | Tick work, worst 1% | 20 ms | 2.2 ms | 2.1 ms |
  | Ticks over budget | 0.1% | 0 | 0 |
  | Worst GC pause | 10 ms | 5.7 ms | 4.2 ms |
  | Server CPU | 0.5 cores | 0.21 | 0.44 |
  | Gap between updates, worst 1% | 150 ms | 39 ms | 127 ms |
  | Round trip, median | 10 ms (clean only) | 0.08 ms | 82 ms |
  | Connection failures | 0 | 0 | 0 |
  | Bytes down per bot | 10 KB/s | 7.6 KB/s | 7.6 KB/s |

- **Server CPU is the tightest margin:** it doubles under the impaired network (0.21 to 0.45 cores) with the same traffic. Most of the increase is kernel time (0.11 to 0.29 cores; the server's own code goes from 0.10 to 0.16).
- **Most of that is TCP's real cost at a long round trip, not the emulator:** on AWS, with no netem on the server's machine, the server's kernel time still doubles under the impaired profile (0.13 to 0.26 cores). Players far away cost the server more CPU than nearby ones.
- **AWS runs (2026-10-06; two `c7i-flex.large`, free plan, us-east-2, full 10-minute windows): clean passes, impaired misses one check.**

  | Check | Limit | Clean | Impaired |
  | --- | --- | --- | --- |
  | Tick work, median | 5 ms | 0.53 ms | 0.65 ms |
  | Tick work, worst 1% | 20 ms | 2.9 ms | 3.1 ms |
  | Ticks over budget | 0.1% | 0 | 0 |
  | Worst GC pause | 10 ms | 8.7 ms | **10.04 ms (fail)** |
  | Server CPU | 0.5 cores | 0.28 | 0.47 |
  | Gap between updates, worst 1% | 150 ms | 40 ms | 132 ms |
  | Round trip, median | 10 ms (clean only) | 0.27 ms | 82 ms |
  | Connection failures | 0 | 0 | 0 |
  | Bytes down per bot | 10 KB/s | 7.6 KB/s | 7.6 KB/s |
  | Bots CPU (validity) | 80% | 19% | 40% |

- **Milestone 1 passes, with one known issue** (project lead, 2026-10-06): every check passes except the worst GC pause on AWS, a one-off pause when players join. It never caused a missed tick. Its likely fix is the connection slot pool (`prototype.md`, Considering), before the stage 2 load test. The GC limit keeps counting pauses from players joining (project lead): a restart or a crowd teleporting in looks similar in real play.
- **The known issue: one or two blocking collections of 10–20 ms on AWS as players join,** copying their connections' state, about 7 MB for 300 (about 22 KB each), out of gen0 and then gen1. Locally the same pause is 6–10 ms: copying costs about 2.8 ms per MB on `c7i-flex.large` against 1–1.6 ms per MB locally. Every other pause is a gen0 under 3 ms with about 80 KB surviving; nothing deep recurs in steady state.
- **What the GC investigation found** (AWS, per-pause log; the network profile makes no difference):
  - *The GC:* workstation GC with background GC on, one heap. `DOTNET_PROCESSOR_COUNT=1` makes the runtime fall back from the Web SDK's server GC, so DATAS is off too.
  - *No setting fixes it* (clean profile, worst pause in the window):

    | Run | Worst pause |
    | --- | --- |
    | Defaults | 8.7–10.1 ms (blocking gen1) |
    | One full collection after startup (a heap settle) | 15.6–18.0 ms (blocking gen2) |
    | `SustainedLowLatency` mode | 10.1 ms (blocking gen1); with the settle 15.2 ms |
    | Bots joining over 50 s instead of 10 s | 13.5 ms (blocking gen0, then gen1); with the settle 19.9 ms |

  - *A heap settle makes it worse:* a full collection after startup leaves gen2 under 1 MB, so the joins overflow gen2's budget and the next deep collection is a blocking gen2. Rejected.
  - *A slower ramp doesn't spread the cost:* gen0 holds about 9 s of allocation (about 900 KB/s in steady state), so 50 s of joins still land in one or two collections.
- **Why it matters later:** the bench holds almost no long-lived state. A real zone server holds far more (entities, AI, items, zone data, content), and if a deep blocking collection grows with that, it becomes missed ticks, which action combat feels most. The heavy-heap variant (`prototype.md`, Considering) tests that.

## ShapeLand phase 0: network tuning (3c)

How the runs work (netem profiles, `tools/shapeland-netrun.sh`) and the pass criteria are in `.claude/design/prototype.md`. Each step below is one tuning pass, in order.

- **First reading under good** (project lead, in the browser): the delay settles around 200–300 ms with similar targets but swings a lot. Likely cause: a TCP resend on the client's downlink delays every entity's states at once, so whether a stall is inside the 2 s window decides the 95th percentile; once one leaves the window the delay shrinks and the next stall finds it short. To measure in the baseline before changing anything (a longer window, growing fast and shrinking only after a quiet spell, or smoothing the target).
- **Baseline** (commit `59065bf`, today's settings; one 180 s run each):

  | Profile | Honest snap-backs | Cheater caught | Ping median (best) | Delay median (5th–95th) | Target 5th–95th, moving | Holds |
  | --- | --- | --- | --- | --- | --- | --- |
  | none | 0 | yes | 33 ms (33) | 195 ms (178–212) | 177–213 ms, 9 ms/s | 0.0% |
  | good | 0 | yes | 99 ms (99) | 280 ms (240–335) | 235–341 ms, 28 ms/s | 1.6%, 46 ms each |
  | bad | 1 (a fall) | yes | 231 ms (198) | 520 ms (439–605) | 325–631 ms, 91 ms/s | 2.5%, 68 ms each |
  | awful | 11 (9 falls, 2 too fast) | yes | 297 ms (198) | 607 ms (525–718) | 410–821 ms, 103 ms/s | 3.0%, 115 ms each |

  Findings: honest snap-backs come mostly from the server finishing falls (`SilenceSeconds` 0.5 s is shorter than a TCP resend stall at these pings); under awful, two honest bots were also too fast, likely a burst after a stall longer than the speed budget's one second of banked movement. The target swings three times as fast under bad as under good. With bots at about 300 ms (`good` plus `--bots-net bad`), 3 to 7 of a few honest bots' snap-backs in 30 s were all finished falls; three cheaters were each still caught 38–49 times in 70 s.
- **Far players pull good players' delay up** (`tools/shapeland-netrun.sh good --far N`: N honest "Far" bots in their own container under bad on top, about 300 ms; same commit as the baseline plus the far option):

  | Far players | Good players' delay median (5th–95th) | Their holds | Far players' own delay, holds | Far players' snap-backs |
  | --- | --- | --- | --- | --- |
  | none (baseline) | 280 ms (240–335) | 1.6%, 46 ms each | — | — |
  | 1 of 12 | 333 ms (297–374) | 0.9%, 49 ms each | 582 ms, 2.7% | 1 (a fall) |
  | 3 of 14 | 358 ms (328–400) | 0.9%, 57 ms each | 603 ms, 3.2% | 2 (falls) |

  One far player among eleven others already adds about 50 ms to everyone's delay (past the 300 ms lag criterion), because their states make up more than 5% of the samples and each carries their own trip; the extra delay also halves good players' holds. Shifting stamps by each sender's smoothed latency targets exactly this.
- **Fix A, the silence before a server fall** (1.0 s, from 0.5; the server now catches up on the arc since the last report when it takes over, so a silent player is where gravity has taken them rather than hanging, then dropping; `netcode.md`): fairness passes under good and bad.

  | Profile | Honest snap-backs | Delay median (5th–95th) | Target moving | Holds |
  | --- | --- | --- | --- | --- |
  | good | 0 | 278 ms (242–330) | 29 ms/s | 1.6%, 46 ms each |
  | bad | 0 (no server falls) | 516 ms (417–595) | 88 ms/s | 2.3%, 97 ms each |
- **States sent to others carry the sender's stamp shifted by their smoothed latency** (agent suggestion, adopted by the project lead to keep good connections from being pulled down by others; `StateStamp.Broadcast`): the median of arrival minus stamp over the last 5 s, approached at no more than 5% of real time, so the spacing of a player's states stays as they sent it while their steady latency stops counting towards everyone's delay; their jitter and stalls still do. Validation keeps the sender's own stamps. No game was found doing exactly this: server-authoritative engines stamp with the server's tick (no sender trip, but the sender's jitter shows), TrinityCore passes on converted client times (our earlier model), and voice and video calls give each sender's stream its own jitter buffer.

  | Run | Delay median (5th–95th) | Holds | Target moving | Honest snap-backs |
  | --- | --- | --- | --- | --- |
  | good | 201 ms (165–256), was 278 | 1.4%, 43 ms each | 26 ms/s | 0 |
  | bad | 388 ms (305–463), was 516 | 2.3%, 65 ms each | 92 ms/s | 0 |
  | good, 1 far of 12 | 200 ms (167–256), was 333 | 1.3%, 46 ms each | 26 ms/s | 0 |
  | good, 3 far of 14 | 212 ms (174–262), was 358 | 1.3%, 43 ms each | 29 ms/s | 0 |

  The far players' own view (about 300 ms with both profiles' loss): delay about 500 ms (was about 590), holds 2.8–3.6%; with 3 of them, 1 too-fast and 1 finished fall among them, beyond the bad profile the criteria cover. Still failing under bad: steadiness (92 ms/s) and, narrowly, smoothness; fix B next.
- **Fix B, the delay's target holds its peaks for 5 s** (`InterpolationDelay.HoldSeconds`): every quarter second the 95th percentile of the last 2 s gives a raw target, and the target is the highest raw target of the last 5 s, so it rises at once for a stall but falls only 5 s after it; the delay follows at 4% and 2% as before. The run report now also shows how fast the delay itself moves.

  | Profile | Delay median (5th–95th), moving | Target moving | Holds | Honest snap-backs |
  | --- | --- | --- | --- | --- |
  | good | 235 ms (187–287), 7 ms/s; was 201 | 8 ms/s, was 26 | 0.7%, 45 ms each; was 1.4%, 43 | 0 |
  | bad | 447 ms (370–643), 13 ms/s; was 388 | 21 ms/s, was 92 | 0.9%, 82 ms each; was 2.3%, 65 | 0 |

  Good passes every criterion. Bad fails only on hold length (82 ms against 60): holds are less than half as frequent, but those left are the long stalls the 95th percentile doesn't cover, so the average length rose while the total time held halved (about 0.7 ms per move, from 1.5). A second seed under bad (seed 2, with Unity builds sharing the CPU) agreed: delay median 431 ms, target moving 18 ms/s, holds 0.8% and 0.56 ms held per move, no honest snap-backs.
- **Shifted stamps are no longer held to the arrival tick:** a report arriving faster than the median lost a tick of its spacing, which others drew at double speed, past the teleport limit, as a jump; 19% of honest stamps under good were clamped. A broadcast stamp may now be later than its arrival by up to the shift (0 of 18,003 held back under good). Good: delay 230 ms, holds 0.28 ms per move; bad: 441 ms, 0.89 ms per move; no honest snap-backs.
- **Speed hitches from whole-tick report stamps** (found from the project lead's playtest: occasional snaps of honest players under good, smooth otherwise): the run report now counts the others' moves drawn as jumps (0) and those whose ground speed changes by over 30% from the step before: 18.6% under none, 16.9% under good. A report is stamped with the server tick rounded down, up to a tick off the moment its position describes, so two steps of movement stamped three ticks apart are drawn at two thirds of the speed, and three steps over two ticks at one and a half. Every sender does it (the Unity client too); 3b's drawn-speed spread of 1.0–1.6 m/s was this. Fixed by **running the player's own simulation on the server's clock** (project lead chose it over fractional stamps in the protocol, which would have turned the server's tick-based rules into fractional time): `FixedStep` steps on a grid (step n at n over the rate) and is driven by `ClientSession.ServerSeconds`, so every step of the bots (30 Hz) and every other step of the Unity client (60 Hz) falls exactly on a server tick (`TickOfStep`); only those steps are reported, stamped with their tick, so stamp and position agree and every reported state is one the simulation produced. A frame carries one tick, so a report for another tick sends what's queued first. The bots wake at 60 Hz and catch up missed steps, where their 30 Hz timer used to drop them (half speed for a step). Blending a report's position between steps to the stamped tick's moment was tried first and dropped: the blended point can cut a block's corner (three honest InsideBox snap-backs under none). Under none, drawn moves now hitch 9.3% of the time against 9.1% of the bots' own reports (their real slowing through turns and around blocks), and the delay settles at 153 ms (was 195), near its 133 ms floor. Under good and bad every criterion still passed (good: delay 233 ms, 0.43 ms held per move; bad: 429 ms, 0.85; no honest snap-backs), but the network still added hitches (12.6% drawn against the bots' own 9.4% under good; under bad also 10 jumps). Two more stamp fixes on the server: the broadcast stamp now starts from the sender's own tick, bounded only to within the 0.5 s maximum lag of arrival either way (a report crossing faster than the client's estimate assumes arrived before its stamp, and holding it to arrival broke the spacing; validation keeps the held stamp); and the whole-tick shift changes only once the smoothed shift is 0.75 tick away from it (rounding a shift hovering near a half moved a stamp every few reports; 7 in 1,000 with the hysteresis against 13 in a test with random 2- or 3-tick trips). Under good after the first: 11.7% drawn against 9.2% own, no jumps, no stamps held back. By eye (project lead, web build under good with 20 bots): better, with occasional jitter or snaps left; dead reckoning (below) then fixed them.
- **Dead reckoning for other players** (project lead, from their playtest under bad: frequent visible hitches, which most games don't accept): holding at the newest state when the buffer runs dry was the wrong choice over TCP with client-reported movement, since a lost packet stalls a stream for one to two round trips. States sent to others carry the mover's velocity; a client that runs out of states keeps drawing the entity along it (with gravity in the air, never below the ground) for a capped time, then holds, and when the late state arrives eases the difference out over a short blend instead of jumping. World of Warcraft, over TCP, moves other players this way; Source extrapolates up to a quarter of a second.
- **A visible-hitches criterion** (project lead): the run report counts what a player would see as a hitch (a jump, a blend-back over a small distance, or extrapolation running out) per other player per minute, and a criterion for bad is set on it, so the criteria can't pass while movement visibly hitches again. The numbers are proposed after the first measurement, for the project lead to approve. Awful stays reported only; bad is the profile that has to look right.
- **Dead reckoning, first results** (0.3 s cap, 0.15 s blend, a blend-back over 0.2 m counted as visible; `RemoteEntities`): states sent to others carry the mover's velocity (the server's own during a fall it finishes); a run-dry entity moves on along it, under gravity in the air and never below `ShapeLandWorld.GroundHeight` (terrain or block tops), then holds; and a long gap only counts as standing still after a state standing still, since a stall over the 200 ms idle gap had restamped the old position before the late state, a snap back then forward.

  | Profile | Visible hitches per other player per minute | Ran out | Blend-backs over 0.2 m | Jumps | Delay median |
  | --- | --- | --- | --- | --- | --- |
  | bad | 2.2 (about 6 before, every hold a stop and a jump) | 76 | 536 | 10 | 451 ms |
  | good | 1.2 | 17 | 326 | 0 | 243 ms |

  What's left is mostly eased corrections where a bot turned or jumped during a stall. Time held per move now measures time dead-reckoned, which isn't visible (1.26 ms under bad), so the smoothness criterion moves to visible hitches; the numbers wait for the project lead's look. The 10 jumps under bad aren't traced yet. By eye (project lead, web build under bad): "it finally looks smooth, even on bad". **Criterion 7 signed off** (project lead): under bad and good, in the web build and the Linux desktop build, other players move smoothly, and holding C gets a snap-back.
- **Final runs** (commit `1126554`; 180 s, 10 honest bots, 1 cheater, seed 1; far runs add far players under bad on top of good):

  | Run | Honest snap-backs | Cheater caught | Ping median | Delay median (5th–95th) | Target moving | Holds | Visible hitches per other player per minute |
  | --- | --- | --- | --- | --- | --- | --- | --- |
  | none | 0 | yes | 16 ms | 158 ms (137–173) | 2 ms/s | 0.0% | 0.0 |
  | good | 0 | yes | 96 ms | 239 ms (189–286) | 8 ms/s | 1.2% | 1.4 |
  | bad | 0 | yes | 224 ms | 453 ms (397–623) | 16 ms/s | 1.3% | 2.5 |
  | awful (reported only) | 7 (3 falls, 4 violations) | yes | 287 ms | 619 ms (501–810) | 27 ms/s | 2.0% | 4.6 |
  | good, 1 far of 12 | 0 | yes | 96 ms | 243 ms (194–294) | 8 ms/s | 1.4% | 2.5 |
  | good, 3 far of 14 | 1 (a fall) | yes | 96 ms | 253 ms (204–304) | 8 ms/s | 1.8% | 4.6 |

  **Every criterion passes under none, good and bad**: no honest snap-backs, the cheater caught, holds at most 2%, visible hitches 1.4 under good (limit 2) and 2.5 under bad (limit 3), the target moving at most 16 ms/s (limit 30), delay medians 239 and 453 ms (limits 300 and 550), and the project lead's sign-off by eye. Far players still barely move good players' delay (243–253 ms against 239), but their own long stalls show: good players see 2.5 to 4.6 visible hitches a minute when one in twelve to three in fourteen players are far, mostly eased blend-backs of the far players themselves; the far players see their own world at 0.8 to 2.4. In the 3-far run one good player's fall was finished by the server (Bot 01, silent in the air over 1 s on good). The code review (3a) found the bot process never paused (no gap between its samples over 0.26 s) and that player alone had a ping spike (704 ms against about 96), so it was most likely a TCP resend stall on that one connection (a lost segment whose resend was also lost: about 0.9 to 1 s on Linux, right at `SilenceSeconds`); the netrun logs now carry timestamps, and each bot's results its start time, to match the next one to a ping spike. The few jumps a run counted (10 to 20, under none too) were the speed cheater's: every honest bot counted the same 1 or 2, which only an entity they all see and none of them is can cause. The cheater runs faster than the jump limit (15 m/s against 13.5), and when its latency shift drops a tick, 1.5 m of its movement lands two ticks apart. Cheaters are now left out of the bots' smoothness counts. Bot pings now read about 16 ms high rather than 33, since the bots wake at 60 Hz.

## Movement tolerances

- **Movement tolerances, tightened in phase 0** (project lead), from measurements: the server records how much of each tolerance every player needed (`MovementHeadroom`, logged on leave; netrun prints the honest worst), and netrun's cheaters can be subtle (`--cheat-speed`, `--cheat-jump`). Honest bots never averaged over 1.0× top speed; under bad they needed up to 0.68 s of banked movement at 1.1×, 0.21 m over an exact jump's apex (from steps and slopes, the same under every profile) and 0.11 s of late arc start. Now: `SpeedFactor` 1.1 (was 1.2), `JumpFactor` 1.0 (was 1.2), `HeightSlack` 0.4 m (was 0.3), `AirTimeSlack` 0.2 s (was 0.3), `MaxBurstSeconds` 1 s (unchanged: it's also the longest a moving player can stall without a snap-back). Netruns under good and bad: no honest snap-backs, and cheaters at 1.15× speed and 1.1× jump speed caught (by the speed check). A first try with 0.25 m and 0.1 s left one honest snap-back under bad: position checks can't tell a 1.1× jump (about 0.21 m over the apex) from honest play, so height waits for the input replay check (`netcode.md`).
