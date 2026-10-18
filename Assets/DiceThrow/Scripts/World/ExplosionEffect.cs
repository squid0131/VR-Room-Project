using UnityEngine;

namespace DiceThrow
{
    /// <summary>
    /// 爆発の見た目。半透明の球が一瞬で広がって消え、光が弱まっていく
    /// </summary>
    public class ExplosionEffect : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private const float Duration = 0.4f;

        private float radius = 3.0f;
        private float startTime = 0.0f;
        private Renderer sphere = null;
        private Light flash = null;
        private Color color = Color.white;
        private MaterialPropertyBlock block = null;

        public static void Spawn(Vector3 position, float radius, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Explosion";
            Destroy(go.GetComponent<Collider>());
            go.transform.position = position;

            var effect = go.AddComponent<ExplosionEffect>();
            effect.radius = radius;
            effect.sphere = go.GetComponent<Renderer>();
            if (material != null)
            {
                effect.sphere.sharedMaterial = material;
                effect.color = material.color;
            }

            effect.flash = go.AddComponent<Light>();
            effect.flash.type = LightType.Point;
            effect.flash.color = new Color(1.0f, 0.6f, 0.2f);
            effect.flash.range = radius * 3.0f;
        }

        private void Awake()
        {
            startTime = Time.time;
            block = new MaterialPropertyBlock();
        }

        private void Update()
        {
            float t = (Time.time - startTime) / Duration;
            if (t >= 1.0f)
            {
                Destroy(gameObject);
                return;
            }

            float eased = 1.0f - Mathf.Pow(1.0f - t, 3.0f);
            transform.localScale = Vector3.one * Mathf.Lerp(0.3f, radius * 2.0f, eased);

            block.SetColor(BaseColorId, new Color(color.r, color.g, color.b, color.a * (1.0f - t)));
            sphere.SetPropertyBlock(block);

            flash.intensity = 8.0f * (1.0f - t);
        }
    }
}
