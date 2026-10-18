using TMPro;
using UnityEngine;

namespace DiceThrow
{
    /// <summary>
    /// 試作用の簡単な敵。近づくと追いかけてきて殴る。リンゴがあればそちらを優先して食べに行き、石の音にも少し気を取られる。
    /// 床の上を真っすぐ歩くだけで、壁は避けない
    /// </summary>
    [RequireComponent(typeof(Damageable))]
    public class SimpleEnemy : MonoBehaviour
    {
        private enum Mode { Idle, Chase, Investigate, Eat, Return, Dead }

        [Tooltip("歩く速さ（m/s）")]
        public float moveSpeed = 1.4f;

        [Tooltip("プレイヤーに気づく距離（m）")]
        public float aggroRadius = 5.0f;

        [Tooltip("殴れる距離（m）")]
        public float attackRange = 1.1f;

        [Tooltip("殴る間隔（秒）")]
        public float attackInterval = 1.5f;

        [Tooltip("殴ったときのダメージ")]
        public int attackDamage = 1;

        [Tooltip("プレイヤーの足元との高さの差がこれ（m）より大きいと、届かないので追わない・攻撃しない（ロープで台に登ったときなど）")]
        public float reachHeight = 1.0f;

        [Header("遠くから投げる攻撃")]
        [Tooltip("オンにすると、離れたところで止まって弾を投げてくる。盾で跳ね返せる")]
        public bool rangedAttack = false;

        [Tooltip("この距離（m）まで近づいたら止まって投げる")]
        public float rangedStopDistance = 5.0f;

        [Tooltip("弾を投げる最大距離（m）")]
        public float rangedRange = 12.0f;

        [Tooltip("弾を投げる間隔（秒）")]
        public float rangedInterval = 2.5f;

        [Tooltip("弾の速さ（m/s）")]
        public float projectileSpeed = 6.0f;

        [Tooltip("弾のダメージ")]
        public int projectileDamage = 1;

        [Tooltip("弾の見た目。空なら体と同じマテリアル")]
        public Material projectileMaterial = null;

        [Tooltip("リンゴを食べ終わるまでの秒数")]
        public float eatDuration = 2.0f;

        [Tooltip("倒されてから復活するまでの秒数（0 なら復活しない）")]
        public float respawnDelay = 6.0f;

        [Tooltip("体力の表示")]
        public TMP_Text healthText = null;

        private Damageable damageable = null;
        private Vector3 home = Vector3.zero;
        private Quaternion homeRotation = Quaternion.identity;
        private Mode mode = Mode.Idle;
        private Lure targetLure = null;
        private float lastAttackTime = -10.0f;
        private float eatStartTime = 0.0f;
        private float deathTime = 0.0f;
        private Vector3 knockback = Vector3.zero;
        private float stunnedUntil = -10.0f;

        public bool IsStunned => Time.time < stunnedUntil;
        public bool IsDead => mode == Mode.Dead;

        /// <summary>弾を撃ち出す位置（目の高さ）</summary>
        public Vector3 ShootPoint => transform.position + Vector3.up * 1.2f + transform.forward * 0.4f;

        /// <summary>弾を当てるときに狙う位置（体の中心）</summary>
        public Vector3 BodyCenter => transform.position + Vector3.up * 0.9f;

        private void Awake()
        {
            damageable = GetComponent<Damageable>();
            damageable.Damaged += OnDamaged;
            damageable.onDied.AddListener(Die);
            home = transform.position;
            homeRotation = transform.rotation;
        }

        private void Start()
        {
            UpdateText();
        }

        private void OnDamaged(int amount, Vector3 point, Vector3 direction)
        {
            direction.y = 0.0f;
            knockback += direction.normalized * 2.0f;

            // 攻撃されたらプレイヤーに気づく
            if (mode == Mode.Idle || mode == Mode.Investigate || mode == Mode.Return)
                mode = Mode.Chase;

            UpdateText();
        }

        private void Update()
        {
            if (mode == Mode.Dead)
            {
                if (respawnDelay > 0.0f && Time.time - deathTime > respawnDelay)
                    Respawn();
                return;
            }

            // 跳ね返されて怯んでいる間は動かず、攻撃もしない（押し戻されるだけ）
            if (IsStunned)
            {
                ApplyKnockback();
                return;
            }

            Vector3 player = PlayerRig.Instance != null ? PlayerRig.Instance.HeadPosition : transform.position;
            float playerDistance = HorizontalDistance(player, transform.position);

            // 高いところ（ロープで登った台など）にいるプレイヤーには届かないので、いないものとして扱う
            bool reachable = PlayerRig.Instance == null || Mathf.Abs(PlayerRig.Instance.FeetHeight - transform.position.y) <= reachHeight;
            bool playerAlive = (PlayerHealth.Instance == null || !PlayerHealth.Instance.IsDead) && reachable;

            // 優先順位: 食べられるもの（リンゴ） > プレイヤー > 音（石）
            var lure = Lure.FindBest(transform.position);
            if (mode == Mode.Eat && targetLure != null && targetLure.isActiveAndEnabled)
            {
                lure = targetLure;
            }
            else if (lure != null && lure.edible)
            {
                SetLure(lure, Mode.Investigate);
            }
            else if (playerAlive && (playerDistance < aggroRadius || mode == Mode.Chase && playerDistance < ChaseRadius))
            {
                mode = Mode.Chase;
            }
            else if (lure != null)
            {
                SetLure(lure, Mode.Investigate);
            }
            else if (mode != Mode.Idle)
            {
                mode = Mode.Return;
            }

            switch (mode)
            {
                case Mode.Chase:
                    float stopDistance = rangedAttack ? rangedStopDistance : attackRange * 0.8f;
                    if (playerDistance > stopDistance)
                        MoveTowards(player);
                    else
                        Face(player);

                    if (playerAlive && playerDistance < attackRange && Time.time - lastAttackTime > attackInterval)
                    {
                        lastAttackTime = Time.time;
                        PlayerHealth.Instance?.TakeDamage(attackDamage, transform.position + Vector3.up * 1.2f, this);
                    }
                    else if (playerAlive && rangedAttack && playerDistance >= attackRange && playerDistance < rangedRange
                        && Time.time - lastAttackTime > rangedInterval)
                    {
                        lastAttackTime = Time.time;
                        Shoot(player);
                    }
                    break;

                case Mode.Investigate:
                    if (targetLure == null || !targetLure.isActiveAndEnabled)
                    {
                        mode = Mode.Return;
                        break;
                    }

                    if (HorizontalDistance(targetLure.transform.position, transform.position) > 0.6f)
                    {
                        MoveTowards(targetLure.transform.position);
                    }
                    else if (targetLure.edible)
                    {
                        mode = Mode.Eat;
                        eatStartTime = Time.time;
                    }
                    break;

                case Mode.Eat:
                    if (targetLure == null || !targetLure.isActiveAndEnabled)
                    {
                        mode = Mode.Return;
                    }
                    else if (Time.time - eatStartTime > eatDuration)
                    {
                        targetLure.Consume();
                        targetLure = null;
                        mode = Mode.Return;
                    }
                    else
                    {
                        // もぐもぐ（上下に揺れる）
                        Face(targetLure.transform.position);
                        transform.position = new Vector3(transform.position.x, home.y + Mathf.Abs(Mathf.Sin(Time.time * 10.0f)) * 0.05f, transform.position.z);
                    }
                    break;

                case Mode.Return:
                    if (HorizontalDistance(home, transform.position) > 0.2f)
                        MoveTowards(home);
                    else
                        mode = Mode.Idle;
                    break;
            }

            ApplyKnockback();
        }

        private float ChaseRadius => rangedAttack ? Mathf.Max(aggroRadius * 2.0f, rangedRange) : aggroRadius * 2.0f;

        private void ApplyKnockback()
        {
            // 当たったときに少し押し戻される
            if (knockback.sqrMagnitude > 0.0001f)
            {
                transform.position += knockback * Time.deltaTime;
                knockback = Vector3.MoveTowards(knockback, Vector3.zero, 8.0f * Time.deltaTime);
            }
        }

        /// <summary>
        /// 盾で跳ね返されたとき。direction の向きに押し戻され、stunTime 秒のあいだ怯んで動けない
        /// </summary>
        public void Repel(Vector3 direction, float speed, float stunTime)
        {
            if (mode == Mode.Dead)
                return;

            direction.y = 0.0f;
            knockback += direction.normalized * speed;
            stunnedUntil = Mathf.Max(stunnedUntil, Time.time + stunTime);
            // 怯みが解けてすぐには殴れない
            lastAttackTime = Mathf.Max(lastAttackTime, stunnedUntil - attackInterval * 0.5f);

            if (mode == Mode.Idle || mode == Mode.Investigate || mode == Mode.Return)
                mode = Mode.Chase;
        }

        /// <summary>プレイヤーの頭をめがけて弾を投げる</summary>
        private void Shoot(Vector3 target)
        {
            Material material = projectileMaterial;
            if (material == null)
            {
                var r = GetComponentInChildren<MeshRenderer>();
                material = r != null ? r.sharedMaterial : null;
            }

            EnemyProjectile.Spawn(this, ShootPoint, target, projectileSpeed, projectileDamage, material);
        }

        private void SetLure(Lure lure, Mode newMode)
        {
            if (targetLure != lure)
                targetLure = lure;
            if (mode != Mode.Eat)
                mode = newMode;
        }

        private void MoveTowards(Vector3 target)
        {
            Vector3 offset = target - transform.position;
            offset.y = 0.0f;
            if (offset.sqrMagnitude < 0.0001f)
                return;

            Vector3 step = Vector3.ClampMagnitude(offset, moveSpeed * Time.deltaTime);
            transform.position = new Vector3(transform.position.x + step.x, home.y, transform.position.z + step.z);
            Face(target);
        }

        private void Face(Vector3 target)
        {
            Vector3 offset = target - transform.position;
            offset.y = 0.0f;
            if (offset.sqrMagnitude < 0.0001f)
                return;

            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(offset), 8.0f * Time.deltaTime);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0.0f;
            b.y = 0.0f;
            return Vector3.Distance(a, b);
        }

        private void Die()
        {
            mode = Mode.Dead;
            deathTime = Time.time;
            targetLure = null;

            // 刺さっているやりは残す
            foreach (var spear in GetComponentsInChildren<SpearBehaviour>())
                spear.Unstick();

            SetVisible(false);
            UpdateText();
        }

        private void Respawn()
        {
            transform.SetPositionAndRotation(home, homeRotation);
            damageable.ResetHealth();
            knockback = Vector3.zero;
            mode = Mode.Idle;
            SetVisible(true);
            UpdateText();
        }

        private void SetVisible(bool visible)
        {
            foreach (var r in GetComponentsInChildren<Renderer>())
                r.enabled = visible;
            foreach (var c in GetComponentsInChildren<Collider>())
                c.enabled = visible;
        }

        private void UpdateText()
        {
            if (healthText != null)
                healthText.text = new string('#', damageable.Health) + new string('-', damageable.maxHealth - damageable.Health);
        }
    }
}
