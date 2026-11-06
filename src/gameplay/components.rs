//! Shared ECS components/resources for the Ball2 gameplay layer.

use bevy::prelude::*;

#[derive(Component)]
pub struct GameWorld;

#[derive(Component)]
pub struct Player;

#[derive(Component)]
pub struct Enemy;

/// Marker for anything that uses the ball physics/combat/health model.
#[derive(Component)]
pub struct Ball;

#[derive(Component, Clone, Debug)]
pub struct Health {
    pub current: i32,
    pub max: i32,
    pub dead: bool,
    pub death_processed: bool,
    pub flash: f32,
    pub intact_color: Color,
    /// 0 = undamaged shell, 1..=3 = cracked variants, 4 = broken.
    pub damage_tier: usize,
}

impl Health {
    pub fn new(max: i32, intact_color: Color) -> Self {
        Self {
            current: max,
            max,
            dead: false,
            death_processed: false,
            flash: 0.0,
            intact_color,
            damage_tier: 0,
        }
    }

    /// Mirrors HealthScript.takeDmg(float): ceil the damage, clamp at zero.
    pub fn take_dmg(&mut self, damage: f32) -> i32 {
        if self.dead || damage <= 0.0 {
            return 0;
        }
        let amount = damage.ceil() as i32;
        self.current = (self.current - amount).max(0);
        self.flash = 0.15;
        self.damage_tier = damage_tier(self.current);
        if self.current == 0 {
            self.dead = true;
        }
        amount
    }

    pub fn heal(&mut self, amount: i32) {
        if self.dead {
            return;
        }
        self.current = (self.current + amount).min(self.max);
        self.flash = 0.1;
        self.damage_tier = damage_tier(self.current);
    }

    pub fn damage_color(&self) -> Color {
        if self.dead {
            return Color::srgb(0.08, 0.075, 0.09);
        }
        match self.max - self.current {
            0 => self.intact_color,
            1 => Color::srgb(1.0, 0.82, 0.12),
            2 => Color::srgb(1.0, 0.42, 0.05),
            _ => Color::srgb(1.0, 0.08, 0.05),
        }
    }
}

/// Unity `HealthScript.materialSwitch`: health value maps directly to a shell
/// material, independent of the maximum health.
fn damage_tier(health: i32) -> usize {
    match health {
        0 => 4,
        1 => 3,
        2 => 2,
        3 => 1,
        _ => 0,
    }
}

#[derive(Component)]
pub struct BallVisual {
    /// Single shell material; its texture is swapped between the five Unity
    /// health materials (BallTex, DMG1..3, broken).
    pub shell_material: Handle<StandardMaterial>,
    pub shell_textures: [Handle<Image>; 5],
    /// SimpleBlue/Green/Red are colour-only swaps in Unity.
    pub core_material: Handle<StandardMaterial>,
    pub charge_fx: Option<Entity>,
}

#[derive(Component, Clone, Copy, Debug, Default)]
pub struct BallState {
    pub speed: f32,
    pub charging: bool,
    pub grounded: bool,
}

#[derive(Component, Clone, Copy, Debug, Default)]
pub struct PrePhysicsVelocity(pub Vec3);

#[derive(Component)]
pub struct BallMovement {
    pub charging: bool,
    pub boost_lv: f32,
    pub dodge_timer: f32,
    pub grounded: bool,
    pub speed: f32,
}

impl Default for BallMovement {
    fn default() -> Self {
        Self {
            charging: false,
            boost_lv: 0.0,
            dodge_timer: 1.0,
            grounded: false,
            speed: 0.0,
        }
    }
}

#[derive(Component)]
pub struct ChargeFx;

#[derive(Component)]
pub struct Pickup;

#[derive(Component)]
pub struct Spin {
    pub radians_per_second: f32,
}

#[derive(Component)]
pub struct DespawnAfter {
    pub timer: Timer,
}

#[derive(Component)]
pub struct HookProjectile;

#[derive(Component)]
pub struct HookIndicator {
    pub is_left: bool,
}

/// World-space camera-facing ribbon replacing Unity's `LineRenderer`.
#[derive(Component)]
pub struct RopeVisual {
    pub is_left: bool,
}

#[derive(Resource, Default)]
pub struct WorldSpawned(pub bool);

#[derive(Resource)]
pub struct DebugSettings {
    pub show_debug_text: bool,
    pub show_aim_gizmos: bool,
}

impl Default for DebugSettings {
    fn default() -> Self {
        // Unity PlayerBall.prefab has displayDebugText = true.
        Self {
            show_debug_text: true,
            show_aim_gizmos: false,
        }
    }
}

#[derive(Resource)]
pub struct GameStats {
    pub enemies_alive: u32,
    pub enemies_max: u32,
    pub strawberries_collected: u32,
    pub strawberries_max: u32,
    pub last_damage: f32,
    pub speed_fx_timer: f32,
    pub dodge_fx_timer: f32,
}

impl Default for GameStats {
    fn default() -> Self {
        Self {
            enemies_alive: 0,
            enemies_max: 0,
            strawberries_collected: 0,
            strawberries_max: 0,
            last_damage: 0.0,
            speed_fx_timer: 0.0,
            dodge_fx_timer: 0.0,
        }
    }
}

#[derive(Component)]
pub struct CameraFollow {
    pub yaw: f32,
    pub pitch: f32,
    pub current_distance: f32,
    pub distance_velocity: f32,
    pub position_velocity: Vec3,
}

impl Default for CameraFollow {
    fn default() -> Self {
        // Test2.unity MainCamera euler angles: yaw 2.677 deg, pitch 7.380 deg.
        Self {
            yaw: 2.677_247_f32.to_radians(),
            pitch: 7.380_425_f32.to_radians(),
            current_distance: 10.0,
            distance_velocity: 0.0,
            position_velocity: Vec3::ZERO,
        }
    }
}
