//! Shared ECS components/resources for the Ball2 gameplay layer.

use bevy::prelude::*;

#[derive(Component)]
pub struct GameWorld;

#[derive(Component)]
pub struct Player;

#[derive(Component)]
pub struct Enemy;

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Team {
    Player,
    Enemy,
}

#[derive(Component, Clone, Copy, Debug)]
pub struct Ball {
    pub team: Team,
}

#[derive(Component, Clone, Debug)]
pub struct Health {
    pub current: i32,
    pub max: i32,
    pub dead: bool,
    pub death_processed: bool,
    pub flash: f32,
    pub intact_color: Color,
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

    pub fn health_fraction(&self) -> f32 {
        if self.max <= 0 {
            0.0
        } else {
            self.current as f32 / self.max as f32
        }
    }
}

#[derive(Component)]
pub struct BallVisual {
    pub shell_material: Handle<StandardMaterial>,
    pub core_material: Handle<StandardMaterial>,
    pub charge_fx: Entity,
    pub charge_material: Handle<StandardMaterial>,
    pub core_radius: f32,
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
pub struct HookProjectile {
    pub owner: Entity,
    pub is_left: bool,
}

#[derive(Component)]
pub struct HookIndicator {
    pub is_left: bool,
}

#[derive(Resource, Default)]
pub struct WorldSpawned(pub bool);

#[derive(Resource, Default)]
pub struct DebugSettings {
    pub show_debug_text: bool,
    pub show_aim_gizmos: bool,
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Outcome {
    Victory,
    Defeat,
}

#[derive(Resource)]
pub struct GameStats {
    pub enemies_alive: u32,
    pub enemies_max: u32,
    pub strawberries_collected: u32,
    pub strawberries_max: u32,
    pub outcome: Option<Outcome>,
    pub last_damage: f32,
    pub perfect_hit_flash: f32,
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
            outcome: None,
            last_damage: 0.0,
            perfect_hit_flash: 0.0,
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
        Self {
            yaw: 0.0,
            pitch: 0.0,
            current_distance: 10.0,
            distance_velocity: 0.0,
            position_velocity: Vec3::ZERO,
        }
    }
}

#[derive(Resource)]
pub struct SpeedTrail {
    pub points: Vec<Vec3>,
    pub active: f32,
}

impl Default for SpeedTrail {
    fn default() -> Self {
        Self {
            points: Vec::new(),
            active: 0.0,
        }
    }
}
