//! Pure gameplay logic ported from `Assets/Scripts/Core`.
//!
//! These modules deliberately contain no Bevy ECS types. They are deterministic
//! functions over maths types, mirroring the Unity `Ball2.Core` assembly.

pub mod combat;
pub mod lockon;
