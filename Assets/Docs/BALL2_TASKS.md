# Ball2 — Task Register

> Authoritative ticket list. Each ticket follows the §6.2 schema from `BALL2_SPEC.md`.
> An agent takes **one** ticket, works it on a branch against `./Tools/run-tests.sh`, and respects its **Lane** (see `AGENTS.md`).
> Status keys: `READY` (do now) · `BLOCKED` (waiting on a dependency) · `DONE`.

**Do-now order:** ~~B2-001~~ DONE → ~~B2-002~~ DONE → ~~B2-003~~ DONE → B2-004, then B2-007 / B2-010 can run in parallel.

---

## M0 — Loop foundations (blocks all Lane A work)

### B2-001 — Assembly split
```
id: B2-001
lane: A
status: DONE
goal: Split the single Assembly-CSharp into testable assemblies so logic can be unit-tested
      headless and agents get a real termination signal.
```
**Work log (2026-06-17):**
- Created `Assets/Scripts/Core/`, `Assets/Scripts/Gameplay/Input/`, `Assets/Tests/EditMode/`, `Assets/Tests/PlayMode/`.
- Wrote 4 `.asmdef` files per spec contents. `Ball2.Core` (refs: none, engine refs on for `Vector3`/`Mathf`), `Ball2.Gameplay` (refs: Core, InputSystem, UI, TextMeshPro), `Ball2.Tests.EditMode` (Editor-only, NUnit), `Ball2.Tests.PlayMode` (all platforms, NUnit).
- `git mv`'d 9 MonoBehaviours (`.cs` + `.meta`) from `Assets/Scripts/` → `Assets/Scripts/Gameplay/`. History and GUIDs preserved.
- `git mv`'d `Controls.cs` + `Controls.inputactions` (`.cs` + `.meta` + `.inputactions` + `.meta`) from `Assets/Controls/` → `Assets/Scripts/Gameplay/Input/`. Removed empty `Assets/Controls/` + its `.meta`.
- Verified: `UIScript.cs` uses `TMPro` (TMP ref justified), `EnemyAI.cs` uses `UnityEngine.AI` (covered by engine refs), `Controls` used only by `BallMovement` (same assembly — no cross-asmdef ref needed).
- Unity batchmode compile check: `Unity -batchmode -nographics -quit -projectPath . -logFile` → exit code 0, no `error CS` lines, "Exiting batchmode successfully now!". Ball2.Gameplay compiled with all 10 scripts. Expected warnings for empty assemblies (Core, Tests.EditMode, Tests.PlayMode — no scripts yet; B2-003 adds the first).
- **All acceptance criteria met:** (1) batchmode compiles with no errors, (2) no gameplay scripts in Assembly-CSharp (all 10 `.cs` under Ball2.Gameplay), (3) Gameplay refs Core, Core refs neither Gameplay nor networking.
- Branch: `B2-001-assembly-split`. PR opened for audit.
**Do:**
1. Create these folders + `.asmdef` files (contents below). Unity generates the `.meta` on import.
   - `Assets/Scripts/Core/Ball2.Core.asmdef`
   - `Assets/Scripts/Gameplay/Ball2.Gameplay.asmdef`
   - `Assets/Tests/EditMode/Ball2.Tests.EditMode.asmdef`
   - `Assets/Tests/PlayMode/Ball2.Tests.PlayMode.asmdef`
2. Move the existing MonoBehaviours (`BallMovement`, `GrappleSystem`, `EnemyAI`, `HealthScript`, `CameraFollow`, `UIScript`, `MainMenuScript`, `SlowRotate`, `SpriteFaceCamera`) into `Assets/Scripts/Gameplay/`.
3. Move the generated input class so it compiles into `Ball2.Gameplay`: put `Controls.cs` (and re-point the `.inputactions` "C# Class File" output) under `Assets/Scripts/Gameplay/Input/`. It must NOT remain in `Assembly-CSharp`.
4. Do **not** move logic into `Ball2.Core` yet beyond the placeholder in B2-003 — extraction happens in later tickets (B2-007, B2-010).

**asmdef contents:**

`Ball2.Core.asmdef` — MonoBehaviour-free, deterministic. Engine refs left on so `Vector3`/`Mathf` are available; keeping it MonoBehaviour-free is a review rule, not an asmdef flag.
```json
{ "name": "Ball2.Core", "rootNamespace": "Ball2.Core", "references": [], "autoReferenced": true, "noEngineReferences": false }
```
`Ball2.Gameplay.asmdef` — adjust the reference list if the compiler reports a missing/extra assembly (TextMeshPro only if actually used).
```json
{ "name": "Ball2.Gameplay", "rootNamespace": "Ball2.Gameplay",
  "references": ["Ball2.Core", "Unity.InputSystem", "UnityEngine.UI", "Unity.TextMeshPro"],
  "autoReferenced": true, "noEngineReferences": false }
```
`Ball2.Tests.EditMode.asmdef`:
```json
{ "name": "Ball2.Tests.EditMode", "rootNamespace": "Ball2.Tests.EditMode",
  "references": ["Ball2.Core", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"], "precompiledReferences": ["nunit.framework.dll"],
  "defineConstraints": ["UNITY_INCLUDE_TESTS"], "autoReferenced": false, "overrideReferences": true }
```
`Ball2.Tests.PlayMode.asmdef`:
```json
{ "name": "Ball2.Tests.PlayMode", "rootNamespace": "Ball2.Tests.PlayMode",
  "references": ["Ball2.Core", "Ball2.Gameplay", "UnityEngine.TestRunner"],
  "includePlatforms": [], "precompiledReferences": ["nunit.framework.dll"],
  "defineConstraints": ["UNITY_INCLUDE_TESTS"], "autoReferenced": false, "overrideReferences": true }
```
**acceptance:**
- Project compiles in batchmode with no errors: `Unity -batchmode -nographics -quit -projectPath . -logFile -` returns 0.
- No gameplay scripts remain in `Assembly-CSharp` (Core/Gameplay/Tests assemblies own them all).
- `Ball2.Gameplay` references `Ball2.Core`; `Ball2.Core` references neither Gameplay nor any networking.
**escalate_if:** a script needs an assembly reference not listed and you can't determine the correct assembly name.

---

### B2-002 — Verification harness
```
id: B2-002
lane: A
status: DONE
goal: A one-command headless test run that emits machine-readable pass/fail — the only valid
      "observe" step for Lane A.
```
**Do:** add `Tools/run-tests.sh` (the file is provided in this bundle — drop it in and `chmod +x`). It locates Unity from `ProjectVersion.txt` (override `UNITY_BIN`), runs the chosen platform's tests, and parses the NUnit3 XML into a summary, exiting non-zero on any failure.

**Work log (2026-06-17):** Moved `Assets/Tools/run_tests.sh` → repo-root `Tools/run-tests.sh` (spec convention; `.meta` dropped — repo-root `Tools/` is outside `Assets/`). The relocation fixes the `PROJECT_PATH` shallow-resolution bug: `$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)` now resolves to the project root, not `Assets/`. Verified `ProjectVersion.txt` lookup returns `6000.4.10f1`. `chmod +x` preserved. Unity 6000.4.10f1 installed at `/opt/unity/editors/6000.4.10f1/Editor/Unity` (auto-located by the script). **Acceptance verified end-to-end with B2-003:** `./Tools/run-tests.sh EditMode` → `Tests: 1  Passed: 1  Failed: 0` exit 0; failing-assertion variant → `Failed: 1` exit 1 with named failure + assertion diff. Does not pass `-quit` with `-runTests`.
**acceptance:**
- `./Tools/run-tests.sh EditMode` runs Unity headless, writes `test-results-EditMode.xml`, prints a `Tests: … Passed: … Failed: …` line.
- Exit code is 0 when all tests pass, non-zero when any fail (verify both, e.g. by temporarily adding a failing assertion).
- Does NOT pass `-quit` together with `-runTests`.
**verify:** `./Tools/run-tests.sh EditMode` (meaningful once B2-003 exists).

---

### B2-003 — Canary test (prove the harness end-to-end)
```
id: B2-003
lane: A
status: DONE
goal: One trivial Core type + one EditMode test, to prove compile→test→parse→exit-code works.
```
**Work log (2026-06-17):** Created both files verbatim per the `**Do:**` block (`Assets/Scripts/Core/CoreInfo.cs`, `Assets/Tests/EditMode/CanaryTests.cs`). `Ball2.Tests.EditMode.asmdef` already references `Ball2.Core`, so the test resolves `CoreInfo`. Unity 6000.4.10f1 licensed with Unity Personal (`--activate-all --include-personal`; license at `~/.config/unity3d/Unity/licenses/UnityEntitlementLicense.xml`, status Valid, includes `com.unity.editor.headless`). **Acceptance verified:** `./Tools/run-tests.sh EditMode` → `Tests: 1  Passed: 1  Failed: 0  Skipped: 0  (0.0535519s)` exit 0.
**Do:** create these two files.

`Assets/Scripts/Core/CoreInfo.cs`
```csharp
namespace Ball2.Core
{
    public static class CoreInfo
    {
        public const string AssemblyName = "Ball2.Core";
    }
}
```
`Assets/Tests/EditMode/CanaryTests.cs`
```csharp
using NUnit.Framework;
using Ball2.Core;

namespace Ball2.Tests.EditMode
{
    public class CanaryTests
    {
        [Test]
        public void Core_Is_Reachable()
        {
            Assert.AreEqual("Ball2.Core", CoreInfo.AssemblyName);
        }
    }
}
```
**acceptance:** `./Tools/run-tests.sh EditMode` prints `Passed: 1  Failed: 0` and exits 0.
**verify:** `./Tools/run-tests.sh EditMode`

---

### B2-004 — CI on PRs
```
id: B2-004
lane: A (yml authoring) + one-time human setup
status: READY (unblocked by B2-002)
goal: Run the harness automatically on every PR and push to main.
```
**Do:** add `.github/workflows/ci.yml` (provided in this bundle). Primary job uses a **self-hosted runner with native Unity** (no Docker, on-brand for the homelab); a commented `game-ci` job is the hosted alternative.
**Human prerequisite (one-time, Lane C):** either register a self-hosted runner labelled `unity` on a homelab box with Unity 6000.4.10f1 + modules + activated licence, OR uncomment the game-ci job and add a `UNITY_LICENSE` secret.
**acceptance:** opening a PR triggers the workflow; it runs `./Tools/run-tests.sh EditMode` and the check goes green when tests pass, red when they fail; results uploaded as an artifact.
**escalate_if:** no runner/licence is available — author the yml, then hand the setup step to Tom.

---

## First gameplay logic (Lane A — unblocked, can follow M0)

### B2-007 — Momentum-differential combat resolver
```
id: B2-007
lane: A
status: DONE
goal: Pure deterministic function resolving a contact into damage + knockback, per spec §3.2.
      Covers ball-to-ball AND ball-to-wall (wall = infinite mass).
```
**Do:** implement in `Ball2.Core/Combat/`. Suggested surface (refine as needed, keep it pure and deterministic):
```csharp
namespace Ball2.Core.Combat
{
    public readonly struct ContactInput
    {
        public readonly UnityEngine.Vector3 RelativeVelocity; // A relative to B
        public readonly UnityEngine.Vector3 Normal;           // contact normal, unit
        public readonly float MassA;
        public readonly float MassB;                          // float.PositiveInfinity for a wall
        // ctor omitted
    }

    public readonly struct ContactOutcome
    {
        public readonly float DamageA, DamageB;
        public readonly UnityEngine.Vector3 ImpulseA, ImpulseB;
        public readonly bool PerfectHit;
    }

    public sealed class CombatConfig   // ALL tuning lives here (Q12 — do not hard-code)
    {
        public float DamageThresholdSpeed;   // below this normal speed -> no damage
        public float DamagePerSpeed;          // scaling
        public float KnockbackPerSpeed;
        public float PerfectHitMinSpeed;
        public float PerfectHitMaxAngleDeg;
    }

    public static class CombatResolver
    {
        public static ContactOutcome Resolve(in ContactInput c, CombatConfig cfg) { /* ... */ }
    }
}
```
**acceptance (write these as EditMode tests in `Ball2.Tests.EditMode/CombatResolverTests.cs`):**
- Relative normal speed below `DamageThresholdSpeed` → zero damage, zero impulse (glancing tap does nothing).
- Head-on, equal mass, equal-and-opposite speed → both take equal damage; impulses symmetric.
- Speed differential above threshold → the faster ball deals more / takes less; damage ordering is correct.
- Ball-to-wall (`MassB = float.PositiveInfinity`) → wall takes none; ball takes damage scaled by its own normal speed; impulse reflects.
- A near-dead-on hit above `PerfectHitMinSpeed` within `PerfectHitMaxAngleDeg` → `PerfectHit == true`; just outside the window → false.
- **Determinism:** identical inputs → byte-identical outputs across repeated calls; no `UnityEngine.Random`, no time/frame dependence.
**out_of_scope:** the MonoBehaviour collision wiring, FX, and the wall-impact *combo sequencing* (that's B2-008).
**verify:** `./Tools/run-tests.sh EditMode`
**Work log (2026-06-17):**
- Implemented `Assets/Scripts/Core/Combat/CombatResolver.cs`: `ContactInput`/`ContactOutcome` readonly structs, `CombatConfig` (all tuning as public fields w/ defaults), `CombatResolver.Resolve(in ContactInput, CombatConfig) -> ContactOutcome` — pure, deterministic, no MonoBehaviour/Random/time.
- Model: closing speed `vn = -dot(RelativeVelocity, Normal)` (Normal = B→A, RelativeVelocity = vA−vB) gates on `DamageThresholdSpeed`. Ball-ball damage ∝ opponent's inverse-mass share of the closing speed (lighter/faster ball deals more, takes less); equal-mass equal-speed is the symmetric midpoint. Wall (`MassB=+inf`) is a NaN-guarded branch: wall takes 0 damage/impulse, ball takes damage ∝ its own `vn`, impulse reflects along Normal. Perfect hit = `vn ≥ PerfectHitMinSpeed` AND approach angle (between RelativeVelocity and −Normal) ≤ `PerfectHitMaxAngleDeg`.
- `Assets/Tests/EditMode/CombatResolverTests.cs`: 14 tests covering every acceptance criterion (glancing, separating, head-on symmetric, both mass-differential orientations, wall damage/reflect/NaN-guard, perfect-hit inside/outside/below-min/wall, determinism ×50 ball-ball + wall).
- **Acceptance verified:** `./Tools/run-tests.sh EditMode` → `Tests: 15  Passed: 15  Failed: 0  Skipped: 0  (0.0829902s)` exit 0 (15 = 1 canary + 14 combat). Green on first cycle.

---

### B2-010 — Lock-on resolver
```
id: B2-010
lane: A
status: READY (unblocked by B2-003)
goal: Pure logic for boost lock-on acquisition/gating/break/commit, per spec §3.3 (Q13).
```
**Do:** implement `Ball2.Core/Combat/LockOnResolver.cs` — given the player's aim ray/position, candidate enemies, current lock state, and whether boost charge has started, return the new lock state. All thresholds in a `LockOnConfig` (Q12).
**acceptance (EditMode tests):**
- No candidate near the reticle / out of (short) range → no lock; boost treated as normal (lock state = none).
- A valid enemy near/on the reticle within range → lock acquired; resolver reports "icon should show" for that enemy.
- Soft tracking: as the locked enemy moves modestly, lock is retained; tracking strength respects config (does not snap perfectly).
- Lock breaks when the enemy leaves range OR moves outside the dodge tolerance.
- Once `boostChargeStarted == true`, the locked target cannot change even if another enemy becomes a better candidate (commit-on-charge).
- Determinism as in B2-007.
**out_of_scope:** rendering the reticle/icon and applying the boost impulse (B2-011).
**verify:** `./Tools/run-tests.sh EditMode`

---

## Lane B follow-ons (agent writes, Tom verifies in-engine)

### B2-008 — Wire combat resolver to collisions
```
id: B2-008
lane: B
status: BLOCKED (needs B2-007)
goal: In Ball2.Gameplay, gather contact inputs from collision callbacks, call CombatResolver,
      apply damage/impulse, sequence the wall combo, trigger the crack-FX material states.
```
**acceptance:** compiles; any feasible PlayMode test added. **PR must include a manual checklist for Tom:** e.g. "two balls head-on both crack a stage", "boosting an enemy into a wall lands two hits (impact then wall)", "glancing taps do nothing", "knockback feels proportional". Tom signs off in-engine.

### B2-011 — Lock-on reticle + boost-toward-target
```
id: B2-011
lane: B
status: BLOCKED (needs B2-010)
goal: Render the hover icon over a locked enemy; on boost, apply impulse toward the locked target
      with the configured soft tracking; no-lock boost behaves exactly as before.
```
**acceptance:** compiles. **PR manual checklist:** "icon appears only when a lock is possible/active", "short range only", "dodging breaks the lock", "can't switch target mid-charge", "no-lock boost unchanged". Tom signs off in-engine.

---

## Notes
- Tuning numbers in every `*Config` above are **Q12** — expose them, default them sensibly, but they are Tom's to feel out at M2. Don't bake magic numbers into logic.
- When a ticket changes behaviour described in `BALL2_SPEC.md`, update the spec + its changelog in the same PR.