using Godot;
using Ball2.Core.Combat;
using NVector3 = System.Numerics.Vector3;

/// <summary>
/// Collision-callback adapter for the momentum-differential combat resolver (B2-008).
/// Attached as a child of each ball body; the body script forwards contacts from
/// _IntegrateForces. On contact it gathers a ContactInput, calls CombatResolver.Resolve,
/// then applies damage + impulse to both participants.
/// </summary>
public partial class CombatAdapter : Node
{
    [ExportGroup("Tuning (Q12)")]
    [Export] public float DamageThresholdSpeed = 5f;
    [Export] public float DamagePerSpeed = 1f;
    [Export] public float KnockbackPerSpeed = 1f;
    [Export] public float PerfectHitMinSpeed = 15f;
    [Export] public float PerfectHitMaxAngleDeg = 10f;

    private RigidBody3D _body;
    private Health _health;
    private CombatConfig _config;

    public override void _Ready()
    {
        _body = GetParent<RigidBody3D>();
        _health = GetParent().GetNodeOrNull<Health>("Health");
        _config = new CombatConfig
        {
            DamageThresholdSpeed = DamageThresholdSpeed,
            DamagePerSpeed = DamagePerSpeed,
            KnockbackPerSpeed = KnockbackPerSpeed,
            PerfectHitMinSpeed = PerfectHitMinSpeed,
            PerfectHitMaxAngleDeg = PerfectHitMaxAngleDeg,
        };
    }

    public void ProcessContacts(PhysicsDirectBodyState3D state)
    {
        int contactCount = state.GetContactCount();
        if (contactCount == 0) return;

        // A ball-ball collision can report several contact points. Resolve each collider once.
        var seen = new Godot.Collections.Array<ulong>();

        for (int i = 0; i < contactCount; i++)
        {
            GodotObject otherObject = state.GetContactColliderObject(i);
            if (otherObject == null) continue;
            if (seen.Contains(otherObject.GetInstanceId())) continue;
            seen.Add(otherObject.GetInstanceId());

            RigidBody3D otherBody = otherObject as RigidBody3D;
            bool otherIsWall = otherBody == null;

            // Double-resolve guard: when two balls collide, both adapters see the event.
            // Only the body with the lower instance id applies outputs for the pair.
            if (!otherIsWall && _body.GetInstanceId() >= otherBody.GetInstanceId())
                continue;

            // Godot's contact point on this body lies on the surface toward the other
            // collider. The resolver's normal points B -> A, so aim it back at our centre.
            Vector3 normalBtoA = (_body.GlobalPosition - state.GetContactLocalPosition(i));
            if (normalBtoA.LengthSquared() < 1e-8f) continue;
            normalBtoA = normalBtoA.Normalized();

            Vector3 relativeVelocity = _body.LinearVelocity -
                (otherIsWall ? Vector3.Zero : otherBody.LinearVelocity);

            float massA = _body.Mass;
            float massB = otherIsWall ? float.PositiveInfinity : otherBody.Mass;

            var input = new ContactInput(ToNumerics(relativeVelocity), ToNumerics(normalBtoA), massA, massB);
            ContactOutcome outcome = CombatResolver.Resolve(in input, _config);

            // --- Apply outputs to self (A) ---
            if (outcome.DamageA > 0f)
                _health?.TakeDamage(outcome.DamageA);
            if (outcome.ImpulseA != NVector3.Zero)
                _body.ApplyCentralImpulse(ToGodot(outcome.ImpulseA));

            // --- Apply outputs to other (B) ---
            if (otherIsWall) continue;

            Health otherHealth = otherBody.GetNodeOrNull<Health>("Health");
            if (otherHealth != null && outcome.DamageB > 0f)
                otherHealth.TakeDamage(outcome.DamageB);
            if (outcome.ImpulseB != NVector3.Zero)
                otherBody.ApplyCentralImpulse(ToGodot(outcome.ImpulseB));
        }
    }

    private static NVector3 ToNumerics(Vector3 v) => new(v.X, v.Y, v.Z);
    private static Vector3 ToGodot(NVector3 v) => new(v.X, v.Y, v.Z);
}
