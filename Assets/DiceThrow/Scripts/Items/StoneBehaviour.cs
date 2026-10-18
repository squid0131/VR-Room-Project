using UnityEngine;

namespace DiceThrow
{
    /// <summary>
    /// 石（出目 1・外れ枠）。当たると小ダメージを与え、音で敵の注意を少し引いて消える
    /// </summary>
    public class StoneBehaviour : ItemBehaviour
    {
        [Tooltip("当たったときのダメージ")]
        public int damage = 1;

        [Tooltip("ダメージを与えるのに必要な勢い（m/s）")]
        public float minDamageSpeed = 2.0f;

        [Tooltip("音で敵の注意を引く強さ（リンゴは 3）")]
        public float noiseStrength = 1.0f;

        [Tooltip("音が届く距離（m）")]
        public float noiseRadius = 8.0f;

        [Tooltip("音が続く秒数")]
        public float noiseDuration = 3.0f;

        protected override void OnImpact(Collision collision)
        {
            Vector3 point = collision.GetContact(0).point;

            if (ImpactSpeed(collision) >= minDamageSpeed)
                Damageable.Hit(collision.collider, damage, point, LastVelocity.normalized);

            Lure.CreateNoise(point, noiseStrength, noiseRadius, noiseDuration);
            Vanish(0.3f);
        }
    }
}
