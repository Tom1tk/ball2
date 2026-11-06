//! Momentum-differential combat resolver.
//!
//! Direct port of `Assets/Scripts/Core/Combat/CombatResolver.cs` (ticket B2-007).
//! Conventions are binding and match the Unity source:
//!
//! - `normal` is the unit contact normal pointing **B -> A**.
//! - `relative_velocity = vA - vB`.
//! - Closing speed is `vn = -dot(relative_velocity, normal)`; `vn <= 0` means separating.
//! - A wall is `mass_b = f32::INFINITY` (the symmetric `mass_a = f32::INFINITY` is also handled).

use bevy::math::Vec3;

/// Inputs describing a single contact, in ball A's frame relative to ball B.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct ContactInput {
    /// `vA - vB`.
    pub relative_velocity: Vec3,
    /// Unit contact normal pointing B -> A.
    pub normal: Vec3,
    pub mass_a: f32,
    /// `f32::INFINITY` for a wall.
    pub mass_b: f32,
}

impl ContactInput {
    pub const fn new(relative_velocity: Vec3, normal: Vec3, mass_a: f32, mass_b: f32) -> Self {
        Self {
            relative_velocity,
            normal,
            mass_a,
            mass_b,
        }
    }
}

/// Result of resolving a contact.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct ContactOutcome {
    pub damage_a: f32,
    pub damage_b: f32,
    pub impulse_a: Vec3,
    pub impulse_b: Vec3,
    pub perfect_hit: bool,
}

impl ContactOutcome {
    pub const ZERO: Self = Self {
        damage_a: 0.0,
        damage_b: 0.0,
        impulse_a: Vec3::ZERO,
        impulse_b: Vec3::ZERO,
        perfect_hit: false,
    };

    pub const fn new(
        damage_a: f32,
        damage_b: f32,
        impulse_a: Vec3,
        impulse_b: Vec3,
        perfect_hit: bool,
    ) -> Self {
        Self {
            damage_a,
            damage_b,
            impulse_a,
            impulse_b,
            perfect_hit,
        }
    }
}

/// All combat tuning. Defaults are the values serialized on the Unity prefabs
/// (Q12 placeholders — tune freely, do not bake magic numbers into the resolver).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct CombatConfig {
    /// Relative normal closing speed below this deals no damage and no impulse.
    pub damage_threshold_speed: f32,
    /// Damage per unit of excess closing speed above the threshold.
    pub damage_per_speed: f32,
    /// Impulse magnitude per unit of excess closing speed.
    pub knockback_per_speed: f32,
    /// A perfect hit requires at least this much closing speed.
    pub perfect_hit_min_speed: f32,
    /// A perfect hit requires the approach angle within this many degrees of dead-on.
    pub perfect_hit_max_angle_deg: f32,
}

impl Default for CombatConfig {
    fn default() -> Self {
        Self {
            damage_threshold_speed: 5.0,
            damage_per_speed: 1.0,
            knockback_per_speed: 1.0,
            perfect_hit_min_speed: 15.0,
            perfect_hit_max_angle_deg: 10.0,
        }
    }
}

/// Pure, deterministic momentum-differential resolver (spec section 3.2).
pub struct CombatResolver;

impl CombatResolver {
    /// Resolve a single ball-ball or ball-wall contact into damage + knockback.
    ///
    /// Pure: identical inputs yield bit-identical outputs across repeated calls.
    pub fn resolve(input: &ContactInput, cfg: &CombatConfig) -> ContactOutcome {
        let normal = input.normal.normalize_or_zero();
        if normal.length_squared() < 1e-12 {
            return ContactOutcome::ZERO;
        }

        let vn = -input.relative_velocity.dot(normal);
        let is_wall = input.mass_a.is_infinite() || input.mass_b.is_infinite();
        let perfect_hit = compute_perfect_hit(input.relative_velocity, normal, vn, cfg);

        if vn <= cfg.damage_threshold_speed {
            return ContactOutcome {
                perfect_hit,
                ..ContactOutcome::ZERO
            };
        }

        let excess = vn - cfg.damage_threshold_speed;

        if is_wall {
            let wall_damage = excess * cfg.damage_per_speed;
            let wall_impulse_mag = excess * cfg.knockback_per_speed;
            if input.mass_b.is_infinite() {
                // Normal points B -> A, so +normal pushes A away from the wall.
                return ContactOutcome::new(
                    wall_damage,
                    0.0,
                    normal * wall_impulse_mag,
                    Vec3::ZERO,
                    perfect_hit,
                );
            } else {
                // Wall is A, ball is B; -normal pushes B away from the wall.
                return ContactOutcome::new(
                    0.0,
                    wall_damage,
                    Vec3::ZERO,
                    -normal * wall_impulse_mag,
                    perfect_hit,
                );
            }
        }

        let sum = input.mass_a + input.mass_b;
        if sum <= 0.0 {
            return ContactOutcome {
                perfect_hit,
                ..ContactOutcome::ZERO
            };
        }

        // Inverse-mass split of the closing speed. Damage to a ball is proportional
        // to the opponent's share, so the faster/lighter ball deals more and takes less.
        let share_a = input.mass_b / sum;
        let share_b = input.mass_a / sum;

        let damage_a = share_b * excess * cfg.damage_per_speed;
        let damage_b = share_a * excess * cfg.damage_per_speed;

        let impulse_mag = excess * cfg.knockback_per_speed;
        let impulse_a = normal * impulse_mag;
        let impulse_b = -normal * impulse_mag;

        ContactOutcome::new(
            damage_a,
            damage_b,
            impulse_a,
            impulse_b,
            perfect_hit,
        )
    }
}

/// Perfect hit = at least `perfect_hit_min_speed` closing speed, and the approach
/// direction (`relative_velocity`) within `perfect_hit_max_angle_deg` of `-normal`.
fn compute_perfect_hit(
    relative_velocity: Vec3,
    normal: Vec3,
    vn: f32,
    cfg: &CombatConfig,
) -> bool {
    if vn < cfg.perfect_hit_min_speed {
        return false;
    }

    let rel_mag = relative_velocity.length();
    if rel_mag < 1e-6 {
        return false;
    }

    let approach_dir = -normal;
    let cos_a = (relative_velocity.dot(approach_dir) / rel_mag).clamp(-1.0, 1.0);
    let angle_deg = cos_a.acos().to_degrees();

    angle_deg <= cfg.perfect_hit_max_angle_deg
}

#[cfg(test)]
mod tests {
    use super::*;
    use bevy::math::{Quat, Vec3};

    const TOL: f32 = 1e-5;

    fn make_config(
        threshold: f32,
        dmg_per_speed: f32,
        kb_per_speed: f32,
        ph_min_speed: f32,
        ph_max_angle: f32,
    ) -> CombatConfig {
        CombatConfig {
            damage_threshold_speed: threshold,
            damage_per_speed: dmg_per_speed,
            knockback_per_speed: kb_per_speed,
            perfect_hit_min_speed: ph_min_speed,
            perfect_hit_max_angle_deg: ph_max_angle,
        }
    }

    fn default_config() -> CombatConfig {
        make_config(5.0, 1.0, 2.0, 15.0, 10.0)
    }

    fn assert_vec3_eq(a: Vec3, b: Vec3, tol: f32, msg: &str) {
        assert!(
            (a - b).abs().max_element() <= tol,
            "{msg}: {a:?} != {b:?}"
        );
    }

    #[test]
    fn below_threshold_glancing_tap_deals_no_damage_and_no_impulse() {
        let cfg = default_config();
        let normal = Vec3::NEG_X;
        let rel_vel = -normal * 3.0;
        let input = ContactInput::new(rel_vel, normal, 1.0, 1.0);
        let o = CombatResolver::resolve(&input, &cfg);

        assert_eq!(0.0, o.damage_a);
        assert_eq!(0.0, o.damage_b);
        assert_vec3_eq(Vec3::ZERO, o.impulse_a, 0.0, "ImpulseA zero");
        assert_vec3_eq(Vec3::ZERO, o.impulse_b, 0.0, "ImpulseB zero");
    }

    #[test]
    fn separating_contact_deals_no_damage_and_no_impulse() {
        let cfg = default_config();
        let normal = Vec3::NEG_X;
        let rel_vel = normal * 10.0;
        let input = ContactInput::new(rel_vel, normal, 1.0, 1.0);
        let o = CombatResolver::resolve(&input, &cfg);

        assert_eq!(0.0, o.damage_a);
        assert_eq!(0.0, o.damage_b);
        assert_vec3_eq(Vec3::ZERO, o.impulse_a, 0.0, "ImpulseA zero");
        assert_vec3_eq(Vec3::ZERO, o.impulse_b, 0.0, "ImpulseB zero");
    }

    #[test]
    fn head_on_equal_mass_equal_speed_both_take_equal_damage_symmetric_impulse() {
        let cfg = default_config();
        let normal = Vec3::NEG_X;
        let rel_vel = -normal * 20.0;
        let input = ContactInput::new(rel_vel, normal, 1.0, 1.0);
        let o = CombatResolver::resolve(&input, &cfg);

        let expected_damage = 0.5 * 15.0 * 1.0;
        assert!((expected_damage - o.damage_a).abs() <= TOL);
        assert!((expected_damage - o.damage_b).abs() <= TOL);
        assert!((o.damage_a - o.damage_b).abs() <= TOL);

        let expected_impulse_mag = 15.0 * 2.0;
        assert_vec3_eq(normal * expected_impulse_mag, o.impulse_a, TOL, "ImpulseA");
        assert_vec3_eq(-normal * expected_impulse_mag, o.impulse_b, TOL, "ImpulseB");
        assert_vec3_eq(o.impulse_a, -o.impulse_b, TOL, "Impulses equal/opposite");
    }

    #[test]
    fn unequal_mass_faster_lighter_ball_deals_more_and_takes_less() {
        let cfg = default_config();
        let normal = Vec3::NEG_X;
        let rel_vel = -normal * 20.0;
        let input = ContactInput::new(rel_vel, normal, 4.0, 1.0);
        let o = CombatResolver::resolve(&input, &cfg);

        let excess = 15.0;
        assert!((0.8 * excess - o.damage_a).abs() <= TOL);
        assert!((0.2 * excess - o.damage_b).abs() <= TOL);
        assert!(o.damage_a > o.damage_b);
    }

    #[test]
    fn unequal_mass_other_orientation_faster_lighter_ball_deals_more_and_takes_less() {
        let cfg = default_config();
        let normal = Vec3::NEG_X;
        let rel_vel = -normal * 20.0;
        let input = ContactInput::new(rel_vel, normal, 1.0, 4.0);
        let o = CombatResolver::resolve(&input, &cfg);

        let excess = 15.0;
        assert!((0.2 * excess - o.damage_a).abs() <= TOL);
        assert!((0.8 * excess - o.damage_b).abs() <= TOL);
        assert!(o.damage_b > o.damage_a);
    }

    #[test]
    fn ball_to_wall_wall_takes_no_damage_ball_damaged_by_own_speed_impulse_reflects() {
        let cfg = default_config();
        let normal = Vec3::NEG_X;
        let rel_vel = -normal * 20.0;
        let input = ContactInput::new(rel_vel, normal, 1.0, f32::INFINITY);
        let o = CombatResolver::resolve(&input, &cfg);

        assert_eq!(0.0, o.damage_b);
        assert_vec3_eq(Vec3::ZERO, o.impulse_b, 0.0, "Wall impulse zero");
        assert!((15.0 - o.damage_a).abs() <= TOL);
        assert!(o.damage_a.is_finite());
        assert!(o.damage_b.is_finite());
        assert!(o.impulse_a.x.is_finite());
        assert!(o.impulse_b.x.is_finite());
        assert_vec3_eq(normal * 30.0, o.impulse_a, TOL, "Ball reflects along +normal");
    }

    #[test]
    fn ball_to_wall_below_threshold_deals_no_damage() {
        let cfg = default_config();
        let normal = Vec3::NEG_X;
        let rel_vel = -normal * 3.0;
        let input = ContactInput::new(rel_vel, normal, 1.0, f32::INFINITY);
        let o = CombatResolver::resolve(&input, &cfg);

        assert_eq!(0.0, o.damage_a);
        assert_eq!(0.0, o.damage_b);
        assert_vec3_eq(Vec3::ZERO, o.impulse_a, 0.0, "ImpulseA zero");
        assert_vec3_eq(Vec3::ZERO, o.impulse_b, 0.0, "ImpulseB zero");
        assert!(o.damage_a.is_finite());
    }

    #[test]
    fn perfect_hit_dead_on_above_min_speed_is_true() {
        let cfg = default_config();
        let normal = Vec3::NEG_X;
        let rel_vel = -normal * 30.0;
        let input = ContactInput::new(rel_vel, normal, 1.0, 1.0);
        let o = CombatResolver::resolve(&input, &cfg);
        assert!(o.perfect_hit);
    }

    #[test]
    fn perfect_hit_just_inside_angle_window_is_true() {
        let cfg = default_config();
        let normal = Vec3::NEG_X;
        let rot = Quat::from_rotation_y(5f32.to_radians());
        let approach_dir = rot * (-normal);
        let rel_vel = approach_dir * 30.0;
        let input = ContactInput::new(rel_vel, normal, 1.0, 1.0);
        let o = CombatResolver::resolve(&input, &cfg);
        assert!(o.perfect_hit);
    }

    #[test]
    fn perfect_hit_just_outside_angle_window_is_false() {
        let cfg = default_config();
        let normal = Vec3::NEG_X;
        let rot = Quat::from_rotation_y(20f32.to_radians());
        let approach_dir = rot * (-normal);
        let rel_vel = approach_dir * 30.0;
        let input = ContactInput::new(rel_vel, normal, 1.0, 1.0);
        let o = CombatResolver::resolve(&input, &cfg);
        assert!(!o.perfect_hit);
    }

    #[test]
    fn perfect_hit_dead_on_but_below_min_speed_is_false() {
        let cfg = default_config();
        let normal = Vec3::NEG_X;
        let rel_vel = -normal * 10.0;
        let input = ContactInput::new(rel_vel, normal, 1.0, 1.0);
        let o = CombatResolver::resolve(&input, &cfg);
        assert!(!o.perfect_hit);
    }

    #[test]
    fn perfect_hit_wall_dead_on_fast_is_true() {
        let cfg = default_config();
        let normal = Vec3::NEG_X;
        let rel_vel = -normal * 40.0;
        let input = ContactInput::new(rel_vel, normal, 1.0, f32::INFINITY);
        let o = CombatResolver::resolve(&input, &cfg);
        assert!(o.perfect_hit);
        assert!(o.damage_a.is_finite());
    }

    #[test]
    fn determinism_identical_inputs_identical_outputs_across_repeated_calls() {
        let cfg = default_config();
        let normal = Vec3::NEG_X;
        let rel_vel = -normal * 30.0;
        let input = ContactInput::new(rel_vel, normal, 2.0, 3.0);

        let first = CombatResolver::resolve(&input, &cfg);
        for _ in 0..50 {
            let again = CombatResolver::resolve(&input, &cfg);
            assert_eq!(first.damage_a, again.damage_a);
            assert_eq!(first.damage_b, again.damage_b);
            assert_eq!(first.impulse_a, again.impulse_a);
            assert_eq!(first.impulse_b, again.impulse_b);
            assert_eq!(first.perfect_hit, again.perfect_hit);
        }
    }

    #[test]
    fn determinism_wall_contact_identical_outputs_across_repeated_calls() {
        let cfg = default_config();
        let normal = Vec3::Z;
        let rel_vel = -normal * 25.0;
        let input = ContactInput::new(rel_vel, normal, 1.5, f32::INFINITY);

        let first = CombatResolver::resolve(&input, &cfg);
        for _ in 0..50 {
            let again = CombatResolver::resolve(&input, &cfg);
            assert_eq!(first.damage_a, again.damage_a);
            assert_eq!(first.damage_b, again.damage_b);
            assert_eq!(first.impulse_a, again.impulse_a);
            assert_eq!(first.impulse_b, again.impulse_b);
            assert_eq!(first.perfect_hit, again.perfect_hit);
            assert!(again.damage_a.is_finite());
            assert!(again.damage_b.is_finite());
        }
    }

    #[test]
    fn symmetric_wall_as_a_is_handled_without_nan() {
        let cfg = default_config();
        let normal = Vec3::X; // B (ball) -> A (wall)
        let rel_vel = -normal * 20.0; // vA - vB, ball B moving toward the wall
        let input = ContactInput::new(rel_vel, normal, f32::INFINITY, 1.0);
        let o = CombatResolver::resolve(&input, &cfg);

        assert_eq!(0.0, o.damage_a);
        assert!((15.0 - o.damage_b).abs() <= TOL);
        assert_vec3_eq(-normal * 30.0, o.impulse_b, TOL, "B reflects away from wall");
        assert!(o.damage_b.is_finite());
    }

    #[test]
    fn zero_normal_returns_zero_outcome() {
        let cfg = default_config();
        let input = ContactInput::new(Vec3::ZERO, Vec3::ZERO, 1.0, 1.0);
        let o = CombatResolver::resolve(&input, &cfg);
        assert_eq!(o, ContactOutcome::ZERO);
    }
}
