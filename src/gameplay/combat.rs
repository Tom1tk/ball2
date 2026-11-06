//! Collision-callback adapter ported from `CombatAdapter.cs`.
//!
//! One collision event resolves both participants. Ball-ball uses sphere centres
//! for the B->A normal; ball-wall uses Avian's contact manifold normal. Damage
//! and impulses are applied through the pure `CombatResolver`.

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
    mut balls: Query<
        (
            &Ball,
            &Transform,
            &mut Health,
            Forces,
            &PrePhysicsVelocity,
            &Mass,
        ),
        With<Ball>,
    >,
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

        let ball1 = balls.contains(body1);
        let ball2 = balls.contains(body2);
        if !ball1 && !ball2 {
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

        if ball1 && ball2 {
            if body1 == body2 {
                continue;
            }
            let Ok([a, b]) = balls.get_many_mut([body1, body2]) else {
                continue;
            };
            let (_, transform_a, mut health_a, mut forces_a, pre_a, mass_a) = a;
            let (_, transform_b, mut health_b, mut forces_b, pre_b, mass_b) = b;

            if health_a.dead && health_b.dead {
                continue;
            }

            let normal = (transform_a.translation - transform_b.translation).normalize_or_zero();
            if normal == Vec3::ZERO {
                continue;
            }

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

            if outcome.perfect_hit {
                stats.perfect_hit_flash = 0.35;
            }
            stats.last_damage = stats
                .last_damage
                .max(outcome.damage_a.max(outcome.damage_b));
        } else {
            // Exactly one ball; the other collider is treated as an immovable wall.
            let ball_entity = if ball1 { body1 } else { body2 };
            let ball_is_collider1 = ball_entity == collider1 || ball_entity == body1;

            let Ok((_, _transform, mut health, mut forces, pre, mass)) =
                balls.get_mut(ball_entity)
            else {
                continue;
            };

            if health.dead {
                continue;
            }

            // Contact manifold normal points collider1 -> collider2.
            let normal = collisions
                .get(collider1, collider2)
                .and_then(|pair| pair.manifolds.first())
                .map(|manifold| {
                    if ball_is_collider1 {
                        -manifold.normal
                    } else {
                        manifold.normal
                    }
                })
                .unwrap_or_else(|| -pre.0.normalize_or_zero());

            if normal == Vec3::ZERO {
                continue;
            }

            let input = ContactInput::new(pre.0, normal, mass.0, f32::INFINITY);
            let outcome = CombatResolver::resolve(&input, &cfg.0);

            if outcome.damage_a > 0.0 {
                health.take_dmg(outcome.damage_a);
            }
            if outcome.impulse_a != Vec3::ZERO {
                forces.apply_linear_impulse(outcome.impulse_a);
            }

            if outcome.perfect_hit {
                stats.perfect_hit_flash = 0.35;
            }
            stats.last_damage = stats.last_damage.max(outcome.damage_a);
        }
    }

    let _ = &collisions;
}
