using UnityEngine;

namespace NoraSquad
{
    /// <summary>
    /// Simple demo projectile: flies to a target point (straight or in an arc), then spawns an
    /// impact effect and destroys itself. Use <see cref="Launch"/> to fire one from code.
    /// </summary>
    public class NoraProjectile : MonoBehaviour
    {
        [Tooltip("Travel speed in units per second.")]
        [SerializeField, Min(0.1f)] float speed = 12f;

        [Tooltip("Height of the arc at the middle of the flight. 0 = straight line.")]
        [SerializeField, Min(0f)] float arcHeight;

        [Tooltip("Spawned where the projectile lands.")]
        [SerializeField] GameObject impactEffect;

        [Tooltip("Seconds the projectile object lingers after impact so trails can fade out.")]
        [SerializeField, Min(0f)] float lingerTime = 0.5f;

        Vector3 start, target;
        float duration, elapsed;
        bool flying;

        /// <summary>Instantiates <paramref name="prefab"/> at <paramref name="from"/> and sends it to <paramref name="to"/>.</summary>
        public static NoraProjectile Launch(NoraProjectile prefab, Vector3 from, Vector3 to)
        {
            var projectile = Instantiate(prefab, from, Quaternion.LookRotation(to - from));
            projectile.Fire(to);
            return projectile;
        }

        /// <summary>Starts flying from the current position to <paramref name="to"/>.</summary>
        public void Fire(Vector3 to)
        {
            start = transform.position;
            target = to;
            duration = Mathf.Max(0.05f, Vector3.Distance(start, target) / speed);
            elapsed = 0f;
            flying = true;
        }

        void Update()
        {
            if (!flying)
                return;

            elapsed += Time.deltaTime;
            var t = Mathf.Clamp01(elapsed / duration);
            var position = Vector3.Lerp(start, target, t) + Vector3.up * (arcHeight * 4f * t * (1f - t));
            var direction = position - transform.position;
            if (direction.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(direction);
            transform.position = position;

            if (t < 1f)
                return;

            flying = false;
            if (impactEffect != null)
                Destroy(Instantiate(impactEffect, target, Quaternion.identity), 3f);

            // Stop emitting so trails fade out naturally, then clean up.
            foreach (var ps in GetComponentsInChildren<ParticleSystem>())
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            foreach (var r in GetComponentsInChildren<MeshRenderer>())
                r.enabled = false;
            Destroy(gameObject, lingerTime);
        }

#if UNITY_EDITOR
        public void EditorSetup(GameObject impact, float newSpeed, float arc)
        {
            impactEffect = impact;
            speed = newSpeed;
            arcHeight = arc;
        }
#endif
    }
}
