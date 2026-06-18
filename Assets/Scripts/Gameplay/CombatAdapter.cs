using UnityEngine;
using Ball2.Core.Combat;

namespace Ball2.Gameplay
{
    /// <summary>
    /// Collision-callback adapter for the momentum-differential combat resolver (B2-008).
    /// Attach to the player ball and to each enemy ball. On contact, gathers a
    /// <see cref="ContactInput"/>, calls <see cref="CombatResolver.Resolve"/>, then
    /// applies the resulting damage + impulse to both participants.
    /// </summary>
    /// <remarks>
    /// <b>Contract honoured (binding on the resolver).</b>
    /// <list type="bullet">
    /// <item>Normal points B→A. Unity's <c>Collision.GetContact(0).normal</c> points A→B,
    /// so we negate it before handing it to the resolver.</item>
    /// <item>RelativeVelocity = vA - vB. If B is a wall (no Rigidbody), vB = 0.</item>
    /// <item>Wall = <c>MassB = float.PositiveInfinity</c>. We use the absence of a
    /// Rigidbody on the other collider as the wall signal.</item>
    /// </list>
    /// <b>Double-resolve guard.</b> When two balls collide, <c>OnCollisionEnter</c> fires
    /// on both. Both adapters compute the same Resolve. We prevent double-damage by only
    /// applying outputs from the side with the lower <c>GetInstanceID()</c>.
    /// <para>
    /// <b>Wall combo.</b> The "enemy impact then wall impact" combo emerges naturally
    /// from two sequential collision events (ball-ball then ball-wall) — no special
    /// sequencing needed in the adapter.
    /// </para>
    /// </remarks>
    public sealed class CombatAdapter : MonoBehaviour
    {
        [SerializeField] CombatConfig config;
        [SerializeField] Rigidbody body;
        [SerializeField] HealthScript health;

        void Reset()
        {
            body = GetComponent<Rigidbody>();
            health = GetComponent<HealthScript>();
        }

        void OnValidate()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            if (health == null) health = GetComponent<HealthScript>();
        }

        void OnCollisionEnter(Collision collision)
        {
            if (config == null) return;
            if (body == null) return;

            Rigidbody otherBody = collision.rigidbody;
            bool otherIsWall = otherBody == null;

            // Double-resolve guard: only the lower GetInstanceID() applies outputs.
            // The two adapters see the same collision; one of them must no-op.
            if (!otherIsWall && GetInstanceID() >= collision.gameObject.GetInstanceID())
                return;

            // Wall = infinite mass. Adapter's own ball is "A" so the wall is "B".
            float massA = body.mass;
            float massB = otherIsWall ? float.PositiveInfinity : otherBody.mass;

            // Unity contact normal points A→B; resolver expects B→A. Negate.
            Vector3 normalBtoA = -collision.GetContact(0).normal;

            // Relative velocity = vA - vB. Wall vB = 0.
            Vector3 vA = body.linearVelocity;
            Vector3 vB = otherIsWall ? Vector3.zero : otherBody.linearVelocity;
            Vector3 relativeVelocity = vA - vB;

            ContactInput input = new ContactInput(relativeVelocity, normalBtoA, massA, massB);
            ContactOutcome outcome = CombatResolver.Resolve(in input, config);

            // --- Apply outputs to self (A) ---
            if (health != null && outcome.DamageA > 0f)
                health.takeDmg(outcome.DamageA);
            if (outcome.ImpulseA != Vector3.zero)
                body.AddForce(outcome.ImpulseA, ForceMode.Impulse);

            // --- Apply outputs to other (B) ---
            if (otherIsWall) return;
            HealthScript otherHealth = collision.gameObject.GetComponent<HealthScript>();
            if (otherHealth != null && outcome.DamageB > 0f)
                otherHealth.takeDmg(outcome.DamageB);
            if (outcome.ImpulseB != Vector3.zero)
                otherBody.AddForce(outcome.ImpulseB, ForceMode.Impulse);
        }
    }
}
