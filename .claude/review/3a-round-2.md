# Code review 3a, round 2

The second review round of the fixes on `review/3a` (PR #2), by fresh agents that didn't write them. These findings are to be fixed in a later session, then PR #2 merges. Round 1's findings and fixes are in PR #2's description.

Status: **scripts** and **client and content** reviews are in; **server fixes and client input** is still running (added below when it reports).

## Verdicts on round 1's fixes

- **Scripts:** all nine closed (benchmark builds get `tools/docker`; `unity-batch.sh` stops the editor; build errors shown; far players counted apart; netrun's `grep -v`; `index.html` check; local benchmark cleanup; AWS cleanup fallback; `unity-restore.sh` paths and pin). Leftovers below.
- **Client and content:** eight of nine closed. **Grounded dead reckoning is closed with a new problem** (finding C1). Transport close, bad addresses, scheme from the page, hidden-tab delay, report pacing, cheaters out of the counts, ScreenFade and rich text, and the content checks all closed. ScreenFade can't stick black: `GameView` sets `Darkness` every Update, and `OnGUI` runs after it.

## Client and content

- **C1 (low-medium, confirmed): grounded dead reckoning pops other players onto blocks and down mid-fall.** `comet/src/Comet.Client/RemoteEntities.cs:345-350`. The step allowance is `max(StepReach, distance since the state)`, measured from the state, so after d metres of reckoning any ground up to d higher or lower counts as walked (45°), far above the motor's 0.35 m step. Against the real world: running at the 1 m block at (10, -4) during a hold, the entity slides into its side, then pops up 1.1 m in one frame; walking off the 1 m block it falls 0.13 s then snaps down 0.74 m; off the 2 m block, a 1.22 m snap. Unblended, since blends only start when a state arrives. Fix: accept ground within about `StepHeight` plus `GroundSnap` of the last drawn height (or march in sub-steps of at most `StepReach`); a higher rise is a wall that stops horizontal motion. Slopes (the island's steepest is 22°), the 0.3 m step and a state at a jump's apex are fine.
- **C2 (low-medium, plausible): no connect timeout.** `comet/src/Comet.Client/WebSocketTransport.cs:73` connects with `CancellationToken.None`, and `JoinClient` has no timer. A black-holed address (the public test build's DNS still pointing at a terminated instance) leaves "Joining…" up for about 2 minutes on Linux, or the browser's own timeout. Pre-existing; matters for step 4. Fix: a connect timeout of a few seconds in the transport (and the browser one), failing with a reason.
- **C3 (low, confirmed): the bad-address message never reaches the player.** `shapeland/unity/Assets/Scripts/JoinClient.cs:157-160` with `JoinScreen.cs:241-250`: `OnFailed` ignores the error text, so a malformed `?server=` on the web says "Couldn't reach the game server. It may not be running right now." Fix: show the bad-address reason as such.
- **C4 (trivial, confirmed): misplaced comment.** `shapeland/src/ShapeLand.Bots/Bot.cs:289-292`: `IsCheaterName` sits between `CountOwnHitch`'s comment and the method.
- Not checked by the reviewer, but covered elsewhere: the Unity Play mode tests (37) passed on this branch, including the `TryServerUrl` and scheme cases on Mono. Still unverified: Mono's `ClientWebSocket` on Abort, an editor domain reload during a background close (up to 5 s), a frozen (not just hidden) browser tab.

## Scripts

- **T1 (low-medium, confirmed): netrun carries on after Ctrl+C.** `tools/shapeland-netrun.sh:105`: `trap cleanup EXIT INT TERM`, and `cleanup` never exits, so after Ctrl+C the script continues (e.g. `docker wait` on a removed container) with a wrong exit code. Pre-existing. Fix: `trap 'cleanup; exit 130' INT` and `trap 'cleanup; exit 143' TERM`. `shapeland-web.sh:154` has the same pattern (harmless there, the last stage).
- **T2 (very low, plausible): `unity-batch.sh` can still leave the editor running** if Ctrl+C lands between starting it (line 43) and setting the traps (57-58), or on an unexpected `set -e` exit in the loop. Fix: set the traps before starting the editor, and add an EXIT trap. Also `stop_unity` signals only the editor's pid, so helper processes can outlive a `kill -9` (they don't hold the lock).
- **T3 (low, plausible): one bad id fails the whole AWS terminate.** `tools/StackBench/aws/run.sh:51-53`: a non-id (such as `None`) in the merged list makes `terminate-instances` reject every id. Fix: keep only `^i-` ids.
- **T4 (very low, confirmed): a "Far cheater" would count as an honest far player** in netrun's summary (`tools/shapeland-netrun.sh:99`); unreachable today (far groups run with no cheaters). Fix: match `Cheater |cheater `.
- **T5 (minor):** `quietly` is defined identically in `shapeland-web.sh:106` and `shapeland-netrun.sh:79`; `aws/run.sh:30` warns about uncommitted changes in `comet/src` and `tools/StackBench` but not the newly archived `tools/docker`.

## Server fixes and client input

Pending.
