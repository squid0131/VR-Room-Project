using UnityEngine;

namespace DiceThrow
{
    /// <summary>
    /// 敵が投げる弾。まっすぐ飛び、プレイヤーの頭に届くとダメージ。
    /// 手で構えた盾に当たると跳ね返り、投げた敵（いなければ跳ね返った向き）へ飛んで敵にダメージを与える
    /// </summary>
    public class EnemyProjectile : MonoBehaviour
    {
        private const float Radius = 0.1f;
        private const float HeadRadius = 0.3f;
        private const float LifeTime = 5.0f;

        private static readonly RaycastHit[] HitBuffer = new RaycastHit[8];

        private SimpleEnemy owner = null;
        private Vector3 velocity = Vector3.zero;
        private int damage = 1;
        private bool reflected = false;
        private float spawnTime = 0.0f;
        private TrailRenderer trail = null;

        public static EnemyProjectile Spawn(SimpleEnemy owner, Vector3 position, Vector3 target, float speed, int damage, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Enemy Projectile";
            // 当たり判定は自分で調べる（物理で押されたり、盾の Rigidbody を押したりしないようにする）
            Destroy(go.GetComponent<Collider>());
            go.transform.position = position;
            go.transform.localScale = Vector3.one * Radius * 2.0f;

            var renderer = go.GetComponent<Renderer>();
            if (material != null)
                renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var projectile = go.AddComponent<EnemyProjectile>();
            projectile.owner = owner;
            projectile.velocity = (target - position).normalized * speed;
            projectile.damage = damage;
            projectile.spawnTime = Time.time;
            projectile.trail = projectile.AddTrail(new Color(1.0f, 0.4f, 0.4f));
            return projectile;
        }

        private TrailRenderer AddTrail(Color color)
        {
            var t = gameObject.AddComponent<TrailRenderer>();
            t.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            t.time = 0.25f;
            t.widthCurve = new AnimationCurve(new Keyframe(0.0f, Radius * 1.5f), new Keyframe(1.0f, 0.0f));
            SetTrailColor(t, color);
            t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return t;
        }

        private static void SetTrailColor(TrailRenderer t, Color color)
        {
            t.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(color, 0.0f), new GradientColorKey(color, 1.0f) },
                alphaKeys = new[] { new GradientAlphaKey(0.8f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) },
            };
        }

        private void OnDestroy()
        {
            if (trail != null && trail.sharedMaterial != null)
                Destroy(trail.sharedMaterial);
        }

        private void Update()
        {
            if (Time.time - spawnTime > LifeTime)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 from = transform.position;
            Vector3 to = from + velocity * Time.deltaTime;

            if (!reflected)
            {
                // 構えた盾に当たったら跳ね返す
                var shield = ShieldBehaviour.FindHeldShieldNear(from, to, Radius);
                if (shield != null)
                {
                    Reflect(shield);
                    return;
                }

                // 頭に届いたらダメージ（頭には当たり判定がないので距離で調べる）
                if (PlayerRig.Instance != null && DistanceToSegment(PlayerRig.Instance.HeadPosition, from, to) < HeadRadius)
                {
                    PlayerHealth.Instance?.TakeDamage(damage, from, owner);
                    Destroy(gameObject);
                    return;
                }
            }

            if (CheckHit(from, to))
                return;

            transform.position = to;
        }

        /// <summary>途中で何かに当たったら、敵ならダメージを与えて消える。当たったら true</summary>
        private bool CheckHit(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.0001f)
                return false;

            int count = Physics.SphereCastNonAlloc(from, Radius, delta / distance, HitBuffer, distance, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            Collider nearestCollider = null;
            Vector3 point = to;
            for (int i = 0; i < count; i++)
            {
                var hit = HitBuffer[i];
                // 投げた本人（跳ね返る前）と、プレイヤーの体・持っている道具は無視する
                if (!reflected && owner != null && hit.collider.transform.IsChildOf(owner.transform))
                    continue;
                if (hit.collider is CharacterController || hit.collider.GetComponentInParent<ItemBehaviour>() != null)
                    continue;
                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    nearestCollider = hit.collider;
                    point = hit.point;
                }
            }

            if (nearestCollider == null)
                return false;

            if (reflected)
                Damageable.Hit(nearestCollider, damage + 1, point, delta / distance);
            Destroy(gameObject);
            return true;
        }

        /// <summary>盾で跳ね返す。投げた敵が生きていればその体へ、いなければ盾の面で反射した向きへ飛ぶ</summary>
        private void Reflect(ShieldBehaviour shield)
        {
            reflected = true;
            float speed = velocity.magnitude * 1.5f;

            if (owner != null && !owner.IsDead)
                velocity = (owner.BodyCenter - transform.position).normalized * speed;
            else
                velocity = Vector3.Reflect(velocity, shield.FaceNormal).normalized * speed;

            if (trail != null)
                SetTrailColor(trail, new Color(0.4f, 0.8f, 1.0f));

            shield.OnReflected();
        }

        internal static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 0.0f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude) : 0.0f;
            return Vector3.Distance(point, a + ab * t);
        }
    }
}
