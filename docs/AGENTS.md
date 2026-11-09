# AGENTS.md — Ball2

You are working on **Ball2**, a physics hamster-ball aerial combat game in Unity 6 (Built-in RP).
This file is your contract. Read it before doing anything.

## Read first
- **`BALL2_SPEC.md`** — the single source of truth (design + architecture + protocol). If code and spec disagree, the spec wins until amended. Amending it is part of the job.
- **`BALL2_TASKS.md`** — the only place work comes from. Do not invent scope.

## The lane rule (most important)
Every ticket has a **Lane**. It defines what "done" means.

- 🟢 **Lane A — Autonomous.** Pure logic in `Ball2.Core` (no `UnityEngine` runtime objects, no MonoBehaviour). You own the full loop. **Done = acceptance criteria met AND `./Tools/run-tests.sh EditMode` passes.**
- 🟡 **Lane B — Assisted.** Touches Unity runtime types; effect can't be proven headlessly (feel, FX, MonoBehaviours, netcode). **Done = compiles + any feasible tests pass + you open a PR with a manual-verification checklist for the human.** You do NOT self-certify it works.
- 🔴 **Lane C — Human/in-engine.** Scene wiring, prefabs, serialized fields, Animator graphs, NavMesh bakes, physics materials, art, audio. **You do not implement these.** You may draft a checklist only.

If a 🟡/🔴 task has a logic part that could be extracted into `Ball2.Core` and made 🟢, propose that split — it is almost always right.

## Hard rules
- "Compiles" is not "done". "Looks right" is not "done". Only a passing `verify` command (or human sign-off for Lane B/C) is done.
- Touch only the ticket's `in_scope` files. Out-of-scope changes need a new ticket.
- Keep `Ball2.Core` **MonoBehaviour-free and deterministic**: no `UnityEngine.Random`, no frame-rate-dependent maths, seed all randomness. Anything that may later feed networking must be deterministic.
- Don't hard-code tuning numbers (damage, ranges, thresholds) — expose them as parameters; they are the human's to feel out (see Q12 in the spec).
- Budget: ~8 edit→test cycles per task. If two consecutive attempts fail the same assertion, change strategy or escalate — do not spin.
- Escalate (don't guess) when: a `DEFERRED` decision blocks you, acceptance criteria are ambiguous, the lane looks wrong, budget is exhausted, or a change would alter a design pillar or a networked/deterministic system.

## Running tests
```bash
./Tools/run-tests.sh            # EditMode (your Lane A loop signal)
./Tools/run-tests.sh PlayMode   # integration
```
Set `UNITY_BIN=/path/to/Unity` if the script can't find the editor.

## Current execution model
A single agentic harness with the human in the loop. `conductor` multi-agent delegation is **deferred** until `Ball2.Core` holds enough pure logic to be worth farming out (revisit ~M2–M3). For now: one ticket at a time, on a branch, against the harness.

## Netcode (when you reach it)
Fishnet, server-authoritative + client prediction. For predicted collisions you MUST use Fishnet's `NetworkCollision`/`NetworkTrigger` + `PredictionRigidbody` — raw Unity collision callbacks won't reconcile. Keep all combat *outcomes* server-authoritative in `Ball2.Core`.
## Progress logging (mandatory)
Whenever you **start, finish, hand off, or block** a ticket, prepend an entry to
`Assets/Docs/PROGRESS.md` (newest first) **in the same commit as the work**, and flip the
ticket's `status` in `BALL2_TASKS.md` in that same commit. Record what you did, the exact
harness summary line as your `verify` result, and the commit SHA. This is not optional
bookkeeping — it is how the next worker (agent or human) picks up context without
re-deriving it. See `PROGRESS.md` for the entry format.
