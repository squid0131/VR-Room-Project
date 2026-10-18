using UnityEngine;

namespace DiceThrow
{
    /// <summary>
    /// やり（出目 4・メインの攻撃）。穂先から勢いよく当たると敵を倒し、刺さったまま残る。刺さったやりは拾い直せる
    /// </summary>
    public class SpearBehaviour : ItemBehaviour
    {
        [Tooltip("当たったときのダメージ")]
        public int damage = 3;

        [Tooltip("ダメージを与えるのに必要な勢い（m/s）")]
        public float minDamageSpeed = 3.0f;

        [Tooltip("刺さるのに必要な勢い（m/s）")]
        public float minStickSpeed = 4.0f;

        [Tooltip("穂先がどれだけ進行方向を向いていれば刺さるか（1 で完全に一致）")]
        [Range(0, 1)] public float stickAlignment = 0.6f;

        [Tooltip("刺さったときにめり込む深さ（m）")]
        public float stickDepth = 0.12f;

        public bool IsStuck { get; private set; } = false;

        protected override void OnGrabbed()
        {
            IsStuck = false;
        }

        protected override void OnImpact(Collision collision)
        {
            float speed = ImpactSpeed(collision);
            Vector3 direction = LastVelocity.sqrMagnitude > 0.01f ? LastVelocity.normalized : transform.forward;
            Vector3 point = collision.GetContact(0).point;

            if (speed >= minDamageSpeed)
                Damageable.Hit(collision.collider, damage, point, direction);

            // 穂先から当たったときだけ刺さる。動く物（箱など）には刺さらずに跳ね返る
            bool tipFirst = Vector3.Dot(transform.forward, direction) >= stickAlignment;
            var otherBody = collision.rigidbody;
            bool canStickTo = otherBody == null || otherBody.isKinematic;
            if (speed >= minStickSpeed && tipFirst && canStickTo)
                Stick(collision, direction);
        }

        private void Stick(Collision collision, Vector3 direction)
        {
            IsStuck = true;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.isKinematic = true;

            transform.rotation = Quaternion.LookRotation(direction, transform.up);
            transform.position += direction * stickDepth;

            // 敵や壊せる壁に刺さったら一緒に動く（倒されたり壊れたりしたら Unstick で外れる）
            // 拡大縮小された見た目の子に入れると形がゆがむので、根元（拡大縮小なし）の子にする
            Component holder = collision.collider.GetComponentInParent<Damageable>();
            if (holder == null)
                holder = collision.collider.GetComponentInParent<BreakableWall>();
            if (holder != null)
                transform.SetParent(holder.transform, true);
        }

        /// <summary>刺さっている物から外して落とす（敵が倒されたときなど）</summary>
        public void Unstick()
        {
            if (!IsStuck)
                return;

            IsStuck = false;
            transform.SetParent(null, true);
            Body.isKinematic = false;
            Body.useGravity = true;
        }
    }
}
