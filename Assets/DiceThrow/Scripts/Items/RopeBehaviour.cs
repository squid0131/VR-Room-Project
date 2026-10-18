using UnityEngine;

namespace DiceThrow
{
    /// <summary>
    /// ロープ（出目 3・移動）。投げて引っかけ場所（RopeAnchor）に当たると引っかかり、自動で引っ張り上げてくれる。
    /// 引っ張り終わるか、引っかからずに落ちると消える
    /// </summary>
    public class RopeBehaviour : ItemBehaviour
    {
        [Tooltip("引っかからなかったとき、消えるまでの秒数")]
        public float missVanishDelay = 1.5f;

        [Tooltip("張ったロープの太さ（m）")]
        public float lineWidth = 0.025f;

        private LineRenderer line = null;
        private bool hooked = false;

        protected override void Awake()
        {
            base.Awake();

            line = gameObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.startWidth = lineWidth;
            line.endWidth = lineWidth;
            line.useWorldSpace = true;
            line.enabled = false;
            var r = GetComponentInChildren<MeshRenderer>();
            if (r != null)
                line.sharedMaterial = r.sharedMaterial;
        }

        private void OnTriggerEnter(Collider other)
        {
            // 引っかけ場所は大きめのトリガーなので、多少外れても引っかかる
            if (!InFlight || hooked)
                return;

            var anchor = other.GetComponentInParent<RopeAnchor>();
            if (anchor != null)
                Hook(anchor);
        }

        protected override void OnImpact(Collision collision)
        {
            var anchor = collision.collider.GetComponentInParent<RopeAnchor>();
            if (anchor != null)
                Hook(anchor);
            else
                Vanish(missVanishDelay);
        }

        private void Hook(RopeAnchor anchor)
        {
            InFlight = false;

            if (PlayerRig.Instance == null || PlayerRig.Instance.IsPulling)
            {
                Vanish(missVanishDelay);
                return;
            }

            hooked = true;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.isKinematic = true;
            transform.position = anchor.HookPoint;

            line.enabled = true;
            UpdateLine();

            PlayerRig.Instance.StartPull(anchor.LandingPosition, () =>
            {
                line.enabled = false;
                Vanish();
            });
        }

        private void LateUpdate()
        {
            if (hooked)
                UpdateLine();
        }

        private void UpdateLine()
        {
            // 胸のあたりから引っかけた場所まで
            Vector3 chest = PlayerRig.Instance != null ? PlayerRig.Instance.HeadPosition + Vector3.down * 0.4f : transform.position;
            line.SetPosition(0, chest);
            line.SetPosition(1, transform.position);
        }
    }
}
