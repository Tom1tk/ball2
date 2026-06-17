# Ball2 — Progress Journal

Append-only build log. **Newest entry first.** This is the operational record of what has
actually happened on each ticket: the transition, what was verified, and any gotchas the
next worker needs. It complements — it does **not** replace:
- `BALL2_TASKS.md` — where each ticket *stands* (status).
- `BALL2_SPEC.md` section 10 Changelog — *design / decision* changes.

## How to log (mandatory for every ticket transition)
On every **start / done / handoff / block** of a ticket, prepend one entry to the Log below,
in the **same commit** as the work, with these fields:
- **heading:** `### YYYY-MM-DD - B2-XXX - <short title> - <STARTED|DONE|BLOCKED|HANDOFF>`
- **by:** agent name / Tom
- **did:** one or two lines
- **verify:** the exact harness summary (e.g. `Tests: 1 Passed: 1 Failed: 0`), or `n/a` for Lane C
- **commit:** SHA, or `uncommitted`
- **notes:** gotchas, follow-ups, what the next worker needs

Keep entries short. Also flip the ticket `status` in `BALL2_TASKS.md` in the same commit.
The seeded entries below are live examples of the format.

---

## Log

### 2026-06-17 - B2-002 / B2-003 - headless harness + canary - DONE
- by: agent
- did: added `Tools/run-tests.sh`; `CoreInfo.cs` + `CanaryTests.cs`; canary proves the full compile to test to parse to exit path.
- verify: EditMode canary green — `Tests: 1 Passed: 1 Failed: 0`.
- commit: 2ac27b6
- notes: the harness is now the Lane A termination signal. **B2-007 (combat resolver) and B2-010 (lock-on) are unblocked.**

### 2026-06-17 - B2-001 - assembly split - DONE (merged PR #1)
- by: agent
- did: created `Ball2.Core` / `Ball2.Gameplay` / `Tests.EditMode` / `Tests.PlayMode` asmdefs; moved 9 MonoBehaviours + `Controls` into Gameplay; batchmode compiles clean.
- verify: batchmode compile returns 0.
- commit: 4590fa0
- notes: `Ball2.Core` is MonoBehaviour-free — keep it that way (review rule, not enforced by asmdef).

### 2026-06-17 - docs - v1.0 spec bundle landed - DONE
- by: Tom
- did: added `BALL2_SPEC.md`, `BALL2_TASKS.md`, `AGENTS.md` under `Assets/Docs`, and `Tools/run-tests.sh`.
- verify: n/a
- commit: bc6ba71
- notes: baseline for the loop protocol. (NB: the committed `BALL2_SPEC.md` lost its `/` `:` `*` characters on the way in — replace with the clean copy.)
