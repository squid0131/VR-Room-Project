using System.Collections.Generic;
using UnityEngine;

namespace DiceThrow
{
    /// <summary>
    /// 敵の注意を引くもの。リンゴ（強い・食べられる）や、石が当たった音（弱い・すぐ消える）
    /// </summary>
    public class Lure : MonoBehaviour
    {
        public static readonly List<Lure> Active = new List<Lure>();

        [Tooltip("引きつける強さ。強いほうが優先される")]
        public float strength = 1.0f;

        [Tooltip("敵が気づく距離（m）")]
        public float radius = 8.0f;

        [Tooltip("この秒数で消える。0 なら消えない")]
        public float lifetime = 0.0f;

        [Tooltip("敵が食べられるか（リンゴ）")]
        public bool edible = false;

        private float startTime = 0.0f;

        private void OnEnable()
        {
            startTime = Time.time;
            Active.Add(this);
        }

        private void OnDisable()
        {
            Active.Remove(this);
        }

        private void Update()
        {
            if (lifetime > 0.0f && Time.time - startTime > lifetime)
                Destroy(gameObject);
        }

        /// <summary>敵に食べられる</summary>
        public void Consume()
        {
            var item = GetComponent<ItemBehaviour>();
            if (item != null)
                item.VanishNow();
            else
                Destroy(gameObject);
        }

        /// <summary>音のような一時的な誘い（石が当たった場所など）を作る</summary>
        public static Lure CreateNoise(Vector3 position, float strength, float radius, float lifetime)
        {
            var go = new GameObject("Noise Lure");
            go.transform.position = position;
            var lure = go.AddComponent<Lure>();
            lure.strength = strength;
            lure.radius = radius;
            lure.lifetime = lifetime;
            return lure;
        }

        /// <summary>from から気づける範囲で、一番引きつけられる誘いを返す</summary>
        public static Lure FindBest(Vector3 from)
        {
            Lure best = null;
            float bestScore = 0.0f;

            foreach (var lure in Active)
            {
                Vector3 offset = lure.transform.position - from;
                offset.y = 0.0f;
                float distance = offset.magnitude;
                if (distance > lure.radius)
                    continue;

                float score = lure.strength * (1.0f - 0.5f * distance / lure.radius);
                if (score > bestScore)
                {
                    best = lure;
                    bestScore = score;
                }
            }

            return best;
        }
    }
}
