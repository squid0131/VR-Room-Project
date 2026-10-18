using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace DiceThrow
{
    /// <summary>
    /// 盾（出目 5・防御）。近距離での守りと反撃に使う。
    /// - 構える：攻撃の来る向きに構えると防ぐ。殴ってきた敵は押し戻されて怯み、ダメージを受ける（突き出しながら受けると強く跳ね返す）
    /// - 殴る：持ったまま勢いよく敵にぶつけると、ダメージを与えて押し戻す
    /// - 跳ね返す：敵の弾を構えた盾で受けると、投げた敵へ跳ね返る
    /// - 投げる：ブーメランのように回転して飛び、敵に当たりながら右手に戻ってくる
    /// </summary>
    public class ShieldBehaviour : ItemBehaviour
    {
        private enum Phase { None, Outgoing, Returning }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly List<ShieldBehaviour> All = new List<ShieldBehaviour>();
        private static readonly Collider[] OverlapBuffer = new Collider[16];

        [Header("構える")]
        [Tooltip("構えた盾で防げる、頭からの距離（m）")]
        public float blockDistance = 0.9f;

        [Tooltip("頭から見て、攻撃の来る向きと盾の向きの差がこの角度（度）以内なら防ぐ")]
        [Range(10, 90)] public float blockAngle = 70.0f;

        [Tooltip("殴ってきた敵に跳ね返すダメージ")]
        public int counterDamage = 1;

        [Tooltip("跳ね返したとき、敵を押し戻す速さ（m/s）")]
        public float counterKnockback = 5.0f;

        [Tooltip("跳ね返したとき、敵が怯んで動けない秒数")]
        public float counterStunTime = 1.2f;

        [Tooltip("受ける瞬間に盾をこの速さ（m/s）以上で敵のほうへ突き出すと、強く跳ね返す（ダメージ 2 倍・怯み 1.7 倍）")]
        public float parrySpeed = 1.2f;

        [Tooltip("弾を跳ね返せる盾の半径（m）")]
        public float faceRadius = 0.35f;

        [Header("持ち方")]
        [Tooltip("盾の面から手（コントローラー）までの距離（m）。手は盾の裏側にある")]
        public float gripDepth = 0.07f;

        [Tooltip("手から盾の中心までの高さ（m）。0 で手の高さに盾の中心が来る")]
        public float gripHeight = 0.05f;

        [Header("持ったまま殴る")]
        [Tooltip("盾をこの速さ（m/s）以上で動かして敵にぶつけると殴る")]
        public float bashSpeed = 2.0f;

        [Tooltip("殴ったときのダメージ")]
        public int bashDamage = 1;

        [Tooltip("殴ったとき、敵を押し戻す速さ（m/s）")]
        public float bashKnockback = 4.0f;

        [Tooltip("殴ったとき、敵が怯んで動けない秒数")]
        public float bashStunTime = 0.8f;

        [Tooltip("殴る判定の半径（m）")]
        public float bashRadius = 0.4f;

        [Tooltip("同じ敵を続けて殴れるまでの秒数")]
        public float bashCooldown = 0.6f;

        [Header("投げる")]
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

        private Phase phase = Phase.None;
        private float phaseStartTime = 0.0f;
        private Collider[] colliders = null;
        private readonly HashSet<Damageable> hitThisThrow = new HashSet<Damageable>();
        private readonly Dictionary<Damageable, float> lastBashTimes = new Dictionary<Damageable, float>();

        private Renderer[] renderers = null;
        private MaterialPropertyBlock block = null;
        private float flashTime = -10.0f;

        /// <summary>盾の面の向き（円盤のローカル Y）</summary>
        public Vector3 FaceNormal => transform.up;

        protected override void Awake()
        {
            base.Awake();
            colliders = GetComponentsInChildren<Collider>();
            renderers = GetComponentsInChildren<MeshRenderer>();
            block = new MaterialPropertyBlock();
            CreateGrip();
        }

        /// <summary>
        /// 勇者の盾のように、盾の裏の真ん中あたりを握る持ち方にする。
        /// コントローラーの前（+Z）が盾の面の向き（円盤のローカル +Y）になり、拳を前に向けると盾が正面を向く
        /// </summary>
        private void CreateGrip()
        {
            var item = GetComponent<ThrowableItem>();
            if (item == null || item.gripPoint != null)
                return;

            var grip = new GameObject("Grip").transform;
            grip.SetParent(transform, false);
            // 手は面の裏（-Y）に、盾の中心より少し下（-Z）に来る
            grip.localPosition = new Vector3(0.0f, -gripDepth, -gripHeight);
            grip.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            item.gripPoint = grip;
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
        /// 頭（head）から見て source の方向に手で構えた盾があれば防ぐ。
        /// attacker（殴ってきた敵）がいれば跳ね返す（countered = true）。防いだら true
        /// </summary>
        public static bool TryBlock(Vector3 head, Vector3 source, Component attacker, out bool countered)
        {
            countered = false;
            Vector3 toSource = source - head;
            foreach (var shield in All)
            {
                if (!shield.IsHeld)
                    continue;

                Vector3 toShield = shield.transform.position - head;
                if (toShield.magnitude < shield.blockDistance && Vector3.Angle(toShield, toSource) < shield.blockAngle)
                {
                    countered = shield.Counter(attacker);
                    return true;
                }
            }
            return false;
        }

        /// <summary>from から to へ動く半径 radius の弾が、手で構えた盾に当たるか。当たる盾を返す</summary>
        public static ShieldBehaviour FindHeldShieldNear(Vector3 from, Vector3 to, float radius)
        {
            foreach (var shield in All)
            {
                if (shield.IsHeld && EnemyProjectile.DistanceToSegment(shield.transform.position, from, to) < shield.faceRadius + radius)
                    return shield;
            }
            return null;
        }

        /// <summary>防いだ攻撃を跳ね返す。殴ってきた敵がいれば押し戻して怯ませ、ダメージを与える</summary>
        private bool Counter(Component attacker)
        {
            Flash();
            Pulse(0.7f, 0.12f);

            var enemy = attacker != null ? attacker.GetComponentInParent<SimpleEnemy>() : null;
            if (enemy == null)
                return false;

            Vector3 away = enemy.transform.position - transform.position;
            away.y = 0.0f;
            if (away.sqrMagnitude < 0.0001f)
                away = transform.forward;
            away.Normalize();

            // 受ける瞬間に盾を敵のほうへ突き出していたら強く跳ね返す
            bool parry = Vector3.Dot(Body.linearVelocity, away) > parrySpeed;
            enemy.Repel(away, counterKnockback * (parry ? 1.5f : 1.0f), counterStunTime * (parry ? 1.7f : 1.0f));
            var target = enemy.GetComponent<Damageable>();
            if (target != null)
                target.TakeDamage(counterDamage * (parry ? 2 : 1), enemy.BodyCenter, away);

            if (parry)
                Pulse(1.0f, 0.2f);
            return true;
        }

        /// <summary>敵の弾を跳ね返したとき</summary>
        public void OnReflected()
        {
            Flash();
            Pulse(0.8f, 0.12f);
            PlayerHealth.Instance?.ShowMessage("Reflect!");
        }

        /// <summary>持ったまま勢いよく敵にぶつけたら殴る</summary>
        private void UpdateBash()
        {
            Vector3 velocity = Body.linearVelocity;
            if (velocity.magnitude < bashSpeed)
                return;

            int count = Physics.OverlapSphereNonAlloc(transform.position, bashRadius, OverlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var target = OverlapBuffer[i].GetComponentInParent<Damageable>();
                if (target == null || target.IsDead)
                    continue;
                if (lastBashTimes.TryGetValue(target, out float last) && Time.time - last < bashCooldown)
                    continue;

                lastBashTimes[target] = Time.time;
                target.TakeDamage(bashDamage, OverlapBuffer[i].ClosestPoint(transform.position), velocity.normalized);
                var enemy = target.GetComponent<SimpleEnemy>();
                if (enemy != null)
                    enemy.Repel(velocity, bashKnockback, bashStunTime);

                Flash();
                Pulse(0.9f, 0.1f);
            }
        }

        /// <summary>持っている手を振動させる</summary>
        private void Pulse(float amplitude, float duration)
        {
            if (!IsHeld || Grab.interactorsSelecting.Count == 0)
                return;

            var haptics = Grab.interactorsSelecting[0].transform.GetComponentInParent<HapticImpulsePlayer>();
            if (haptics != null)
                haptics.SendHapticImpulse(amplitude, duration);
        }

        /// <summary>盾を一瞬白く光らせる</summary>
        private void Flash()
        {
            flashTime = Time.time;
        }

        private void Update()
        {
            float t = (Time.time - flashTime) / 0.2f;
            foreach (var r in renderers)
            {
                if (r == null)
                    continue;

                if (t < 1.0f)
                {
                    r.GetPropertyBlock(block);
                    block.SetColor(BaseColorId, Color.Lerp(Color.white, r.sharedMaterial.color, t));
                    r.SetPropertyBlock(block);
                }
                else if (r.HasPropertyBlock())
                {
                    r.SetPropertyBlock(null);
                }
            }
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

            if (IsHeld && !Body.isKinematic)
                UpdateBash();

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
