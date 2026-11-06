//! Pure boost lock-on resolver.
//!
//! Direct port of `Assets/Scripts/Core/Combat/LockOnResolver.cs` (ticket B2-010).
//! Behaviour matches spec section 3.3 / Q13:
//!
//! - No qualifying candidate in range near the reticle -> no lock; boost is normal.
//! - Valid enemy near/on the reticle -> acquire, report icon for it.
//! - Soft tracking lerps the aim point toward the enemy by `tracking_strength`.
//! - Break when beyond `break_range` or aim drifts past `dodge_tolerance_deg`.
//! - Commit-on-charge: no target switching while `boost_charge_started`; break still applies.

use bevy::math::Vec3;

/// Stable identifier used for a target. `i32::MIN` means "no lock".
pub const NO_TARGET: i32 = i32::MIN;

/// One enemy the player could lock onto.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct LockOnCandidate {
    pub id: i32,
    pub position: Vec3,
    pub velocity: Vec3,
}

impl LockOnCandidate {
    pub const fn new(id: i32, position: Vec3, velocity: Vec3) -> Self {
        Self {
            id,
            position,
            velocity,
        }
    }
}

/// Lock state carried frame-to-frame.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct LockState {
    pub target_id: i32,
    pub tracking_position: Vec3,
}

impl LockState {
    pub const NONE: Self = Self {
        target_id: NO_TARGET,
        tracking_position: Vec3::ZERO,
    };

    pub const fn new(target_id: i32, tracking_position: Vec3) -> Self {
        Self {
            target_id,
            tracking_position,
        }
    }

    pub fn has_lock(&self) -> bool {
        self.target_id != NO_TARGET
    }
}

impl Default for LockState {
    fn default() -> Self {
        Self::NONE
    }
}

/// Everything the resolver needs for one tick.
#[derive(Clone, Debug)]
pub struct LockOnInput {
    pub player_position: Vec3,
    /// Assumed unit-length.
    pub aim_direction: Vec3,
    pub candidates: Vec<LockOnCandidate>,
    pub current_lock: LockState,
    pub boost_charge_started: bool,
}

impl LockOnInput {
    pub fn new(
        player_position: Vec3,
        aim_direction: Vec3,
        candidates: Vec<LockOnCandidate>,
        current_lock: LockState,
        boost_charge_started: bool,
    ) -> Self {
        Self {
            player_position,
            aim_direction,
            candidates,
            current_lock,
            boost_charge_started,
        }
    }
}

/// Result of a resolve tick.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct LockOnResult {
    pub new_lock: LockState,
    pub icon_target_id: i32,
    pub icon_should_show: bool,
}

impl LockOnResult {
    pub const fn none() -> Self {
        Self {
            new_lock: LockState::NONE,
            icon_target_id: -1,
            icon_should_show: false,
        }
    }

    pub const fn new(new_lock: LockState, icon_target_id: i32, icon_should_show: bool) -> Self {
        Self {
            new_lock,
            icon_target_id,
            icon_should_show,
        }
    }
}

/// All lock-on tuning. Defaults are the values serialized on `PlayerBall.prefab`
/// (Q12 placeholders — tune freely).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct LockOnConfig {
    /// Max distance from the player at which a lock can be acquired.
    pub range: f32,
    /// Max angle between the aim ray and the direction to an enemy.
    pub acquisition_angle_deg: f32,
    /// Soft-tracking lerp factor in [0, 1]. 0 = frozen, 1 = snap.
    pub tracking_strength: f32,
    /// Max aim drift from a locked target before the lock breaks.
    pub dodge_tolerance_deg: f32,
    /// Distance beyond which the lock breaks.
    pub break_range: f32,
    /// Floating-point comparison epsilon.
    pub epsilon: f32,
}

impl Default for LockOnConfig {
    fn default() -> Self {
        Self {
            // PlayerBall.prefab LockOnAdapter overrides.
            range: 20.0,
            acquisition_angle_deg: 60.0,
            tracking_strength: 0.35,
            dodge_tolerance_deg: 45.0,
            break_range: 30.0,
            epsilon: 1e-5,
        }
    }
}

/// Angle (degrees) below which two directions are treated as identical.
const AIM_EPSILON_DEG: f32 = 0.0001;

/// Pure, deterministic lock-on resolver.
pub struct LockOnResolver;

impl LockOnResolver {
    pub fn resolve(input: &LockOnInput, cfg: &LockOnConfig) -> LockOnResult {
        if input.candidates.is_empty() {
            return LockOnResult::none();
        }

        let aim = input.aim_direction;
        if !is_unit_length(aim, cfg.epsilon) {
            return LockOnResult::none();
        }

        if input.current_lock.has_lock() {
            if let Some(locked) = find_candidate(&input.candidates, input.current_lock.target_id) {
                let dist = input.player_position.distance(locked.position);
                let to_enemy = locked.position - input.player_position;
                let aim_angle_deg = angle_deg(aim, to_enemy);

                if dist > cfg.break_range + cfg.epsilon
                    || aim_angle_deg > cfg.dodge_tolerance_deg + AIM_EPSILON_DEG
                {
                    if input.boost_charge_started {
                        return LockOnResult::none();
                    }
                    return acquire_best(input, cfg);
                }

                let new_tracking = input
                    .current_lock
                    .tracking_position
                    .lerp(locked.position, cfg.tracking_strength);
                let held = LockState::new(locked.id, new_tracking);
                return LockOnResult::new(held, locked.id, true);
            }

            // Locked enemy disappeared from the candidate list.
            if input.boost_charge_started {
                return LockOnResult::none();
            }
            return acquire_best(input, cfg);
        }

        acquire_best(input, cfg)
    }
}

/// Pick the in-range candidate closest to the reticle centreline (smallest aim angle),
/// tie-broken by nearest distance.
fn acquire_best(input: &LockOnInput, cfg: &LockOnConfig) -> LockOnResult {
    let aim = input.aim_direction;
    let mut best_score = f32::MAX;
    let mut best_dist = f32::MAX;
    let mut best: Option<LockOnCandidate> = None;

    for &candidate in &input.candidates {
        let dist = input.player_position.distance(candidate.position);
        if dist > cfg.range + cfg.epsilon {
            continue;
        }

        let to_enemy = candidate.position - input.player_position;
        if to_enemy.length_squared() <= cfg.epsilon * cfg.epsilon {
            continue;
        }

        let angle = angle_deg(aim, to_enemy);
        if angle > cfg.acquisition_angle_deg + AIM_EPSILON_DEG {
            continue;
        }

        let better = match best {
            None => true,
            Some(_) => {
                if angle < best_score - AIM_EPSILON_DEG {
                    true
                } else {
                    (angle - best_score).abs() <= AIM_EPSILON_DEG && dist < best_dist
                }
            }
        };

        if better {
            best_score = angle;
            best_dist = dist;
            best = Some(candidate);
        }
    }

    match best {
        None => LockOnResult::none(),
        Some(candidate) => {
            // Seed tracking at the enemy's current position on a fresh acquisition.
            let acquired = LockState::new(candidate.id, candidate.position);
            LockOnResult::new(acquired, candidate.id, true)
        }
    }
}

fn find_candidate(candidates: &[LockOnCandidate], id: i32) -> Option<LockOnCandidate> {
    candidates.iter().copied().find(|c| c.id == id)
}

fn is_unit_length(v: Vec3, eps: f32) -> bool {
    let mag = v.length();
    mag > eps && (mag - 1.0).abs() <= 0.01
}

/// Angle in degrees between two vectors; 90 if either is degenerate.
fn angle_deg(a: Vec3, b: Vec3) -> f32 {
    let denom = (a.length_squared() * b.length_squared()).sqrt();
    if denom <= 0.0 {
        return 90.0;
    }
    a.angle_between(b).to_degrees()
}

#[cfg(test)]
mod tests {
    use super::*;
    use bevy::math::Vec3;

    const AIM_FORWARD: Vec3 = Vec3::Z;

    fn results_equal(a: &LockOnResult, b: &LockOnResult) -> bool {
        a.new_lock.target_id == b.new_lock.target_id
            && a.icon_target_id == b.icon_target_id
            && a.icon_should_show == b.icon_should_show
            && a.new_lock.tracking_position == b.new_lock.tracking_position
    }

    #[test]
    fn no_candidates_no_lock_boost_normal() {
        let cfg = LockOnConfig::default();
        let input = LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![], LockState::NONE, false);
        let result = LockOnResolver::resolve(&input, &cfg);

        assert!(!result.new_lock.has_lock());
        assert_eq!(NO_TARGET, result.new_lock.target_id);
        assert!(!result.icon_should_show);
        assert_eq!(-1, result.icon_target_id);
    }

    #[test]
    fn enemy_out_of_range_no_lock() {
        let cfg = LockOnConfig {
            range: 15.0,
            ..Default::default()
        };
        let enemy = LockOnCandidate::new(1, Vec3::new(0.0, 0.0, 30.0), Vec3::ZERO);
        let input = LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![enemy], LockState::NONE, false);
        let result = LockOnResolver::resolve(&input, &cfg);

        assert!(!result.new_lock.has_lock());
        assert!(!result.icon_should_show);
    }

    #[test]
    fn enemy_off_reticle_no_lock() {
        let cfg = LockOnConfig::default();
        let enemy = LockOnCandidate::new(1, Vec3::new(10.0, 0.0, 0.0), Vec3::ZERO);
        let input = LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![enemy], LockState::NONE, false);
        let result = LockOnResolver::resolve(&input, &cfg);

        assert!(!result.new_lock.has_lock());
        assert!(!result.icon_should_show);
    }

    #[test]
    fn enemy_on_reticle_in_range_acquires_lock_icon_shows() {
        let cfg = LockOnConfig::default();
        let enemy = LockOnCandidate::new(7, Vec3::new(0.0, 0.0, 10.0), Vec3::ZERO);
        let input = LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![enemy], LockState::NONE, false);
        let result = LockOnResolver::resolve(&input, &cfg);

        assert!(result.new_lock.has_lock());
        assert_eq!(7, result.new_lock.target_id);
        assert!(result.icon_should_show);
        assert_eq!(7, result.icon_target_id);
    }

    #[test]
    fn enemy_near_reticle_within_acquisition_angle_acquires() {
        let cfg = LockOnConfig::default();
        let enemy = LockOnCandidate::new(2, Vec3::new(3.0, 0.0, 11.83), Vec3::ZERO);
        let input = LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![enemy], LockState::NONE, false);
        let result = LockOnResolver::resolve(&input, &cfg);

        assert!(result.new_lock.has_lock());
        assert_eq!(2, result.new_lock.target_id);
        assert!(result.icon_should_show);
    }

    #[test]
    fn multiple_candidates_picks_closest_to_reticle_centre() {
        let cfg = LockOnConfig::default();
        let a = LockOnCandidate::new(1, Vec3::new(0.87, 0.0, 10.0), Vec3::ZERO);
        let b = LockOnCandidate::new(2, Vec3::new(0.35, 0.0, 10.0), Vec3::ZERO);
        let input = LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![a, b], LockState::NONE, false);
        let result = LockOnResolver::resolve(&input, &cfg);

        assert_eq!(2, result.new_lock.target_id);
    }

    #[test]
    fn soft_tracking_lock_retained_and_does_not_snap_perfectly() {
        let cfg = LockOnConfig::default();
        let enemy = LockOnCandidate::new(1, Vec3::new(0.0, 0.0, 10.0), Vec3::ZERO);
        let acquired = LockOnResolver::resolve(
            &LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![enemy], LockState::NONE, false),
            &cfg,
        );
        assert!(acquired.new_lock.has_lock());
        assert_eq!(
            Vec3::new(0.0, 0.0, 10.0),
            acquired.new_lock.tracking_position
        );

        let moved = LockOnCandidate::new(1, Vec3::new(1.0, 0.0, 10.0), Vec3::ZERO);
        let tracked = LockOnResolver::resolve(
            &LockOnInput::new(
                Vec3::ZERO,
                AIM_FORWARD,
                vec![moved],
                acquired.new_lock,
                false,
            ),
            &cfg,
        );

        assert!(tracked.new_lock.has_lock());
        assert_eq!(1, tracked.new_lock.target_id);
        let tp = tracked.new_lock.tracking_position;
        assert!((0.35 - tp.x).abs() <= 1e-4);
        assert!((10.0 - tp.z).abs() <= 1e-4);
        assert_ne!(moved.position, tp);
    }

    #[test]
    fn soft_tracking_strength_zero_frozen_aim_point() {
        let mut cfg = LockOnConfig::default();
        cfg.tracking_strength = 0.0;
        let enemy = LockOnCandidate::new(1, Vec3::new(0.0, 0.0, 10.0), Vec3::ZERO);
        let acquired = LockOnResolver::resolve(
            &LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![enemy], LockState::NONE, false),
            &cfg,
        );

        let moved = LockOnCandidate::new(1, Vec3::new(5.0, 0.0, 10.0), Vec3::ZERO);
        let tracked = LockOnResolver::resolve(
            &LockOnInput::new(
                Vec3::ZERO,
                AIM_FORWARD,
                vec![moved],
                acquired.new_lock,
                false,
            ),
            &cfg,
        );

        assert!(tracked.new_lock.has_lock());
        assert_eq!(
            Vec3::new(0.0, 0.0, 10.0),
            tracked.new_lock.tracking_position
        );
    }

    #[test]
    fn soft_tracking_strength_one_snaps_to_enemy() {
        let mut cfg = LockOnConfig::default();
        cfg.tracking_strength = 1.0;
        let enemy = LockOnCandidate::new(1, Vec3::new(0.0, 0.0, 10.0), Vec3::ZERO);
        let acquired = LockOnResolver::resolve(
            &LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![enemy], LockState::NONE, false),
            &cfg,
        );

        let moved = LockOnCandidate::new(1, Vec3::new(5.0, 0.0, 10.0), Vec3::ZERO);
        let tracked = LockOnResolver::resolve(
            &LockOnInput::new(
                Vec3::ZERO,
                AIM_FORWARD,
                vec![moved],
                acquired.new_lock,
                false,
            ),
            &cfg,
        );

        assert_eq!(
            Vec3::new(5.0, 0.0, 10.0),
            tracked.new_lock.tracking_position
        );
    }

    #[test]
    fn lock_breaks_enemy_leaves_range() {
        let cfg = LockOnConfig::default();
        let enemy = LockOnCandidate::new(1, Vec3::new(0.0, 0.0, 10.0), Vec3::ZERO);
        let acquired = LockOnResolver::resolve(
            &LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![enemy], LockState::NONE, false),
            &cfg,
        );
        assert!(acquired.new_lock.has_lock());

        let cfg2 = LockOnConfig {
            range: 20.0,
            break_range: 15.0,
            ..Default::default()
        };
        let far = LockOnCandidate::new(1, Vec3::new(0.0, 0.0, 30.0), Vec3::ZERO);
        let result = LockOnResolver::resolve(
            &LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![far], acquired.new_lock, false),
            &cfg2,
        );

        assert!(!result.new_lock.has_lock());
        assert!(!result.icon_should_show);
    }

    #[test]
    fn lock_breaks_enemy_dodges_outside_tolerance() {
        let cfg = LockOnConfig {
            dodge_tolerance_deg: 45.0,
            ..Default::default()
        };
        let enemy = LockOnCandidate::new(1, Vec3::new(0.0, 0.0, 10.0), Vec3::ZERO);
        let acquired = LockOnResolver::resolve(
            &LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![enemy], LockState::NONE, false),
            &cfg,
        );
        assert!(acquired.new_lock.has_lock());

        let dodged = LockOnCandidate::new(1, Vec3::new(10.0, 0.0, 0.0), Vec3::ZERO);
        let result = LockOnResolver::resolve(
            &LockOnInput::new(
                Vec3::ZERO,
                AIM_FORWARD,
                vec![dodged],
                acquired.new_lock,
                false,
            ),
            &cfg,
        );

        assert!(!result.new_lock.has_lock());
        assert!(!result.icon_should_show);
    }

    #[test]
    fn lock_holds_enemy_stays_within_dodge_tolerance() {
        let cfg = LockOnConfig::default();
        let enemy = LockOnCandidate::new(1, Vec3::new(0.0, 0.0, 10.0), Vec3::ZERO);
        let acquired = LockOnResolver::resolve(
            &LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![enemy], LockState::NONE, false),
            &cfg,
        );

        let shifted = LockOnCandidate::new(1, Vec3::new(5.77, 0.0, 10.0), Vec3::ZERO);
        let result = LockOnResolver::resolve(
            &LockOnInput::new(
                Vec3::ZERO,
                AIM_FORWARD,
                vec![shifted],
                acquired.new_lock,
                false,
            ),
            &cfg,
        );

        assert!(result.new_lock.has_lock());
    }

    #[test]
    fn lock_breaks_break_range_larger_than_acquisition_range() {
        let cfg = LockOnConfig {
            range: 20.0,
            break_range: 25.0,
            ..Default::default()
        };
        let enemy = LockOnCandidate::new(1, Vec3::new(0.0, 0.0, 10.0), Vec3::ZERO);
        let acquired = LockOnResolver::resolve(
            &LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![enemy], LockState::NONE, false),
            &cfg,
        );

        let at22 = LockOnCandidate::new(1, Vec3::new(0.0, 0.0, 22.0), Vec3::ZERO);
        let hold = LockOnResolver::resolve(
            &LockOnInput::new(
                Vec3::ZERO,
                AIM_FORWARD,
                vec![at22],
                acquired.new_lock,
                false,
            ),
            &cfg,
        );
        assert!(hold.new_lock.has_lock());

        let at27 = LockOnCandidate::new(1, Vec3::new(0.0, 0.0, 27.0), Vec3::ZERO);
        let broken = LockOnResolver::resolve(
            &LockOnInput::new(
                Vec3::ZERO,
                AIM_FORWARD,
                vec![at27],
                acquired.new_lock,
                false,
            ),
            &cfg,
        );
        assert!(!broken.new_lock.has_lock());
    }

    #[test]
    fn commit_on_charge_does_not_switch_to_better_candidate() {
        let cfg = LockOnConfig::default();
        let a = LockOnCandidate::new(1, Vec3::new(0.87, 0.0, 10.0), Vec3::ZERO);
        let acquired = LockOnResolver::resolve(
            &LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![a], LockState::NONE, false),
            &cfg,
        );
        assert_eq!(1, acquired.new_lock.target_id);

        let b = LockOnCandidate::new(2, Vec3::new(0.0, 0.0, 10.0), Vec3::ZERO);
        let result = LockOnResolver::resolve(
            &LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![a, b], acquired.new_lock, true),
            &cfg,
        );

        assert_eq!(1, result.new_lock.target_id);
        assert!(result.icon_should_show);
    }

    #[test]
    fn commit_on_charge_break_conditions_still_apply() {
        let cfg = LockOnConfig {
            break_range: 15.0,
            ..Default::default()
        };
        let a = LockOnCandidate::new(1, Vec3::new(0.0, 0.0, 10.0), Vec3::ZERO);
        let acquired = LockOnResolver::resolve(
            &LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![a], LockState::NONE, false),
            &cfg,
        );

        let a_far = LockOnCandidate::new(1, Vec3::new(0.0, 0.0, 30.0), Vec3::ZERO);
        let b = LockOnCandidate::new(2, Vec3::new(0.0, 0.0, 10.0), Vec3::ZERO);
        let result = LockOnResolver::resolve(
            &LockOnInput::new(
                Vec3::ZERO,
                AIM_FORWARD,
                vec![a_far, b],
                acquired.new_lock,
                true,
            ),
            &cfg,
        );

        assert!(!result.new_lock.has_lock());
        assert!(!result.icon_should_show);
    }

    #[test]
    fn commit_on_charge_not_charging_can_switch_to_better_candidate() {
        let cfg = LockOnConfig {
            dodge_tolerance_deg: 45.0,
            ..Default::default()
        };
        let a = LockOnCandidate::new(1, Vec3::new(0.87, 0.0, 10.0), Vec3::ZERO);
        let acquired = LockOnResolver::resolve(
            &LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![a], LockState::NONE, false),
            &cfg,
        );

        let a_dodged = LockOnCandidate::new(1, Vec3::new(10.0, 0.0, 0.0), Vec3::ZERO);
        let b = LockOnCandidate::new(2, Vec3::new(0.0, 0.0, 10.0), Vec3::ZERO);
        let result = LockOnResolver::resolve(
            &LockOnInput::new(
                Vec3::ZERO,
                AIM_FORWARD,
                vec![a_dodged, b],
                acquired.new_lock,
                false,
            ),
            &cfg,
        );

        assert_eq!(2, result.new_lock.target_id);
    }

    #[test]
    fn determinism_identical_inputs_produce_identical_outputs() {
        let cfg = LockOnConfig::default();
        let enemy = LockOnCandidate::new(1, Vec3::new(0.5, -0.3, 12.0), Vec3::new(0.1, 0.0, 0.0));
        let input = LockOnInput::new(
            Vec3::new(1.0, 2.0, 3.0),
            AIM_FORWARD,
            vec![enemy],
            LockState::NONE,
            false,
        );

        let r1 = LockOnResolver::resolve(&input, &cfg);
        let r2 = LockOnResolver::resolve(&input, &cfg);
        let r3 = LockOnResolver::resolve(&input, &cfg);

        assert!(results_equal(&r1, &r2));
        assert!(results_equal(&r2, &r3));
    }

    #[test]
    fn determinism_tracking_repeated_produces_identical_outputs() {
        let cfg = LockOnConfig::default();
        let enemy = LockOnCandidate::new(1, Vec3::new(0.0, 0.0, 10.0), Vec3::ZERO);
        let acquired = LockOnResolver::resolve(
            &LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![enemy], LockState::NONE, false),
            &cfg,
        );

        let moved = LockOnCandidate::new(1, Vec3::new(1.0, 0.0, 10.0), Vec3::ZERO);
        let input = LockOnInput::new(
            Vec3::ZERO,
            AIM_FORWARD,
            vec![moved],
            acquired.new_lock,
            false,
        );

        let r1 = LockOnResolver::resolve(&input, &cfg);
        let r2 = LockOnResolver::resolve(&input, &cfg);
        let r3 = LockOnResolver::resolve(&input, &cfg);

        assert!(results_equal(&r1, &r2));
        assert!(results_equal(&r2, &r3));
    }

    #[test]
    fn determinism_no_frame_or_time_dependence() {
        let cfg = LockOnConfig::default();
        let enemy = LockOnCandidate::new(42, Vec3::new(0.0, 1.0, 9.0), Vec3::ZERO);
        let input = LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![enemy], LockState::NONE, false);

        let first = LockOnResolver::resolve(&input, &cfg);
        for i in 0..1000 {
            let r = LockOnResolver::resolve(&input, &cfg);
            assert!(results_equal(&first, &r), "result drifted at iteration {i}");
        }
    }

    #[test]
    fn zero_aim_direction_no_lock() {
        let cfg = LockOnConfig::default();
        let enemy = LockOnCandidate::new(1, Vec3::new(0.0, 0.0, 10.0), Vec3::ZERO);
        let input = LockOnInput::new(Vec3::ZERO, Vec3::ZERO, vec![enemy], LockState::NONE, false);
        let result = LockOnResolver::resolve(&input, &cfg);

        assert!(!result.new_lock.has_lock());
    }

    #[test]
    fn enemy_on_top_of_player_not_acquired() {
        let cfg = LockOnConfig::default();
        let enemy = LockOnCandidate::new(1, Vec3::ZERO, Vec3::ZERO);
        let input = LockOnInput::new(Vec3::ZERO, AIM_FORWARD, vec![enemy], LockState::NONE, false);
        let result = LockOnResolver::resolve(&input, &cfg);

        assert!(!result.new_lock.has_lock());
    }
}
