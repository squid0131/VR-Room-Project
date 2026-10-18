using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DiceThrow
{
    /// <summary>
    /// 掴んで投げられる道具。重さに応じて、投げたときの速さと持ったときの重さの感じが変わる。
    /// 振った勢いと向きから投げる速さを決め（離す瞬間のぶれに強い）、飛ぶ道筋と軌跡を表示する
    /// </summary>
    // 投げる速さを ItemBehaviour（盾など）が読む前に与えるため、先に FixedUpdate を実行する
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(Rigidbody), typeof(XRGrabInteractable))]
    public class ThrowableItem : MonoBehaviour
    {
        private const int HandSampleCount = 32;
        private const int PathPointCount = 151;
        private const float PathFadeTime = 1.0f;

        private static Material lineMaterial = null;

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

        [Tooltip("さいころで出した道具か。元の位置に戻す代わりに消える")]
        public bool spawnedByDice = false;

        [Tooltip("さいころで出したとき、円盤を縦に立てて面を前に向ける（盾用）")]
        public bool spawnUpright = false;

        [Tooltip("狙いを補正する的の種類")]
        public ThrowTargetKind aimTargets = ThrowTargetKind.None;

        [Tooltip("重力なしで真っすぐ飛ぶ（盾のブーメラン）。道筋の予測と狙いの補正に使う")]
        public bool straightFlight = false;

        [Tooltip("真っすぐ飛ぶときの道筋の長さ（秒）")]
        public float straightFlightTime = 0.5f;

        [Tooltip("軌跡の太さ（m）")]
        public float trailWidth = 0.05f;

        private Rigidbody body = null;
        private XRGrabInteractable grab = null;

        private Vector3 spawnPosition = Vector3.zero;
        private Quaternion spawnRotation = Quaternion.identity;

        // 手を離してから最初に何かに当たるまでの間 true
        private bool inFlight = false;
        private Vector3 releasePosition = Vector3.zero;

        // 投げる設定（ApplySettings で決まる）
        private float throwScale = 1.0f;

        // 持っている手の位置の記録（リングバッファ）
        private readonly Vector3[] handPositions = new Vector3[HandSampleCount];
        private readonly float[] handTimes = new float[HandSampleCount];
        private int handNewest = -1;
        private int handCount = 0;
        private Transform holdingHand = null;

        // 手の位置は XR リグの中の座標で記録する。スティック移動や旋回の速さが「振った」と見なされないようにする
        private Transform handSpace = null;

        private bool throwPending = false;
        private Vector3 pendingHandVelocity = Vector3.zero;

        // 道筋と軌跡
        private readonly Vector3[] pathPoints = new Vector3[PathPointCount];
        private LineRenderer pathLine = null;
        private float pathShownTime = -10.0f;
        private bool pathFading = false;
        private TrailRenderer trail = null;

        public bool IsHeld => grab != null && grab.isSelected;

        private void Awake()
        {
            CacheComponents();

            spawnPosition = transform.position;
            spawnRotation = transform.rotation;

            grab.selectEntered.AddListener(args =>
            {
                inFlight = false;
                throwPending = false;
                holdingHand = args.interactorObject.transform;
                var origin = holdingHand.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
                handSpace = origin != null ? origin.transform : null;
                handCount = 0;
                handNewest = -1;
            });

            grab.selectExited.AddListener(_ =>
            {
                inFlight = true;
                releasePosition = transform.position;

                RecordHand();
                pendingHandVelocity = EstimateHandVelocity();
                throwPending = true;
                holdingHand = null;
            });
        }

        private void Start()
        {
            ApplySettings();
        }

        private void OnDestroy()
        {
            // 消えても、投げた道筋は残りの時間だけ表示しておく
            if (pathLine != null)
                Destroy(pathLine.gameObject, pathFading ? Mathf.Max(0.0f, PathFadeTime - (Time.time - pathShownTime)) : 0.0f);
        }

        private static ThrowTestManager Manager => ThrowTestManager.Instance;
        private static bool UseSwingThrow => Manager == null || Manager.useSwingThrow;
        private static float MinThrowSpeed => Manager != null ? Manager.minThrowSpeed : 1.0f;
        private static float SwingWindow => Manager != null ? Manager.swingWindow : 0.12f;

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
            throwScale = throwPower * weightFactor;

            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grab.useDynamicAttach = true;
            grab.throwOnDetach = !UseSwingThrow;
            grab.throwVelocityScale = throwScale;
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
            if (throwPending)
            {
                throwPending = false;
                if (UseSwingThrow)
                    ApplySwingThrow();
            }

            if (!alignToVelocity || !inFlight || IsHeld)
                return;

            Vector3 velocity = body.linearVelocity;
            if (velocity.sqrMagnitude < 4.0f)
                return;

            Quaternion target = Quaternion.LookRotation(velocity);
            body.MoveRotation(Quaternion.Slerp(body.rotation, target, 10.0f * Time.fixedDeltaTime));
            body.angularVelocity *= 0.9f;
        }

        /// <summary>
        /// 振った勢いと向きで飛ばす。XRI の標準の投げ方は離す瞬間の手の動きを使うため、手首のぶれで狙いが外れやすい
        /// </summary>
        private void ApplySwingThrow()
        {
            if (body.isKinematic)
                return;

            Vector3 velocity = ComputeLaunchVelocity(pendingHandVelocity, out ThrowTarget target);
            body.linearVelocity = velocity;

            // やりは最初から穂先を進行方向に向ける（刺さりやすくする）
            if (alignToVelocity && velocity.sqrMagnitude > 4.0f)
            {
                body.rotation = Quaternion.LookRotation(velocity);
                body.angularVelocity = Vector3.zero;
            }

            if (velocity.magnitude >= MinThrowSpeed * throwScale)
                ShowPath(velocity, fading: true, onTarget: target != null);
        }

        /// <summary>
        /// 手の速さから、実際に飛ばす初速を決める（投擲力・重さ・狙いの補正）
        /// </summary>
        private Vector3 ComputeLaunchVelocity(Vector3 handVelocity, out ThrowTarget target)
        {
            target = null;

            // さいころを振っている間は投げられない（その場に落ちる）
            if (DiceRoller.BlocksThrowing)
                return Vector3.zero;

            // ゆっくり離したときは投げずにそっと置く
            if (handVelocity.magnitude < MinThrowSpeed)
                return handVelocity;

            Vector3 velocity = handVelocity * throwScale;

            var manager = Manager;
            if (manager != null && ThrowPhysics.TryAimAssist(transform.position, velocity, aimTargets, manager.aimAssistAngle,
                manager.aimAssistDistance, airDrag, !straightFlight, out Vector3 assisted, out target))
            {
                velocity = assisted;
            }

            return velocity;
        }

        private void Update()
        {
            if (grab != null)
                grab.throwOnDetach = !UseSwingThrow && !DiceRoller.BlocksThrowing;

            if (IsHeld)
                RecordHand();

            if (trail != null || (Manager != null && Manager.showTrail))
                UpdateTrail();

            if (transform.position.y < respawnHeight)
            {
                if (spawnedByDice)
                    Destroy(gameObject);
                else
                    ResetToSpawn();
            }
        }

        private void LateUpdate()
        {
            UpdatePreview();
        }

        private void RecordHand()
        {
            if (holdingHand == null)
                return;

            handNewest = (handNewest + 1) % HandSampleCount;
            handPositions[handNewest] = handSpace != null ? handSpace.InverseTransformPoint(holdingHand.position) : holdingHand.position;
            handTimes[handNewest] = Time.time;
            handCount = Mathf.Min(handCount + 1, HandSampleCount);
        }

        /// <summary>振った速さ（ワールド座標の向き）</summary>
        private Vector3 EstimateHandVelocity()
        {
            Vector3 local = ThrowPhysics.EstimateThrowVelocity(handPositions, handTimes, handCount, handNewest, SwingWindow);
            return handSpace != null ? handSpace.TransformVector(local) : local;
        }

        // ---- 道筋の表示 ----

        /// <summary>
        /// 振っている間、今離したら飛ぶ道筋を表示する。的に向かうときは緑、それ以外は白
        /// </summary>
        private void UpdatePreview()
        {
            bool showTrajectory = Manager != null && Manager.showTrajectory;

            if (showTrajectory && IsHeld && UseSwingThrow && !DiceRoller.BlocksThrowing && handCount >= 2)
            {
                Vector3 hand = EstimateHandVelocity();

                // 構えて振り始めたら（投げる速さの 1.5 倍）表示する
                if (hand.magnitude >= MinThrowSpeed * 1.5f)
                {
                    Vector3 velocity = ComputeLaunchVelocity(hand, out ThrowTarget target);
                    ShowPath(velocity, fading: false, onTarget: target != null);
                    return;
                }
            }

            if (pathLine == null || !pathLine.enabled)
                return;

            // 投げた後の道筋は少しずつ消す。振るのをやめたら少し残して消す
            float age = Time.time - pathShownTime;
            float fadeTime = pathFading ? PathFadeTime : 0.25f;
            if (age >= fadeTime)
            {
                pathLine.enabled = false;
                return;
            }

            float alpha = 1.0f - age / fadeTime;
            Color start = pathLine.startColor;
            Color end = pathLine.endColor;
            start.a = 0.9f * alpha;
            end.a = 0.3f * alpha;
            pathLine.startColor = start;
            pathLine.endColor = end;
        }

        private void ShowPath(Vector3 velocity, bool fading, bool onTarget)
        {
            if (Manager == null || !Manager.showTrajectory)
                return;

            EnsurePathLine();

            float maxTime = straightFlight ? straightFlightTime : 3.0f;
            int count = ThrowPhysics.Simulate(transform.position, velocity, airDrag, !straightFlight, maxTime, body, pathPoints, out bool hit);

            // 的への補正が効いている（＝的に当たる道筋）なら緑
            Color color = onTarget ? new Color(0.3f, 1.0f, 0.4f) : Color.white;

            pathLine.positionCount = count;
            pathLine.SetPositions(pathPoints);
            pathLine.startColor = new Color(color.r, color.g, color.b, 0.9f);
            pathLine.endColor = new Color(color.r, color.g, color.b, hit ? 0.9f : 0.3f);
            pathLine.enabled = true;

            pathShownTime = Time.time;
            pathFading = fading;
        }

        private void EnsurePathLine()
        {
            if (pathLine != null)
                return;

            // 道具が消えても少し残せるよう、道具の子にはしない
            var go = new GameObject($"{displayName} Throw Path");
            pathLine = go.AddComponent<LineRenderer>();
            pathLine.useWorldSpace = true;
            pathLine.sharedMaterial = LineMaterial;
            pathLine.widthCurve = new AnimationCurve(new Keyframe(0.0f, 0.015f), new Keyframe(1.0f, 0.04f));
            pathLine.numCapVertices = 2;
            pathLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pathLine.receiveShadows = false;
            pathLine.enabled = false;
        }

        // ---- 軌跡 ----

        private void UpdateTrail()
        {
            if (trail == null)
            {
                trail = gameObject.AddComponent<TrailRenderer>();
                trail.sharedMaterial = LineMaterial;
                trail.time = 0.35f;
                trail.minVertexDistance = 0.05f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0.0f, trailWidth), new Keyframe(1.0f, 0.0f));
                trail.colorGradient = new Gradient
                {
                    colorKeys = new[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(Color.white, 1.0f) },
                    alphaKeys = new[] { new GradientAlphaKey(0.7f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) },
                };
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.emitting = false;
            }

            bool enabled = Manager != null && Manager.showTrail;
            trail.emitting = enabled && !IsHeld && !body.isKinematic && body.linearVelocity.sqrMagnitude > 4.0f;
        }

        private static Material LineMaterial
        {
            get
            {
                // 頂点カラーと透明度が使え、どのパイプラインでも表示できる
                if (lineMaterial == null)
                    lineMaterial = new Material(Shader.Find("Sprites/Default"));
                return lineMaterial;
            }
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
            if (IsHeld || spawnedByDice)
                return;

            inFlight = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = spawnPosition;
            body.rotation = spawnRotation;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            if (trail != null)
                trail.Clear();
        }
    }
}
