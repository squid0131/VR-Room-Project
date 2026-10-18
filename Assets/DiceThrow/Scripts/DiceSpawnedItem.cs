using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DiceThrow
{
    /// <summary>
    /// さいころで出した道具。掴まれるまでは右手のすぐ上に浮かんで付いてきて、掴まれたら通常の道具として投げられる
    /// </summary>
    [RequireComponent(typeof(ThrowableItem))]
    public class DiceSpawnedItem : MonoBehaviour
    {
        public enum State { Floating, Held, Released }

        public State CurrentState { get; private set; } = State.Floating;

        private const float PopDuration = 0.15f;

        private DiceRoller roller = null;
        private Rigidbody body = null;
        private XRGrabInteractable grab = null;
        private bool upright = false;
        private float halfHeight = 0.0f;
        private Vector3 baseScale = Vector3.one;
        private float spawnTime = 0.0f;

        public void Init(DiceRoller owner)
        {
            roller = owner;
            body = GetComponent<Rigidbody>();
            grab = GetComponent<XRGrabInteractable>();

            var item = GetComponent<ThrowableItem>();
            item.spawnedByDice = true;
            upright = item.spawnUpright;

            Pose pose = roller.GetFloatPose(0.0f, upright);
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            halfHeight = MeasureHalfHeight();

            baseScale = transform.localScale;
            transform.localScale = baseScale * 0.01f;
            spawnTime = Time.time;

            grab.selectEntered.AddListener(OnGrabbed);
            grab.selectExited.AddListener(OnReleased);

            BeginFloating();
        }

        /// <summary>右手の上に浮かんで待つ状態にする</summary>
        private void BeginFloating()
        {
            // 浮かんでいる間は物理で動かない。掴まれると XRI が動かし、離したときに下で戻す
            transform.SetParent(null);
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
            body.useGravity = false;
            CurrentState = State.Floating;
        }

        private float MeasureHalfHeight()
        {
            // 見た目のメッシュだけで測る（ロープの LineRenderer や軌跡の TrailRenderer は位置が原点のままなので含めない）
            var renderers = GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0)
                return 0.0f;

            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);

            // 中心が道具の原点からずれていても下端が右手の上に来るようにする
            return transform.position.y - bounds.min.y;
        }

        private void Update()
        {
            // 出現時にポンと膨らむ
            if (CurrentState != State.Floating || transform.localScale == baseScale)
                return;

            float t = Mathf.Clamp01((Time.time - spawnTime) / PopDuration);
            transform.localScale = baseScale * Mathf.Max(0.01f, 1.0f - Mathf.Pow(1.0f - t, 3.0f));
        }

        private void FixedUpdate()
        {
            if (CurrentState != State.Floating || roller == null)
                return;

            // 右手に付いてくる。ふわふわ上下させて「取れる」ことを見せる
            Pose pose = roller.GetFloatPose(halfHeight, upright);
            pose.position += Vector3.up * (Mathf.Sin(Time.time * 4.0f) * 0.01f);
            body.MovePosition(pose.position);
            body.MoveRotation(pose.rotation);
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            if (CurrentState == State.Floating)
                transform.localScale = baseScale;

            CurrentState = State.Held;
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            // XRI は掴んだ時点の状態（浮遊中や刺さっていたときの kinematic・親子関係）に戻すので、ここで通常の物体にする。
            // 投げる速度はこの後（同じフレームの終わり）に XRI が与える
            transform.SetParent(null);
            body.isKinematic = false;
            body.useGravity = true;

            CurrentState = State.Released;
            if (roller != null)
                roller.OnItemReleased(this);
        }

        /// <summary>
        /// 投げた道具を右手に戻す（盾のブーメラン用）。手元に別の道具があれば消える
        /// </summary>
        public void ReturnToHand()
        {
            if (roller != null && roller.TryReturnToHand(this))
                BeginFloating();
            else
                Vanish();
        }

        /// <summary>道具を消す。手に持っていれば手から外してから消す</summary>
        public void Vanish()
        {
            if (grab != null && grab.isSelected && grab.interactionManager != null)
                grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);

            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (roller != null)
                roller.OnItemDestroyed(this);
        }
    }
}
