using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DiceThrow
{
    /// <summary>
    /// 掴んで投げられる道具。重さに応じて、投げたときの速さと持ったときの重さの感じが変わる
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(XRGrabInteractable))]
    public class ThrowableItem : MonoBehaviour
    {
        [Tooltip("結果表示に使う名前")]
        public string displayName = "Item";

        [Tooltip("重さ（kg）")]
        public float mass = 1.0f;

        [Tooltip("空気抵抗。大きいほど遠くまで飛ばない")]
        public float airDrag = 0.0f;

        [Tooltip("投げた後に先端（+Z）を進行方向へ向ける（やり用）")]
        public bool alignToVelocity = false;

        [Tooltip("この高さより下に落ちたら元の位置に戻す")]
        public float respawnHeight = -10.0f;

        private Rigidbody body = null;
        private XRGrabInteractable grab = null;

        private Vector3 spawnPosition = Vector3.zero;
        private Quaternion spawnRotation = Quaternion.identity;

        // 手を離してから最初に何かに当たるまでの間 true
        private bool inFlight = false;
        private Vector3 releasePosition = Vector3.zero;

        public bool IsHeld => grab != null && grab.isSelected;

        private void Awake()
        {
            CacheComponents();

            spawnPosition = transform.position;
            spawnRotation = transform.rotation;

            grab.selectEntered.AddListener(_ => inFlight = false);
            grab.selectExited.AddListener(_ =>
            {
                inFlight = true;
                releasePosition = transform.position;
            });
        }

        private void Start()
        {
            ApplySettings();
        }

        /// <summary>
        /// ThrowTestManager の設定と自分の重さから、掴み・投げの挙動を決める
        /// </summary>
        public void ApplySettings()
        {
            // Play 開始時に Awake より先に呼ばれることがある
            CacheComponents();

            var manager = ThrowTestManager.Instance;
            float throwPower = manager != null ? manager.throwPower : 1.0f;
            float referenceMass = manager != null ? manager.referenceMass : 0.5f;
            float weightInfluence = manager != null ? manager.weightInfluence : 0.0f;

            body.mass = mass;
            body.linearDamping = airDrag;
            body.angularDamping = 0.5f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // 軽いものほど速く、重いものほど遅く飛ぶ
            float weightFactor = Mathf.Clamp(Mathf.Pow(referenceMass / mass, weightInfluence), 0.3f, 2.0f);

            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grab.useDynamicAttach = true;
            grab.throwOnDetach = true;
            grab.throwVelocityScale = throwPower * weightFactor;
            grab.throwAngularVelocityScale = 1.0f;

            // 重いものほど手の動きに遅れてついてくる
            float heaviness = Mathf.InverseLerp(0.2f, 4.0f, mass);
            grab.smoothPosition = true;
            grab.smoothPositionAmount = Mathf.Lerp(20.0f, 5.0f, heaviness);
            grab.smoothRotation = true;
            grab.smoothRotationAmount = Mathf.Lerp(20.0f, 5.0f, heaviness);
        }

        private void CacheComponents()
        {
            if (body == null)
                body = GetComponent<Rigidbody>();
            if (grab == null)
                grab = GetComponent<XRGrabInteractable>();
        }

        private void FixedUpdate()
        {
            if (!alignToVelocity || !inFlight || IsHeld)
                return;

            Vector3 velocity = body.linearVelocity;
            if (velocity.sqrMagnitude < 4.0f)
                return;

            Quaternion target = Quaternion.LookRotation(velocity);
            body.MoveRotation(Quaternion.Slerp(body.rotation, target, 10.0f * Time.fixedDeltaTime));
            body.angularVelocity *= 0.9f;
        }

        private void Update()
        {
            if (transform.position.y < respawnHeight)
                ResetToSpawn();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!inFlight)
                return;

            inFlight = false;

            Vector3 offset = collision.GetContact(0).point - releasePosition;
            offset.y = 0.0f;

            if (ThrowTestManager.Instance != null)
                ThrowTestManager.Instance.ReportThrow(displayName, offset.magnitude);
        }

        public void ResetToSpawn()
        {
            if (IsHeld)
                return;

            inFlight = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = spawnPosition;
            body.rotation = spawnRotation;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        }
    }
}
