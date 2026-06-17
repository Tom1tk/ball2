# Ball2 — Living Design & Build Spec

 Status `v1.0` (first complete baseline; still living) · Last updated 17 June 2026 · Owner Tom (architectorchestrator)
 Repo httpsgithub.comTom1tkball2 · Engine Unity 6000.4.10f1 (Built-in RP)
 Companion files `AGENTS.md` (agent entrypoint) · `BALL2_TASKS.md` (task register  tickets) · `Toolsrun-tests.sh` (verification harness)

This document is the single source of truth for Ball2. It serves two readers at once

1. Humans (Tom) — for design intent, decisions, and in-engine work.
2. Agents — as the authoritative context and task source for autonomousassisted implementation loops.

When design and code disagree, this document wins until it is amended. Amending it is part of the work, not separate from it.

---

## 0. How to use this document

### For agents

- Treat sections 1–5 as binding context. Read them before acting. (`AGENTS.md` is the short version of these rules.)
- Take work only from the Task Register — the live tickets live in `BALL2_TASKS.md`. Do not invent scope.
- Every task carries a Lane (§6.1). Respect the lane — it defines what done means and whether you are allowed to consider the task complete without a human.
- A task is complete only when its acceptance criteria are met and its verification command passes (§6.3). Compiling is not done. Looks right is not done.
- If you are blocked, looping without progress, or a `DEFERRED` item blocks you, escalate (§6.5) rather than guessing.
- When you change behaviour described in this doc, update the relevant section and the Changelog (§10) in the same change.

### For Tom

- Keep §7 (Roadmap) and the tickets in `BALL2_TASKS.md` current; they are the queue agents pull from.
- Resolve `DEFERRED` items in §9 when their milestone arrives — each unblocks dependent work.
- Convention used throughout
  - ` SETTLED (Qn)` — a decision that is made. Binding.
  - ` DEFERRED (Qn)` — a known decision intentionally postponed to a later milestone; not blocking now.
  - ` PROPOSED` — a default assumed so the doc is usable; override freely.
  - ` AGENT-NOTE` — implementation guidance aimed at workers.

---

## 1. Vision & Pillars

One line A physics hamster-ball aerial combat game — grapple, swing, boost, and collide. Think Beyblade meets a grappling-hook movement shooter, fought in the air.

### Design pillars (the north star — every feature is judged against these)

1. Momentum is the weapon. Damage and dominance come from speed, mass and angle at the moment of contact, not from a fire button. Skill = managing your own momentum and exploiting the enemy's.
2. The grapple is the verb. Movement, positioning and offence all flow through the dual grapple. It should feel expressive and high-skill-ceiling, never a cooldown to wait out.
3. Readable physicality. Outcomes are physically intuitive — a fast head-on is brutal, a glancing tap deflects. Players should predict collisions before they happen.
4. Easy to grasp, hard to master. A newcomer understands go fast, hit them in ten seconds. Mastery is years of grapple-swing-collision craft.

 SETTLED — non-goals (to keep scope honest) no character abilitiesloadout RPG layer at launch; no large-scale battle-royale counts; no in-engine level editor; no mobiletouch target initially.

Intent & scope ceiling (Q11) Portfolio piece + hobby + a fun, stupid game to play with friends. A distant, optional goal is a Steam release — which would likely require hiring artists3D modellers. So prioritise fun and feel over polish; keep infrastructure cheap; don't over-build for a commercial scale that may never come. Is this fun to play with mates this month beats is this market-ready.

---

## 2. Current state (as-built, June 2026)

Grounded in the repository, not aspiration.

 System  File  State 
---------
 Player movement  `AssetsScriptsBallMovement.cs` (268)  Reduced-gravity (0.4) ball with air strafe, multi-level boost charge, dodge + cooldown, particle FX hooks, third-person camera. Actively being tuned for feel. 
 Grapple  `AssetsScriptsGrappleSystem.cs` (507)  Dual independent hooks (LR mouse), physics rope (spring stiffnessdamping), reel (LShift), auto-aim w manual-aim radius, swing anti-gravity, release boost, anti-wall force. State machine Ready→Firing→Hooked→Returning. 
 Healthdamage  `AssetsScriptsHealthScript.cs` (111)  Integer health, regen timer, in-combat timer, 5 material states (intact→cracked→broken). `takeDmg()` currently parameterless — no momentum model yet. 
 Enemy AI  `AssetsScriptsEnemyAI.cs` (269)  Basic AI on Unity NavMesh (`com.unity.ai.navigation`). Single-target. 
 Camera  `CameraFollow.cs` (74)  Third-person follow. 
 Sprite  `SpriteFaceCamera.cs`, `SlowRotate.cs`  Billboarded 2D hamster sprite inside the 3D ball. 
 UI  Menu  `UIScript.cs` (123), `MainMenuScript.cs` (24)  Basic HUD + menu. 
 Input  `AssetsControlsControls.cs` (569, generated)  New Input System action map. 

Render pipeline Built-in RP (no URPHDRP package).
Multiplayer Not started. Only `com.unity.multiplayer.center` (the advisor tool) is installed — no NGOFishnetMirrorPhoton.
Tests None. `com.unity.test-framework 1.6.0` is present but unused. `Test``Test2` are dev scenes, not tests.
Assemblies Everything is in the default `Assembly-CSharp`. No `.asmdef` split.

 AGENT-NOTE The lack of an assembly split and the lack of tests are the two blockers to fast autonomous loops. §5.2 and §6 address both. Do not start gameplay-logic tasks until the `Ball2.Core` assembly and test harness exist (Milestone M0).

---

## 3. Game design

### 3.1 Core loop (match-level)

 PROPOSED
 Locate → Build momentum (boost + grapple-swing) → Engage (collision) → Resetreposition → repeat, until a win condition is met. Between contacts, players hunt for speed and angle advantage; the moment of contact resolves the exchange.

### 3.2 Combat resolution — the core unsolved system

This is the heart of the game and currently a stub. The model below is settled (Q1) and must resolve both ball-to-ball and ball-to-wall contacts.

 SETTLED (Q1) — momentum-differential model
 On collision between two balls, compute relative velocity along the contact normal and each ball's mass. The ball losing the exchange (lower normal momentum into the contact) takes damage and knockback proportional to the momentum differential; the winner keeps more of its speed.
 - Damage ∝ `clamp(relativeNormalSpeed × massRatioFactor)` above a threshold (light taps do nothing).
 - Knockback applied as an impulse; both balls are affected, asymmetrically.
 - Boost state and grapple-reel speed feed directly into the contact velocity, so the grapple is an offensive tool.
 - Optional perfect-hit window hitting within a small angletiming band of a dead-on, max-speed strike grants bonus damage + screenshake (rewards mastery).

 SETTLED (Q2) — health depletion in a closed box, no ring-out
 - No ring-out. All arenas are closed-box (§3.5) you cannot be knocked out of bounds or accidentally escape.
 - Win = last ball intact. Damage depletes health; the ball visibly cracks through `HealthScript`'s material states until it breaks.
 - Walls are weapons. Hitting a wall too hard deals self-damage above a speed threshold. Being knocked into a wall by an enemy deals two hits the enemy-impact damage, then a second wall-impact hit on landing. This is the beyblade slam them into the arena payoff without any ring-out — and it makes grapplingboosting an enemy into geometry (§3.3) a high-value combo.
 - The same momentum-differential maths drives ball-to-ball and ball-to-wall (a wall is effectively an immovable, infinite-mass body).

 AGENT-NOTE The resolution maths must live in `Ball2.Core` as a pure, deterministic function (`CombatResolver.Resolve(contact) - outcome`) covering both ball-ball and ball-wall contacts, with unit tests. The wall combo (enemy-impact then wall-impact) is two sequential resolves, not a special case. The MonoBehaviour collision callback only gathers inputs (velocities, masses, normals) and applies outputs (damage, impulse). This is what makes it Lane A (fully autonomous).

### 3.3 Movement & grapple (player feel)

Largely implemented; this section captures intended feel as the spec of record for tuning.

- Flight model reduced-gravity aerial control. Direct input gives strafingsteering; real speed comes from boost and grapple-swing, not from holding a direction.
- Grapple two hooks fired independently. Single hook = pendulum swing; double hook = tensioned positioningslingshot. Reel converts rope tension into speed. Release timing converts swing arc into launch velocity (the skill expression).
-  SETTLED (Q4) Grapple targets anything except yourself — geometry, obstacles, and other players. Aiming is fully manual; there is no auto-grapple to floorssurfaces, so the grapple stays a deliberate dodgingmanoeuvring tool rather than an assist. (Auto-aim assistance applies to firing accuracy, not to choosing targets for you.)
-  SETTLED (Q3) Grappling other players is core combat — yank them, tether-and-slam, or swing them into geometry. This is the natural fusion of pillars 1 and 2, and feeds the wall-combo (§3.2).

Offence vectors (three ways to convert movement into a hit)
1. Grapple-swing momentum — build speed on a swing and collide.
2. Grapple the enemy directly — tether and reelslam them, including into walls.
3. Boost lock-on strike (SETTLED, Q3Q13) — a SonicSuper Monkey Ball-style soft homing. Holdingcharging boost (Space) while aiming near or on an enemy acquires a soft lock, letting you boost into them with good accuracy. It is an aim assist on the boost, not a guaranteed hit; positioning and timing still decide the outcome, and it feeds straight into the contact velocity the resolver reads. Without a lock, boost behaves exactly as normal.
   - Range relatively short — lock-on only available at close range, not across the arena.
   - Acquisition only when the reticle is nearon a valid enemy and within range. A lock-on icon hovers over the enemy to telegraph that a lock is possibleactive.
   - Tracking soft — it tracks the target but not too hard; a skilled target can still slip the strike.
   - Breaking the lock breaks if the enemy leaves range or dodges out of it.
   - Commit target switching is disabled once the boost charge begins — you commit to the locked target when you start charging.

 AGENT-NOTE Lock-on acquisition + validity + rangeangle gating + break conditions + commit-on-charge are pure logic → `Ball2.Core` (`LockOnResolver`), Lane A, fully unit-testable. The reticleicon rendering and the boost impulse toward the locked target are thin `Ball2.Gameplay` adapters, Lane B. Tuning numbers (exact range, tracking strength, dodge tolerance) are Q12-class — parameterise them, don't hard-code.

### 3.4 Game modes

 SETTLED (Q5) — development priority FFADuel → Tag → CTF.

 PvP (primary)
 - Free-for-all (up to 8) — the first target mode. Casual chaos; everyone is a contested body.
 - Duel (1v1) — trivially an FFA lobby with a 2-player cap. Same systems, rankedcompetitive framing.
 - Tag (VIP  king-of-the-hill, second priority) — one player holds a buffcrown. Hitting the holder takes it and passes it to the striker. Designed to create chase-and-evade gameplay and test whether an objective improves the feel over pure deathmatch.
 - Capture the Flag (third priority) — team objective mode, to test whether the game plays better with a goal to fight over vs. pure combat.
 - Team (2v2  3v3) — falls out of CTFteam plumbing; later.

 PvE (secondary, mostly onboarding)
 - Tutorial — movement, grapple, collision basics (scripted, single-player; reuses `EnemyAI`).
 - TrainingPractice — free arena vs dummyAI for grapple drills.
 - Trials — optional skill challenges (timegrapple courses) that double as tutorialisation.

 AGENT-NOTE Build FFA as the general case with a lobby player-cap parameter; Duel is `cap = 2`, not a separate mode. Tag and CTF are objective layers on top of the same combatmovement core — keep their rules (crown ownership, flag state, scoring) in `Ball2.Core` as Lane A logic, independent of the physics.

### 3.5 Arena  level design

 SETTLED (Q6) Tall, open, vertical arenas with dense grapple obstacles, fully bounded as a closed box on all sides (including a ceiling and floor). The verticality and obstacle density give the grapple and boost somewhere to play; the bounds guarantee no ring-outs and no accidental escape (§3.2). Wallsobstacles double as combat surfaces via wall-impact damage. Design arenas as 3D volumes to fly through, not arenas to stand on.

### 3.6 Progression & meta

 PROPOSED defer. At first commercialportfolio-ready slice, ship cosmetic-only (ball skins, hamster sprites, trails). No stat-altering unlocks — preserve pillar 4 (pure skill). Revisit only if a meta hook is needed for retention.

### 3.7 Aesthetic  art direction

- Hamster-in-a-ball 3D physics sphere, billboarded 2D hamster sprite inside (as built).
- Damage = the ball visibly cracking (as built in `HealthScript`'s material states) — keep and lean into this; it is strong, readable, and on-pillar.
-  SETTLED (Q7) Colourful, loud, over-the-top, high-intensity.
   - Visual mood board Super Monkey Ball (toy-bright physics playfulness), Kirby Air Riders (saturated, kinetic), Wipeout (slick, fast, high-energy). Clean and vibrant, not gritty.
   - Movementcombat feel the ODM-gear swing-and-strike flow of Attack on Titan (esp. the RAOT fan game) — fast aerial grappling between points with momentum-based attacks; the parkourgrapple flow of Titanfall 2; and the unique high-speed movement of Tribes 3. The throughline is flow chaining grapples, boosts and swings into continuous high-speed motion.
   - HUDUI the cassette-futurism  NERV-terminal leaning can live in the HUD and menus as a stylistic contrast to the bright arenas — to confirm as UI work begins, but compatible with the above.

---

## 4. Technical architecture

### 4.1 Netcode (greenfield, highest-risk decision)

Physics-driven PvP is one of the hardest networking problems PhysX (Unity's physics) is not deterministic across machines, which rules out naive locksteprollback on raw PhysX.

 SETTLED (Q8) — server-authoritative simulation + client-side prediction & reconciliation, via Fishnet.
 This is the model real physicsaction games ship — Rocket League, Titanfall 2, Overwatch, Valorant. Rocket League specifically runs an authoritative server on a fixed physics tick, never re-simulates the past (it buffersrepeats inputs instead), and has clients predict locally with reconciliation; remote-player input is decayed over a few frames to avoid overshoot. It is not deterministic rollback — that (GGPO; fighting games like SF6Strive) needs bit-exact determinism, which Unity PhysX doesn't provide, especially with 8 colliding bodies.

 Why Fishnet specifically free, Unity 6-compatible (v4.7.1+), fixed-tick with physics simulated on the tick, ships `PredictionRigidbody` for this exact model, and — crucially for a contact-combat game — can predict collision EnterExit events via `NetworkCollision``NetworkTrigger` (by their own claim, the only Unity framework that does, ahead of Fusion). Your whole combat layer is collision events, so this matters.

 Caveat  scaling risk Rocket League is tuned around one contested object (the ball). Ball2's 8-player FFA makes every player a contested colliding body, which is harder to keep smooth. Mitigation the combat resolver is server-authoritative (`Ball2.Core`), so damage outcomes are decided correctly on contact even when a remote position is briefly approximate. Build Duel (2 players) first, scale the cap up, and find where it breaks before optimising. Escape hatch if 8-body prediction is unacceptable Photon Quantum (deterministic, paid, effectively a rewrite). Keeping combat maths in `Ball2.Core` keeps that door open without committing now.

 SETTLED (Q9) — PC desktop first, WebGL second, no platform off the table.
 WebGL is a degraded netcode path, not the core one no UDP, no threads, can't host, can't use the Steam transport — it would need a separate WebSocket transport and would feel worse. Treat a WebGL build as a try it in the browser convenience (good for your portfolioshareability), and do not let its constraints shape the desktop architecture.

 SETTLED (Q10) — listen-server over Steam relay now; dedicated headless servers later.
 Under an authoritative model, P2P really means a listen-server one player hosts and plays, and is the authority. Route it through Steam Datagram Relay using a Fishnet Steam transport (FishySteamworks  FishyFacepunch) free, NAT punch-through, hidden IPs, lobby invites, zero server cost — and it doubles as the Steam integration you'll want for an eventual release. Long term, Fishnet builds headless dedicated servers cleanly for competitive integrity; run those on your homelab first, cloud only if it ever matters. This matches the cheap now, dedicated eventually intent.

### 4.2 Assembly split (foundational — enables agent loops)

The current single-assembly layout means any change recompiles everything and almost nothing is unit-testable in isolation. Restructure into

```
Ball2.Core        (asmdef) — pure C#, no MonoBehaviour, minimalzero UnityEngine.
                  Combat resolution, damage model, abilitycooldown state machines,
                  matchmaking, scoring, match state. Deterministic. Fully unit-testable.
Ball2.Gameplay    (asmdef) — MonoBehaviour adapters. Wires Core to Rigidbody, Input,
                  collisions, FX. References Ball2.Core.
Ball2.Net         (asmdef) — networking layer (post-Q8).
Ball2.Tests.EditMode  (asmdef) — fast unit tests over Ball2.Core. Headless.
Ball2.Tests.PlayMode  (asmdef) — integration tests in-engine.
```

 AGENT-NOTE The ports and adapters boundary is the whole game from a looping standpoint. Logic that lives in `Ball2.Core` is Lane A (you can fully own it). Logic that touches `UnityEngine` types is at best Lane B. When given a logic task, push as much as possible into `Ball2.Core` behind a small interface and leave a thin adapter in `Ball2.Gameplay`.

### 4.3 Determinism boundary

Anything that may later feed networking (combat resolution, movement integration constants, RNG) must be deterministic and centralised fixed timestep, seeded RNG in `Ball2.Core`, no `UnityEngine.Random` or frame-rate-dependent maths in core logic. Flag violations in review.

---

## 5. Build foundations (Milestone M0 — do first)

These unlock everything else. Until they exist, agents run blind.

- M0.1 Create the asmdef split (§4.2). Move existing pure-ish logic into `Ball2.Core` where feasible; leave MonoBehaviours in `Ball2.Gameplay`.
- M0.2 Stand up the verification harness (§6.3) a headless test-run command that produces a machine-readable passfail.
- M0.3 Write the first canary EditMode test (a trivial `Ball2.Core` test) to prove the harness end-to-end.
- M0.4 Add a CI workflow (GitHub Actions, `game-ciunity-test-runner` or equivalent) so the same harness runs on every PR.

---

## 6. The agent loop protocol

This is the operational core — how looping actually works for a game that can't be fully tested headlessly. Built on the reason→act→observe→repeat model, with the human-in-the-loop boundary made explicit.

### 6.1 Task lanes (the humanagent boundary)

Every task is exactly one lane. The lane defines the definition of done and the autonomy level.

 Lane  Meaning  Agent autonomy  Done =  
------------
 🟢 A — Autonomous  Pure logic in `Ball2.Core` (or toolsscriptsCI). No `UnityEngine` runtime dependency.  Full reason-act-observe-verify loop. No human needed mid-loop.  Acceptance criteria met and EditMode tests + compile green via harness. 
 🟡 B — Assisted  Code that touches Unity runtime types but whose effect can't be proven headlessly (controller feel, FX, gameplay MonoBehaviours, netcode).  Agent writes code, makes it compile, adds whatever tests are possible, then opens a PR with a manual-verification checklist for Tom.  Compiles + available tests green + PR raised. Final sign-off is Tom's in-engine. 
 🔴 C — Human  in-engine  Scene wiring, prefabserialized-field assignment, Animator graphs, NavMesh bake, physics materials, art, audio, input-binding asset edits.  Agents may draft instructionschecklists, never implement.  Tom completes in-engine. 

 AGENT-NOTE If you find a 🟡 or 🔴 task that could be re-cut so the logic part becomes 🟢 (by extracting it into `Ball2.Core`), propose that split — it is almost always the right move and increases how much can be automated.

### 6.2 Task ticket schema

Every task in §8 uses this shape so the loop has a specific goal and a real termination condition.

```yaml
id B2-007
title Momentum-differential combat resolver
lane A
goal 
  Pure function resolving a two-ball collision into damage + knockback
  per the model in §3.2.
acceptance                 # phrased as testable assertions  test names
  - Head-on equal-mass equal-speed - both take equal damage, symmetric knockback.
  - Speed differential above threshold - faster ball wins, takes less damage.
  - Relative normal speed below threshold - zero damage (glancing tap).
  - Resolver is deterministic same inputs - identical outputs (seeded).
in_scope [Ball2.CoreCombatCombatResolver.cs, Ball2.Tests.EditModeCombatResolverTests.cs]
out_of_scope [collision detection wiring, FX, knockback application MonoBehaviour]
depends_on [B2-001 asmdef split, B2-002 test harness]
verify .Toolsrun-tests.sh EditMode Ball2.Core
escalate_if [§9 Q1Q2 still OPEN, threshold values undefined]
```

### 6.3 Verification harness (the tools that close the loop)

Agents need to observe real results, not guess. The harness provides that signal.

- Compile check — `Ball2.Core` should also build as a plain library where possible, giving a sub-second compile signal independent of Unity. Full Unity compile via batchmode for the rest.
- EditMode tests — run headless via Unity CLI
  `Unity -batchmode -nographics -runTests -testPlatform EditMode -testResults results.xml -projectPath . -quit`
  Wrap this in `Toolsrun-tests.sh` that parses the NUnit XML and exits non-zero on any failure, printing a structured summary (which tests, which assertion, not a raw log dump).
- PlayMode tests — same mechanism, `-testPlatform PlayMode`, for integration cases that can run without human input.
- What the harness cannot judge feel, visual correctness, anything requiring inspector wiring. Those are exactly the 🟡🔴 boundary — never let an agent self-certify them.

 AGENT-NOTE Treat the harness output as the only valid observe step for Lane A. A green compile with no passing test is not success.

### 6.4 Loop guardrails

- Budget cap iterationstool-calls per task (PROPOSED 8 edit-test cycles). Exhausting the budget without green tests is a failure signal → escalate, not try harder.
- No spinning never re-run an identical failed approach. If two consecutive attempts fail the same assertion, change strategy or escalate.
- Context hygiene keep a short running log of attemptsoutcomes per task; summarise before each new iteration rather than re-reading full history. (This maps cleanly onto your `conductor` worktree isolation — one task = one worktree = one clean context.)
- Scope lock touch only `in_scope` files. Out-of-scope changes require a new ticket.

### 6.5 Escalation paths

Escalate to Tom (don't guess) when an `OPEN` decision blocks the task; acceptance criteria are ambiguous or untestable; the task is actually 🟡🔴 mislabelled; budget exhausted; or a change would alter a design pillar or a networkeddeterministic system.

### 6.6 Execution model (current) and `conductor` (deferred)

Current execution a single agentic harness (e.g. Claude Code) with Tom in the loop. This project is, for now, too intertwined with the Unity engine and manual in-engine work to benefit from `conductor`'s multi-agent local-worker delegation — too much of the value is Lane BC that can't be delegated to a cheap worker. So the practical model is
- One agent works one ticket at a time against the harness (§6.3), on a branch.
- Lane A tickets run the full reason-act-observe-verify loop autonomously and open a PR.
- Lane B tickets compile + add what tests they can, then hand Tom a PR with an in-engine verification checklist.
- Lane C stays with Tom; agents may draft instructions only.

`conductor` is deferred, not rejected. It becomes worthwhile once `Ball2.Core` holds a substantial body of pure, headless-testable logic (combat, lock-on, scoring, matchmaking) — at that point those Lane A systems can be farmed to isolated-worktree local workers exactly as `conductor` is built for, while Unity-bound work stays human. Revisit at M2–M3 when Core has mass.

---

## 7. Roadmap (milestones)

 Coarse and living. Reorder freely. Each milestone should end at something playable or verifiable.

- M0 — Loop foundations asmdef split, test harness, CI, canary test. (blocks all Lane A work)
- M1 — Controller feel locked finalise movement + grapple tuning (Lane B, Tom-driven). Lock the verbs.
- M2 — Combat resolution momentum model in `Ball2.Core` + tests (Lane A), wired to collisions + cracking FX (Lane B). Single-player vs dummy.
- M3 — First playable PvE training arena + tutorial flow; AI opponent using resolved combat.
- M4 — Netcode spike Fishnet server-authoritative + prediction, listen-server over Steam relay, Duel (2-player) topology, two balls colliding and resolving combat correctly across the wire (collision EnterExit predicted).
- M5 — First playable Duel (1v1) the vertical slice. The real test of the whole game.
- M6 — Modes + polish FFATeam, cosmetics, onboarding, arenas.

---

## 8. Task Register

 The live queue agents pull from. Each entry follows §6.2. Seeded with M0M2 examples; expand as design firms up.

 The full, worker-ready tickets live in `BALL2_TASKS.md` (one expanded ticket per the §6.2 schema, with acceptance criteria, verify commands, and the asmdefCIharness contents). Summary queue below; `BALL2_TASKS.md` is authoritative.

- B2-001 · Lane A · Assembly split per §4.2. depends_on none. READY. (Foundational — blocks all Lane A.)
- B2-002 · Lane A · Verification harness `Toolsrun-tests.sh` + NUnit3 parse + structured summary. depends_on B2-001. READY after B2-001.
- B2-003 · Lane A · Canary EditMode test proving the harness end-to-end. depends_on B2-002.
- B2-004 · Lane A · CI running the harness on PRs (self-hosted-native primary; game-ci alt). depends_on B2-002.
- B2-007 · Lane A · Momentum-differential combat resolver (ball-ball + ball-wall). depends_on B2-003. UNBLOCKED (Q1Q2 settled).
- B2-010 · Lane A · Lock-on resolver (acquisitiongatingbreakcommit per §3.3). depends_on B2-003. UNBLOCKED (Q13 settled).
- B2-008 · Lane B · Wire resolver to collision callbacks + apply knockback + trigger crack FX. depends_on B2-007.
- B2-011 · Lane B · Reticlelock-on icon + boost impulse toward locked target. depends_on B2-010.

---

## 9. Decisions log

Resolved
- Q1 — Combat maths. Momentum-differential model (§3.2).
- Q2 — Win condition. Health depletion, closed-box, no ring-out, wall-impact damage + slam combo (§3.2).
- Q3 — Grapple players. Yes, core combat (§3.3).
- Q4 — Grappleable. Anything but self; manual aim only, no auto-floor-grapple (§3.3).
- Q5 — Modespriority. FFA(≤8)Duel → Tag → CTF (§3.4).
- Q6 — Arena. Tall, open, dense obstacles, fully bounded closed box (§3.5).
- Q7 — Visual tone. ColourfulloudOTT; SMBKirbyWipeout look, AoT-ODMTitanfall 2Tribes feel (§3.7).
- Q8 — Netcode. Server-authoritative + prediction via Fishnet (§4.1).
- Q9 — Platform. Desktop first, WebGL secondbackburner (§4.1).
- Q10 — Hosting. Listen-server over Steam relay now, dedicated later (§4.1).
- Q11 — Intent. Portfolio + hobby + play-with-friends; distant optional Steam goal (§1).
- Q13 — Lock-on gating. Short range; hover icon; aim nearon enemy; soft tracking; breaks on dodgeleaving range; no target-switch once charging; no lock = normal boost (§3.3).
- New mechanic added boost lock-on homing strike as a third offence vector (§3.3).

Deferred (not blocking; revisit at the noted point)
- Q12 — Tuning values. Damagewall thresholds, knockback scaling, perfect-hit window, lock-on rangetracking numbers — set empirically at M2. Owned by Tom (feel); agents expose the parameter surface, don't hard-code.
- Q14 — WebGL. On the backburner like any other port. Only pursue if it's not a near-full second implementation effort on top of desktop+Steam; if it costs roughly as much again, it's not worth it. Transport choice deferred with it.
- Q15 — Crownflag rules. Exact Tag buff and CTF scoringrespawn rules; specify as M6 approaches.

---

## 10. Changelog

- 2026-06-17 — v1.0 — First complete baseline. Hardened Q8Q10 to settled (Fishnet server-authoritative + prediction; listen-server over Steam relay → dedicated later). Settled Q13 lock-on gating (short range, hover icon, soft tracking, breaks on dodgerange, commit-on-charge, no-lock = normal boost). Q14 (WebGL) explicitly back-burnered. Reframed §6.6 direct agentic harness with Tom in the loop now; `conductor` deferred until `Ball2.Core` has mass. Split tickets into companion `BALL2_TASKS.md`; added `AGENTS.md` and `Toolsrun-tests.sh`. B2-007 and B2-010 unblocked.
- 2026-06-17 — v0.2 — Resolved Q1–Q7, Q9, Q11. Combat closed-box health depletion, no ring-out, added wall-impact damage + slam combo. Grapple targets anything but self, manual aim only; grappling players is core. Added boost lock-on homing strike as a third offence vector. Modes FFA(≤8)Duel → Tag → CTF. Arena tallopenbounded closed box. Visuals SMBKirbyWipeout look, AoT-ODMTitanfall 2Tribes feel. Netcode direction set (Q8Q10, pending confirm) Fishnet server-authoritative + prediction, listen-server over Steam relay → dedicated later; researched against Rocket League's model. Surfaced new open items Q12–Q15.
- 2026-06-17 — v0.1 — Initial draft. Grounded current-state audit from repo; proposed pillars, combat model, lane protocol, harness, and roadmap. 11 open decisions logged.