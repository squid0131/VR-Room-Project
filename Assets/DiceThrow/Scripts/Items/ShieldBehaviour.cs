using System.Collections.Generic;
using UnityEngine;

namespace DiceThrow
{
    /// <summary>
    /// 盾（出目 5・防御）。構えると攻撃を防ぐ。投げるとブーメランのように回転して飛び、敵に当たりながら右手に戻ってくる
    /// </summary>
    public class ShieldBehaviour : ItemBehaviour
    {
        private enum Phase { None, Outgoing, Returning }

        private static readonly List<ShieldBehaviour> All = new List<ShieldBehaviour>();

        [Tooltip("当たったときのダメージ")]
        public int damage = 1;

        [Tooltip("この速さ（m/s）より遅く離したときは、ただ落とす")]
        public float minThrowSpeed = 2.0f;

        [Tooltip("投げてから戻り始めるまでの秒数")]
        public float outgoingTime = 0.5f;

        [Tooltip("戻ってくる速さ（m/s）")]
        public float returnSpeed = 12.0f;

        [Tooltip("戻るときの曲がりやすさ。大きいほど急に向きを変える")]
        public float returnSteering = 40.0f;

        [Tooltip("回転の速さ（rad/s）")]
        public float spinSpeed = 25.0f;

        [Tooltip("この秒数で戻れなかったら、あきらめて落ちる")]
        public float maxFlightTime = 4.0f;

        [Tooltip("構えた盾で防げる、頭からの距離（m）")]
        public float blockDistance = 0.9f;

        private Phase phase = Phase.None;
        private float phaseStartTime = 0.0f;
        private Collider[] colliders = null;
        private readonly HashSet<Damageable> hitThisThrow = new HashSet<Damageable>();

        protected override void Awake()
        {
            base.Awake();
            colliders = GetComponentsInChildren<Collider>();
        }

        private void OnEnable()
        {
            All.Add(this);
        }

        private void OnDisable()
        {
            All.Remove(this);
        }

        /// <summary>
        /// 頭（head）から見て source の方向に、手で構えた盾があるか
        /// </summary>
        public static bool IsBlocking(Vector3 head, Vector3 source)
        {
            Vector3 toSource = (source - head).normalized;
            foreach (var shield in All)
            {
                if (!shield.IsHeld)
                    continue;

                Vector3 toShield = shield.transform.position - head;
                if (toShield.magnitude < shield.blockDistance && Vector3.Dot(toShield.normalized, toSource) > 0.4f)
                    return true;
            }
            return false;
        }

        protected override void OnGrabbed()
        {
            EndFlight();
        }

        protected override void OnReleased()
        {
            if (Body.linearVelocity.magnitude < minThrowSpeed)
                return;

            hitThisThrow.Clear();
            phase = Phase.Outgoing;
            phaseStartTime = Time.time;
            Body.useGravity = false;

            // 縦に構えていても、投げたら水平に寝かせてフリスビーのように回す（戻ると右手の前で縦に戻る）
            Vector3 flat = Vector3.ProjectOnPlane(Body.linearVelocity, Vector3.up);
            if (flat.sqrMagnitude > 0.01f)
                Body.rotation = Quaternion.LookRotation(flat, Vector3.up);
            Body.angularVelocity = transform.up * spinSpeed;
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();

            if (phase == Phase.None)
                return;

            if (Time.time - ReleaseTime > maxFlightTime)
            {
                EndFlight();
                return;
            }

            Body.angularVelocity = transform.up * spinSpeed;

            if (phase == Phase.Outgoing)
            {
                // 少しずつ減速しながら飛ぶ
                Body.linearVelocity *= 1.0f - 1.5f * Time.fixedDeltaTime;
                if (Time.time - phaseStartTime > outgoingTime)
                    StartReturning();
            }
            else
            {
                var hand = GetReturnTarget();
                Vector3 toHand = hand - transform.position;
                if (toHand.magnitude < 0.35f)
                {
                    Catch();
                    return;
                }

                Vector3 desired = toHand.normalized * returnSpeed;
                Body.linearVelocity = Vector3.MoveTowards(Body.linearVelocity, desired, returnSteering * Time.fixedDeltaTime);
            }
        }

        protected override void OnImpact(Collision collision)
        {
            HandleHit(collision.collider, collision.GetContact(0).point);
            if (phase == Phase.Outgoing)
                StartReturning();
        }

        protected override void OnLaterCollision(Collision collision)
        {
            if (phase == Phase.None)
                return;

            HandleHit(collision.collider, collision.GetContact(0).point);
            if (phase == Phase.Outgoing)
                StartReturning();
        }

        private void OnTriggerEnter(Collider other)
        {
            // 戻るときは物をすり抜けるが、敵には当たる
            if (phase == Phase.Returning)
                HandleHit(other, transform.position);
        }

        private void HandleHit(Collider other, Vector3 point)
        {
            var target = other.GetComponentInParent<Damageable>();
            if (target == null || hitThisThrow.Contains(target))
                return;

            hitThisThrow.Add(target);
            target.TakeDamage(damage, point, Body.linearVelocity.normalized);
        }

        private void StartReturning()
        {
            phase = Phase.Returning;
            phaseStartTime = Time.time;
            hitThisThrow.Clear();
            SetTrigger(true);
        }

        private void Catch()
        {
            EndFlight();

            var spawned = GetComponent<DiceSpawnedItem>();
            if (spawned != null)
                spawned.ReturnToHand();
        }

        private void EndFlight()
        {
            if (phase == Phase.None)
                return;

            phase = Phase.None;
            SetTrigger(false);
            Body.useGravity = true;
        }

        private void SetTrigger(bool trigger)
        {
            foreach (var c in colliders)
                c.isTrigger = trigger;
        }

        private DiceRoller roller = null;

        private Vector3 GetReturnTarget()
        {
            if (roller == null)
                roller = FindAnyObjectByType<DiceRoller>();
            if (roller != null)
                return roller.RightHand.position;
            return PlayerRig.Instance != null ? PlayerRig.Instance.HeadPosition : transform.position;
        }
    }
}
