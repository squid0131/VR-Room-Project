using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DiceThrow
{
    /// <summary>
    /// 道具ごとの「投げた後の挙動」の共通部分。掴む・離す・最初に何かに当たる、を各道具に知らせる
    /// </summary>
    [RequireComponent(typeof(ThrowableItem))]
    public abstract class ItemBehaviour : MonoBehaviour
    {
        protected Rigidbody Body { get; private set; }
        protected XRGrabInteractable Grab { get; private set; }

        public bool IsHeld => Grab != null && Grab.isSelected;

        /// <summary>手を離してから最初に何かに当たるまでの間 true</summary>
        public bool InFlight { get; protected set; } = false;

        /// <summary>離す直前（＝手で持っていた最後）の速さ。当たったときの勢いの判定に使う</summary>
        protected Vector3 LastVelocity { get; private set; } = Vector3.zero;

        protected float ReleaseTime { get; private set; } = 0.0f;

        private bool releasePending = false;
        private bool vanishing = false;

        protected virtual void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Grab = GetComponent<XRGrabInteractable>();

            Grab.selectEntered.AddListener(_ =>
            {
                releasePending = false;
                InFlight = false;
                OnGrabbed();
            });

            // 投げる速さは XRI がこのフレームの最後に与えるので、次の FixedUpdate で「投げた」として扱う
            Grab.selectExited.AddListener(_ => releasePending = true);
        }

        protected virtual void FixedUpdate()
        {
            if (releasePending)
            {
                releasePending = false;
                InFlight = true;
                ReleaseTime = Time.time;
                OnReleased();
            }

            if (!Body.isKinematic)
                LastVelocity = Body.linearVelocity;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (IsHeld || vanishing)
                return;

            if (InFlight)
            {
                InFlight = false;
                OnImpact(collision);
            }
            else
            {
                OnLaterCollision(collision);
            }
        }

        /// <summary>掴まれたとき</summary>
        protected virtual void OnGrabbed() { }

        /// <summary>手を離したとき（投げる速さはもう Body に入っている）</summary>
        protected virtual void OnReleased() { }

        /// <summary>離してから最初に何かに当たったとき</summary>
        protected virtual void OnImpact(Collision collision) { }

        /// <summary>2 回目以降に当たったとき</summary>
        protected virtual void OnLaterCollision(Collision collision) { }

        /// <summary>当たったときの勢い（m/s）</summary>
        protected float ImpactSpeed(Collision collision)
        {
            return Mathf.Max(collision.relativeVelocity.magnitude, LastVelocity.magnitude);
        }

        /// <summary>少し縮んでから消える</summary>
        protected void Vanish(float delay = 0.0f)
        {
            if (vanishing)
                return;
            vanishing = true;
            StartCoroutine(VanishRoutine(delay));
        }

        /// <summary>すぐに消える</summary>
        public void VanishNow()
        {
            vanishing = true;
            var spawned = GetComponent<DiceSpawnedItem>();
            if (spawned != null)
                spawned.Vanish();
            else
                Destroy(gameObject);
        }

        private IEnumerator VanishRoutine(float delay)
        {
            if (delay > 0.0f)
                yield return new WaitForSeconds(delay);

            Vector3 scale = transform.localScale;
            for (float t = 0.0f; t < 0.2f; t += Time.deltaTime)
            {
                transform.localScale = scale * (1.0f - t / 0.2f);
                yield return null;
            }

            VanishNow();
        }
    }
}
