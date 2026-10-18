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

            Vector3 player = PlayerRig.Instance != null ? PlayerRig.Instance.HeadPosition : transform.position;
            bool playerAlive = PlayerHealth.Instance == null || !PlayerHealth.Instance.IsDead;
            float playerDistance = HorizontalDistance(player, transform.position);

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
            else if (playerAlive && (playerDistance < aggroRadius || mode == Mode.Chase && playerDistance < aggroRadius * 2.0f))
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
                    if (playerDistance > attackRange * 0.8f)
                        MoveTowards(player);
                    else
                        Face(player);

                    if (playerAlive && playerDistance < attackRange && Time.time - lastAttackTime > attackInterval)
                    {
                        lastAttackTime = Time.time;
                        PlayerHealth.Instance?.TakeDamage(attackDamage, transform.position + Vector3.up * 1.2f);
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

            // 当たったときに少し押し戻される
            if (knockback.sqrMagnitude > 0.0001f)
            {
                transform.position += knockback * Time.deltaTime;
                knockback = Vector3.MoveTowards(knockback, Vector3.zero, 8.0f * Time.deltaTime);
            }
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
