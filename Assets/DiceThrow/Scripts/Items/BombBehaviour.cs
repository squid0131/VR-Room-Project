using System.Collections.Generic;
using UnityEngine;

namespace DiceThrow
{
    /// <summary>
    /// 爆弾（出目 6・破壊）。手を離すと導火線に火がつき、勢いよく当たるか時間が来ると爆発する。
    /// 周りの敵にダメージを与え、壊せる壁を壊す。近くにいると自分も巻き込まれる
    /// </summary>
    public class BombBehaviour : ItemBehaviour
    {
        [Tooltip("火がついてから爆発するまでの秒数")]
        public float fuseTime = 3.0f;

        [Tooltip("この勢い（m/s）以上で当たると、その場で爆発する")]
        public float impactSpeed = 3.0f;

        [Tooltip("爆発の半径（m）")]
        public float radius = 3.0f;

        [Tooltip("敵へのダメージ")]
        public int damage = 5;

        [Tooltip("巻き込まれたときの自分へのダメージ")]
        public int playerDamage = 2;

        [Tooltip("周りの物を吹き飛ばす力")]
        public float force = 12.0f;

        [Tooltip("爆発の見た目に使う半透明のマテリアル")]
        public Material explosionMaterial = null;

        [Tooltip("火がついている間に点滅させる部品（導火線）")]
        public Renderer fuse = null;

        private bool lit = false;
        private bool exploded = false;
        private float litTime = 0.0f;

        protected override void OnReleased()
        {
            if (lit)
                return;

            lit = true;
            litTime = Time.time;
        }

        protected override void OnImpact(Collision collision)
        {
            if (lit && ImpactSpeed(collision) >= impactSpeed)
                Explode();
        }

        private void Update()
        {
            if (!lit || exploded)
                return;

            float elapsed = Time.time - litTime;

            // 爆発が近いほど速く点滅する
            if (fuse != null)
            {
                float rate = Mathf.Lerp(4.0f, 16.0f, elapsed / fuseTime);
                fuse.enabled = Mathf.Repeat(elapsed * rate, 1.0f) < 0.6f;
            }

            if (elapsed >= fuseTime)
                Explode();
        }

        private void Explode()
        {
            if (exploded)
                return;
            exploded = true;

            Vector3 center = transform.position;
            var damaged = new HashSet<Damageable>();
            var walls = new HashSet<BreakableWall>();
            var bodies = new HashSet<Rigidbody>();

            foreach (var hit in Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                var target = hit.GetComponentInParent<Damageable>();
                if (target != null && damaged.Add(target))
                    target.TakeDamage(damage, hit.ClosestPoint(center), (hit.transform.position - center).normalized);

                var wall = hit.GetComponentInParent<BreakableWall>();
                if (wall != null)
                    walls.Add(wall);

                if (hit.attachedRigidbody != null && hit.attachedRigidbody != Body && !hit.attachedRigidbody.isKinematic)
                    bodies.Add(hit.attachedRigidbody);
            }

            foreach (var wall in walls)
                wall.Break(center, force, radius);

            foreach (var body in bodies)
                body.AddExplosionForce(force, center, radius * 1.5f, 0.5f, ForceMode.Impulse);

            // 自分も巻き込まれる（盾で防げる）
            if (PlayerRig.Instance != null && PlayerHealth.Instance != null
                && Vector3.Distance(PlayerRig.Instance.HeadPosition, center) < radius)
            {
                PlayerHealth.Instance.TakeDamage(playerDamage, center);
            }

            ExplosionEffect.Spawn(center, radius, explosionMaterial);
            VanishNow();
        }
    }
}
