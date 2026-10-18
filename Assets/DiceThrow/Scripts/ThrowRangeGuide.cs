using UnityEngine;

namespace DiceThrow
{
    /// <summary>
    /// 範囲で投げる（Range）ときの目安の表示。
    /// 基準の位置を中心に、最大の力になる円と置くだけになる内側の円を、投げる向きと上方向を含む縦の面に描き、
    /// 基準の位置から手までを力のゲージ（線）で、ゲージの先に最大の力の位置の小さな輪を描く
    /// </summary>
    public class ThrowRangeGuide : MonoBehaviour
    {
        private const int RingSegments = 48;
        private const float MarkerRadius = 0.035f;

        private static readonly Color RangeColor = new Color(1.0f, 1.0f, 1.0f, 0.35f);
        private static readonly Color DeadZoneColor = new Color(1.0f, 1.0f, 1.0f, 0.2f);
        private static readonly Color EmptyColor = new Color(1.0f, 1.0f, 1.0f, 0.25f);
        private static readonly Color WeakColor = new Color(1.0f, 0.9f, 0.3f, 0.9f);
        private static readonly Color StrongColor = new Color(1.0f, 0.3f, 0.2f, 0.95f);

        private LineRenderer rangeRing = null;
        private LineRenderer deadZoneRing = null;
        private LineRenderer gauge = null;
        private LineRenderer marker = null;

        private readonly Vector3[] ringPoints = new Vector3[RingSegments];
        private readonly Vector3[] gaugePoints = new Vector3[3];

        public static ThrowRangeGuide Create(string name, Material material)
        {
            var guide = new GameObject(name).AddComponent<ThrowRangeGuide>();
            guide.rangeRing = guide.AddLine("Range", material, 0.006f, loop: true);
            guide.deadZoneRing = guide.AddLine("Dead Zone", material, 0.004f, loop: true);
            guide.gauge = guide.AddLine("Gauge", material, 0.012f, loop: false);
            guide.marker = guide.AddLine("Max Marker", material, 0.005f, loop: true);
            guide.Hide();
            return guide;
        }

        private LineRenderer AddLine(string lineName, Material material, float width, bool loop)
        {
            var go = new GameObject(lineName);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = material;
            line.widthMultiplier = width;
            line.loop = loop;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        /// <summary>力の色。弱いと黄色、強いと赤</summary>
        public static Color PowerColor(float power)
        {
            return Color.Lerp(WeakColor, StrongColor, power);
        }

        /// <summary>
        /// center: 基準の位置、up: XR リグの上方向、forward: 手をほとんど動かしていないときの円の向き（顔の向き）、
        /// hand: 今の手の位置、power: 0〜1（置くだけのときは 0）
        /// </summary>
        public void Show(Vector3 center, Vector3 up, Vector3 forward, float radius, float deadZone, Vector3 hand, float power)
        {
            gameObject.SetActive(true);

            Vector3 offset = hand - center;

            // 円は「投げる向き（水平）」と「上」を含む縦の面に描く。手を左右に向けると円も一緒に回る
            Vector3 aim = Vector3.ProjectOnPlane(offset, up);
            if (aim.sqrMagnitude < 0.02f * 0.02f)
                aim = forward;
            Vector3 side = Vector3.Cross(up, aim);
            if (side.sqrMagnitude < 0.000001f)
                side = Vector3.Cross(up, Vector3.forward);

            bool full = power >= 1.0f;
            Color rangeColor = full ? PowerColor(1.0f) : RangeColor;
            SetRing(rangeRing, center, side, radius, rangeColor);
            SetRing(deadZoneRing, center, side, deadZone, DeadZoneColor);

            float distance = offset.magnitude;
            if (distance <= deadZone || radius <= 0.0f)
            {
                gauge.enabled = false;
                marker.enabled = false;
                return;
            }

            // 基準の位置 → 手（力の分だけ色付き） → 最大の力の位置（灰色）
            Vector3 direction = offset / distance;
            float fill = Mathf.Min(distance, radius);
            Vector3 maxPoint = center + direction * radius;
            gaugePoints[0] = center;
            gaugePoints[1] = center + direction * fill;
            gaugePoints[2] = maxPoint;
            gauge.positionCount = 3;
            gauge.SetPositions(gaugePoints);
            gauge.colorGradient = GaugeGradient(fill / radius, PowerColor(power));
            gauge.enabled = true;

            SetRing(marker, maxPoint, direction, MarkerRadius, full ? PowerColor(1.0f) : EmptyColor);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        private static Gradient GaugeGradient(float split, Color color)
        {
            // split までは力の色、その先は灰色にくっきり切り替える
            float edge = Mathf.Min(split + 0.001f, 1.0f);
            return new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(color, 0.0f),
                    new GradientColorKey(color, split),
                    new GradientColorKey(EmptyColor, edge),
                    new GradientColorKey(EmptyColor, 1.0f),
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(color.a, 0.0f),
                    new GradientAlphaKey(color.a, split),
                    new GradientAlphaKey(EmptyColor.a, edge),
                    new GradientAlphaKey(EmptyColor.a, 1.0f),
                },
            };
        }

        /// <summary>normal に垂直な面に、center を中心とした円を描く</summary>
        private void SetRing(LineRenderer line, Vector3 center, Vector3 normal, float radius, Color color)
        {
            if (radius <= 0.0f)
            {
                line.enabled = false;
                return;
            }

            Vector3 a = Vector3.Cross(normal, Mathf.Abs(Vector3.Dot(normal.normalized, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 b = Vector3.Cross(normal.normalized, a);

            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2.0f / RingSegments;
                ringPoints[i] = center + (a * Mathf.Cos(angle) + b * Mathf.Sin(angle)) * radius;
            }

            line.positionCount = RingSegments;
            line.SetPositions(ringPoints);
            line.startColor = color;
            line.endColor = color;
            line.enabled = true;
        }
    }
}
