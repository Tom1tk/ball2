//! Collision-callback adapter ported from `CombatAdapter.cs`.
//!
//! Only ball-ball contacts are resolved into combat damage and knockback.
//! Static arena contacts are left entirely to Avian's physics response: in the
//! Unity project only opponent hits damaged the balls, and using the pre-step
//! velocity for walls would make every fast floor/wall landing lethal.

use std::collections::HashSet;

use avian3d::prelude::*;
use bevy::prelude::*;

use crate::core::combat::{CombatResolver, ContactInput};

use super::components::*;
use super::config::CombatSettings;

pub fn resolve_ball_collisions(
    mut events: MessageReader<CollisionStart>,
    collisions: Collisions,
    cfg: Res<CombatSettings>,
    mut stats: ResMut<GameStats>,
    mut balls: Query<(&Transform, &mut Health, Forces, &PrePhysicsVelocity, &Mass), With<Ball>>,
    pickups: Query<(), With<Pickup>>,
) {
    let mut seen: HashSet<(Entity, Entity)> = HashSet::new();

    for event in events.read() {
        let collider1 = event.collider1;
        let collider2 = event.collider2;
        let body1 = event.body1.unwrap_or(collider1);
        let body2 = event.body2.unwrap_or(collider2);

        if pickups.contains(body1) || pickups.contains(body2) {
            continue;
        }

        // Static arena contacts have only one ball body. Leave them to the
        // physics material and never apply combat damage: in the Unity project
        // only opponent collisions damaged the balls.
        let ball1 = balls.contains(body1);
        let ball2 = balls.contains(body2);
        if !ball1 || !ball2 {
            continue;
        }

        if body1 == body2 {
            continue;
        }

        let pair_key = if body1 < body2 {
            (body1, body2)
        } else {
            (body2, body1)
        };
        if !seen.insert(pair_key) {
            continue;
        }

        let Ok([a, b]) = balls.get_many_mut([body1, body2]) else {
            continue;
        };
        let (transform_a, mut health_a, mut forces_a, pre_a, mass_a) = a;
        let (transform_b, mut health_b, mut forces_b, pre_b, mass_b) = b;

        if health_a.dead || health_b.dead {
            continue;
        }

        // Prefer the actual contact normal; fall back to the sphere-centre
        // direction if the manifold is not available.
        let normal = collisions
            .get(collider1, collider2)
            .and_then(|pair| pair.manifolds.first())
            .map(|manifold| {
                if body1 == collider1 {
                    -manifold.normal
                } else {
                    manifold.normal
                }
            })
            .unwrap_or_else(|| {
                (transform_a.translation - transform_b.translation).normalize_or_zero()
            });
        if normal == Vec3::ZERO {
            continue;
        }

        // Use the velocities recorded at the start of this physics step so the
        // solver's separating post-impact velocities do not hide the hit.
        let input = ContactInput::new(pre_a.0 - pre_b.0, normal, mass_a.0, mass_b.0);
        let outcome = CombatResolver::resolve(&input, &cfg.0);

        if outcome.damage_a > 0.0 {
            health_a.take_dmg(outcome.damage_a);
        }
        if outcome.damage_b > 0.0 {
            health_b.take_dmg(outcome.damage_b);
        }
        if outcome.impulse_a != Vec3::ZERO {
            forces_a.apply_linear_impulse(outcome.impulse_a);
        }
        if outcome.impulse_b != Vec3::ZERO {
            forces_b.apply_linear_impulse(outcome.impulse_b);
        }
        stats.last_damage = stats
            .last_damage
            .max(outcome.damage_a.max(outcome.damage_b));
    }
}
