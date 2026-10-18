using System;
using UnityEngine;
using UnityEngine.Events;

namespace DiceThrow
{
    /// <summary>
    /// ダメージを受けるもの（敵など）。当たると白く光る
    /// </summary>
    public class Damageable : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("最大体力")]
        public int maxHealth = 6;

        [Tooltip("ダメージを受けたとき（引数は残り体力）")]
        public UnityEvent<int> onDamaged = new UnityEvent<int>();

        [Tooltip("体力が 0 になったとき")]
        public UnityEvent onDied = new UnityEvent();

        /// <summary>ダメージ量・当たった位置・当たった向き</summary>
        public event Action<int, Vector3, Vector3> Damaged;

        public int Health { get; private set; }
        public bool IsDead => Health <= 0;

        private Renderer[] renderers = null;
        private MaterialPropertyBlock block = null;
        private float flashTime = -10.0f;

        private void Awake()
        {
            Health = maxHealth;
            // 体力表示などの文字は光らせない
            renderers = Array.FindAll(GetComponentsInChildren<Renderer>(), r => r.GetComponent<TMPro.TMP_Text>() == null);
            block = new MaterialPropertyBlock();
        }

        /// <summary>ダメージを与える。与えられたら true</summary>
        public bool TakeDamage(int amount, Vector3 point, Vector3 direction)
        {
            if (IsDead || amount <= 0)
                return false;

            Health = Mathf.Max(0, Health - amount);
            flashTime = Time.time;

            Damaged?.Invoke(amount, point, direction);
            onDamaged.Invoke(Health);
            if (IsDead)
                onDied.Invoke();
            return true;
        }

        public void ResetHealth()
        {
            Health = maxHealth;
        }

        private void Update()
        {
            float t = (Time.time - flashTime) / 0.15f;
            bool flashing = t < 1.0f;

            foreach (var r in renderers)
            {
                if (r == null)
                    continue;

                if (flashing)
                {
                    r.GetPropertyBlock(block);
                    block.SetColor(BaseColorId, Color.Lerp(Color.white, r.sharedMaterial.color, t));
                    r.SetPropertyBlock(block);
                }
                else if (r.HasPropertyBlock())
                {
                    r.SetPropertyBlock(null);
                }
            }
        }

        /// <summary>当たったコライダーの持ち主にダメージを与える。与えられたら true</summary>
        public static bool Hit(Collider collider, int amount, Vector3 point, Vector3 direction)
        {
            if (collider == null)
                return false;

            var target = collider.GetComponentInParent<Damageable>();
            return target != null && target.TakeDamage(amount, point, direction);
        }
    }
}
