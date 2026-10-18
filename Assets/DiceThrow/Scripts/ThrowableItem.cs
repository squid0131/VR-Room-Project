using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace DiceThrow
{
    /// <summary>
    /// 掴んで投げられる道具。重さに応じて、投げたときの速さと持ったときの重さの感じが変わる。
    /// 掴んだ位置から手を動かした向きと距離（Range）、または振った勢いと向き（Swing）で投げる速さを決め、
    /// 飛ぶ道筋と軌跡を表示する
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

        [Tooltip("持つ位置と向き（盾など）。空なら掴んだ位置のまま持つ")]
        public Transform gripPoint = null;

        private Rigidbody body = null;
        private XRGrabInteractable grab = null;

        private Vector3 spawnPosition = Vector3.zero;
        private Quaternion spawnRotation = Quaternion.identity;

        // 手を離してから最初に何かに当たるまでの間 true
        private bool inFlight = false;
        private Vector3 releasePosition = Vector3.zero;

        // 投げる設定（ApplySettings で決まる）
        private float throwScale = 1.0f;
        private float weightFactor = 1.0f;

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

        // 範囲で投げる（Range）：基準の位置（XR リグの中の座標）。掴んだ位置から始まり、手を後ろへ引くと付いてくる
        private Vector3 rangeAnchor = Vector3.zero;
        private ThrowRangeGuide rangeGuide = null;
        private HapticImpulsePlayer holdingHaptics = null;
        private int powerStep = 0;
        private Transform head = null;

        // 持っている間の動き。長い道具（やり）は物に当たらず手にぴったり付いてくる（Instantaneous）
        private const float LongItemLength = 1.0f;
        private float itemLength = 0.0f;
        private float weightSmoothing = 20.0f;
        private Vector3 lastRigPosition = Vector3.zero;
        private float lastRigYaw = 0.0f;

        // 持っている道具がプレイヤーの体（CharacterController）にぶつかって移動を邪魔しないようにする
        private CharacterController playerBody = null;
        private Collider[] ownColliders = null;
        private bool ignoringPlayer = false;

        // 持っている間は、スティックで道具を回したり近づけたりする XRI の機能（manipulation）を止める。
        // 右スティックの旋回と重なって、道具が回り続けて震えるため
        private InteractionAttachController attachController = null;
        private bool hadManipulation = false;

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
            ownColliders = GetComponentsInChildren<Collider>(true);
            itemLength = MeasureLength();

            grab.selectEntered.AddListener(args =>
            {
                inFlight = false;
                throwPending = false;
                holdingHand = args.interactorObject.transform;
                var origin = holdingHand.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
                handSpace = origin != null ? origin.transform : null;
                handCount = 0;
                handNewest = -1;

                holdingHaptics = holdingHand.GetComponentInParent<HapticImpulsePlayer>();
                rangeAnchor = LocalHandPosition();
                powerStep = 0;

                if (handSpace != null)
                {
                    lastRigPosition = handSpace.position;
                    lastRigYaw = handSpace.eulerAngles.y;
                }
                playerBody = origin != null ? origin.GetComponent<CharacterController>() : null;
                SetIgnorePlayer(true);

                attachController = (args.interactorObject as NearFarInteractor)?.interactionAttachController as InteractionAttachController;
                if (attachController != null)
                {
                    hadManipulation = attachController.useManipulationInput;
                    attachController.useManipulationInput = false;
                }
            });

            grab.selectExited.AddListener(_ =>
            {
                inFlight = true;
                releasePosition = transform.position;

                RecordHand();
                pendingHandVelocity = Control == ThrowControl.Range ? EstimateRangeVelocity(out float _) :EstimateHandVelocity();
                throwPending = true;
                holdingHand = null;
                holdingHaptics = null;
                if (attachController != null)
                    attachController.useManipulationInput = hadManipulation;
                attachController = null;
                if (rangeGuide != null)
                    rangeGuide.Hide();
            });
        }

        private void Start()
        {
            ApplySettings();

            if (Camera.main != null)
                head = Camera.main.transform;
        }

        private void OnDestroy()
        {
            // 消えても、投げた道筋は残りの時間だけ表示しておく
            if (pathLine != null)
                Destroy(pathLine.gameObject, pathFading ? Mathf.Max(0.0f, PathFadeTime - (Time.time - pathShownTime)) : 0.0f);
            if (rangeGuide != null)
                Destroy(rangeGuide.gameObject);
        }

        private static ThrowTestManager Manager => ThrowTestManager.Instance;
        private static ThrowControl Control => Manager != null ? Manager.throwControl : ThrowControl.Range;
        private static bool UseCustomThrow => Control != ThrowControl.Standard;
        private static float MinThrowSpeed => Manager != null ? Manager.minThrowSpeed : 1.0f;
        private static float SwingWindow => Manager != null ? Manager.swingWindow : 0.12f;
        private static float RangeRadius => Manager != null ? Manager.rangeRadius : 0.4f;
        private static float RangeDeadZone => Manager != null ? Manager.rangeDeadZone : 0.06f;
        private static float RangeMinDistance => Manager != null ? Manager.rangeMinDistance : 1.0f;
        private static float RangeMaxDistance => Manager != null ? Manager.rangeMaxDistance : 10.0f;
        private static bool RangeFollowPullBack => Manager == null || Manager.rangeFollowPullBack;

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
            weightFactor = Mathf.Clamp(Mathf.Pow(referenceMass / mass, weightInfluence), 0.3f, 2.0f);
            throwScale = throwPower * weightFactor;

            // 長い道具は、持っている間に床や机・敵に当たって向きが乱れないよう、物理を使わず手に付ける
            grab.movementType = itemLength > LongItemLength
                ? XRBaseInteractable.MovementType.Instantaneous
                : XRBaseInteractable.MovementType.VelocityTracking;

            // 持つ位置が決まっている道具（盾）は、どこを掴んでもその位置で持つ
            grab.useDynamicAttach = gripPoint == null;
            if (gripPoint != null)
                grab.attachTransform = gripPoint;

            // 離れたところからレイで掴んでも、手元に引き寄せて持つ（離れたまま持つと、スティックで回せてしまう）
            grab.farAttachMode = InteractableFarAttachMode.Near;

            grab.throwOnDetach = !UseCustomThrow;
            grab.throwVelocityScale = throwScale;
            grab.throwAngularVelocityScale = 1.0f;

            // 重いものほど手の動きに遅れてついてくる
            float heaviness = Mathf.InverseLerp(0.2f, 4.0f, mass);
            weightSmoothing = Mathf.Lerp(20.0f, 5.0f, heaviness);
            grab.smoothPosition = true;
            grab.smoothPositionAmount = weightSmoothing;
            grab.smoothRotation = true;
            grab.smoothRotationAmount = weightSmoothing;
        }

        /// <summary>見た目のいちばん長い辺の長さ（m）。拡大縮小される前（Awake）に測る</summary>
        private float MeasureLength()
        {
            var renderers = GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0)
                return 0.0f;

            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);
            return Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        }

        /// <summary>
        /// スティック移動や旋回をしている間は、重さによる遅れをなくす。
        /// 遅れは手の動きに対して付けたいが、XRI はワールド座標で遅らせるので、移動中は道具が後ろに引きずられてしまう
        /// </summary>
        private void UpdateHoldSmoothing()
        {
            if (handSpace == null || Time.deltaTime <= 0.0f)
                return;

            Vector3 position = handSpace.position;
            float yaw = handSpace.eulerAngles.y;
            float moveSpeed = (position - lastRigPosition).magnitude / Time.deltaTime;
            float turnSpeed = Mathf.Abs(Mathf.DeltaAngle(lastRigYaw, yaw)) / Time.deltaTime;
            lastRigPosition = position;
            lastRigYaw = yaw;

            bool moving = moveSpeed > 0.05f || turnSpeed > 5.0f;
            float amount = moving ? 40.0f : weightSmoothing;
            grab.smoothPositionAmount = amount;
            grab.smoothRotationAmount = amount;
        }

        private void SetIgnorePlayer(bool ignore)
        {
            if (playerBody == null || ignoringPlayer == ignore)
                return;

            ignoringPlayer = ignore;
            foreach (var c in ownColliders)
            {
                if (c != null)
                    Physics.IgnoreCollision(c, playerBody, ignore);
            }
        }

        /// <summary>離した後、プレイヤーの体から離れたら、また当たるようにする（体の中で離すと弾き出されるため）</summary>
        private void RestorePlayerCollision()
        {
            if (!ignoringPlayer || IsHeld || playerBody == null)
                return;

            Bounds player = playerBody.bounds;
            foreach (var c in ownColliders)
            {
                if (c != null && c.enabled && c.bounds.Intersects(player))
                    return;
            }
            SetIgnorePlayer(false);
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
                if (UseCustomThrow)
                    ApplyCustomThrow();
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
        /// 動かした向きと距離（Range）、または振った勢いと向き（Swing）で飛ばす。
        /// XRI の標準の投げ方は離す瞬間の手の動きを使うため、手首のぶれで狙いが外れやすい
        /// </summary>
        private void ApplyCustomThrow()
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

            bool thrown = Control == ThrowControl.Range
                ? velocity.sqrMagnitude > 0.0001f
                : velocity.magnitude >= MinThrowSpeed * throwScale;
            if (thrown)
                ShowPath(velocity, fading: true, onTarget: target != null);
        }

        /// <summary>
        /// 実際に飛ばす初速を決める（Swing は投擲力と重さの倍率、どちらも狙いの補正）。
        /// throwVelocity は Swing なら手の速さ、Range なら EstimateRangeVelocity で決めた初速
        /// </summary>
        private Vector3 ComputeLaunchVelocity(Vector3 throwVelocity, out ThrowTarget target)
        {
            target = null;

            // さいころを振っている間は投げられない（その場に落ちる）
            if (DiceRoller.BlocksThrowing)
                return Vector3.zero;

            Vector3 velocity;
            if (Control == ThrowControl.Range)
            {
                // 置くだけの範囲で離したときは 0
                if (throwVelocity == Vector3.zero)
                    return Vector3.zero;
                velocity = throwVelocity;
            }
            else
            {
                // ゆっくり離したときは投げずにそっと置く
                if (throwVelocity.magnitude < MinThrowSpeed)
                    return throwVelocity;
                velocity = throwVelocity * throwScale;
            }

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
                grab.throwOnDetach = !UseCustomThrow && !DiceRoller.BlocksThrowing;

            if (IsHeld)
            {
                RecordHand();
                UpdateHoldSmoothing();
                if (Control == ThrowControl.Range)
                    FollowPullBack();
            }
            else
            {
                RestorePlayerCollision();
            }

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

        /// <summary>持っている手の位置（XR リグの中の座標）</summary>
        private Vector3 LocalHandPosition()
        {
            return handSpace != null ? handSpace.InverseTransformPoint(holdingHand.position) : holdingHand.position;
        }

        private void RecordHand()
        {
            if (holdingHand == null)
                return;

            handNewest = (handNewest + 1) % HandSampleCount;
            handPositions[handNewest] = LocalHandPosition();
            handTimes[handNewest] = Time.time;
            handCount = Mathf.Min(handCount + 1, HandSampleCount);
        }

        // ---- 範囲で投げる（Range） ----

        /// <summary>
        /// 手が基準の位置より後ろ（顔の向きの反対側）にある間は、基準の位置を手に合わせる。
        /// 振りかぶる動きでは力がたまらず、前に出し始めた位置から力が決まる
        /// </summary>
        private void FollowPullBack()
        {
            if (!RangeFollowPullBack || holdingHand == null || head == null)
                return;

            Vector3 forward = handSpace != null ? handSpace.InverseTransformDirection(head.forward) : head.forward;
            forward.y = 0.0f;
            if (forward.sqrMagnitude < 0.001f)
                return;

            Vector3 hand = LocalHandPosition();
            if (Vector3.Dot(hand - rangeAnchor, forward) < 0.0f)
                rangeAnchor = hand;
        }

        /// <summary>
        /// 基準の位置から今の手の位置への向きと距離で、飛ばす初速（ワールド座標）を決める。
        /// 力から飛距離の目安を決め、約 45 度で投げたときにその距離に届く速さにする（重さで距離が変わる）。
        /// 置くだけの範囲なら 0。power は 0〜1
        /// </summary>
        private Vector3 EstimateRangeVelocity(out float power)
        {
            power = 0.0f;
            if (holdingHand == null)
                return Vector3.zero;

            Vector3 offset = LocalHandPosition() - rangeAnchor;
            float distance = offset.magnitude;
            float deadZone = RangeDeadZone;
            if (distance <= deadZone)
                return Vector3.zero;

            float radius = Mathf.Max(RangeRadius, deadZone + 0.01f);
            power = Mathf.Clamp01((distance - deadZone) / (radius - deadZone));

            // 平らな地面で 45 度に投げたときの飛距離は 速さ² ÷ 重力。軽いものほど遠く、重いものほど短く飛ぶ
            float distanceGoal = Mathf.Lerp(RangeMinDistance, Mathf.Max(RangeMinDistance, RangeMaxDistance), power) * weightFactor;
            float speed = Mathf.Sqrt(Mathf.Max(0.1f, distanceGoal) * Physics.gravity.magnitude);

            Vector3 direction = offset / distance;
            if (handSpace != null)
                direction = handSpace.TransformDirection(direction);
            return direction * speed;
        }

        /// <summary>力の範囲の円と力のゲージを表示する</summary>
        private void UpdateRangeGuide(float power)
        {
            bool show = Manager != null && Manager.showRangeGuide && IsHeld && holdingHand != null
                && Control == ThrowControl.Range && !DiceRoller.BlocksThrowing;
            if (!show)
            {
                if (rangeGuide != null)
                    rangeGuide.Hide();
                return;
            }

            if (rangeGuide == null)
                rangeGuide = ThrowRangeGuide.Create($"{displayName} Throw Range", LineMaterial);

            Vector3 center = handSpace != null ? handSpace.TransformPoint(rangeAnchor) : rangeAnchor;
            Vector3 up = handSpace != null ? handSpace.up : Vector3.up;
            Vector3 forward = head != null ? Vector3.ProjectOnPlane(head.forward, up) : transform.forward;
            float scale = handSpace != null ? handSpace.lossyScale.y : 1.0f;
            rangeGuide.Show(center, up, forward, RangeRadius * scale, RangeDeadZone * scale, holdingHand.position, power);
        }

        /// <summary>力が 25% 上がるごとに軽く振動し、最大になったら強めに振動する</summary>
        private void UpdatePowerHaptics(float power)
        {
            int step = Mathf.FloorToInt(power * 4.0f + 0.0001f);
            if (step > powerStep && holdingHaptics != null)
            {
                if (step >= 4)
                    holdingHaptics.SendHapticImpulse(0.6f, 0.06f);
                else
                    holdingHaptics.SendHapticImpulse(0.1f + 0.08f * step, 0.02f);
            }
            powerStep = step;
        }

        /// <summary>振った速さ（ワールド座標の向き）</summary>
        private Vector3 EstimateHandVelocity()
        {
            Vector3 local = ThrowPhysics.EstimateThrowVelocity(handPositions, handTimes, handCount, handNewest, SwingWindow);
            return handSpace != null ? handSpace.TransformVector(local) : local;
        }

        // ---- 道筋の表示 ----

        /// <summary>
        /// 投げようとしている間、今離したら飛ぶ道筋を表示する。的に向かうときは緑、それ以外は白。
        /// Range では置くだけの範囲の外に手を出している間、Swing では振っている間に表示する
        /// </summary>
        private void UpdatePreview()
        {
            bool showTrajectory = Manager != null && Manager.showTrajectory;
            bool canThrow = IsHeld && UseCustomThrow && !DiceRoller.BlocksThrowing;

            float power = 0.0f;
            Vector3 rangeVelocity = Vector3.zero;
            if (canThrow && Control == ThrowControl.Range)
                rangeVelocity = EstimateRangeVelocity(out power);

            UpdateRangeGuide(power);
            UpdatePowerHaptics(canThrow ? power : 0.0f);

            if (showTrajectory && canThrow)
            {
                if (Control == ThrowControl.Range)
                {
                    if (rangeVelocity != Vector3.zero)
                    {
                        Vector3 velocity = ComputeLaunchVelocity(rangeVelocity, out ThrowTarget target);
                        ShowPath(velocity, fading: false, onTarget: target != null);
                        return;
                    }
                }
                else if (handCount >= 2)
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
