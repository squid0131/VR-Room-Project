using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace DiceThrow
{
    /// <summary>
    /// プレイヤーの体力。0 になるとゲームオーバーになり、少し待ってから開始位置で復活する
    /// </summary>
    [RequireComponent(typeof(PlayerRig))]
    public class PlayerHealth : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static PlayerHealth Instance { get; private set; }

        [Tooltip("最大体力")]
        public int maxHealth = 5;

        [Tooltip("ダメージを受けた後、次のダメージを受けない時間（秒）")]
        public float invincibleTime = 0.8f;

        [Tooltip("ゲームオーバーから復活までの秒数")]
        public float respawnDelay = 2.5f;

        [Tooltip("体力の表示（左手）")]
        public TMP_Text healthText = null;

        [Tooltip("ダメージを受けたときに視界を赤くする板（カメラの子）")]
        public Renderer damageFlash = null;

        public int Health { get; private set; }
        public bool IsDead { get; private set; } = false;

        private PlayerRig rig = null;
        private HapticImpulsePlayer[] haptics = null;
        private Vector3 respawnPosition = Vector3.zero;
        private float lastDamageTime = -10.0f;
        private float flashAlpha = 0.0f;
        private Color flashColor = new Color(1.0f, 0.0f, 0.0f, 0.5f);
        private MaterialPropertyBlock block = null;
        private string message = "";
        private float messageTime = -10.0f;

        private void Awake()
        {
            Instance = this;
            rig = GetComponent<PlayerRig>();
            Health = maxHealth;
            block = new MaterialPropertyBlock();
        }

        private void Start()
        {
            haptics = GetComponentsInChildren<HapticImpulsePlayer>();
            respawnPosition = rig.HeadPosition;
            respawnPosition.y = transform.position.y;
            UpdateText();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// ダメージを受ける。source の方向に構えた盾があれば防ぐ。受けたら true
        /// </summary>
        public bool TakeDamage(int amount, Vector3 source)
        {
            if (IsDead || Time.time - lastDamageTime < invincibleTime)
                return false;

            if (ShieldBehaviour.IsBlocking(rig.HeadPosition, source))
            {
                ShowMessage("Blocked!");
                foreach (var h in haptics)
                    h.SendHapticImpulse(0.4f, 0.1f);
                return false;
            }

            lastDamageTime = Time.time;
            Health = Mathf.Max(0, Health - amount);
            Flash(new Color(1.0f, 0.0f, 0.0f, 0.5f));
            foreach (var h in haptics)
                h.SendHapticImpulse(1.0f, 0.25f);

            if (Health == 0)
                StartCoroutine(GameOver());

            UpdateText();
            return true;
        }

        public void Heal(int amount)
        {
            if (IsDead)
                return;

            Health = Mathf.Min(maxHealth, Health + amount);
            Flash(new Color(0.2f, 1.0f, 0.3f, 0.35f));
            ShowMessage($"+{amount}");
            UpdateText();
        }

        private IEnumerator GameOver()
        {
            IsDead = true;
            UpdateText();

            yield return new WaitForSeconds(respawnDelay);

            // 企画書の案では「その部屋の最初から」。今は開始位置に戻す
            rig.Teleport(respawnPosition);
            Health = maxHealth;
            IsDead = false;
            UpdateText();
        }

        private void ShowMessage(string text)
        {
            message = text;
            messageTime = Time.time;
            UpdateText();
        }

        private void Flash(Color color)
        {
            flashColor = color;
            flashAlpha = color.a;
        }

        private void Update()
        {
            if (damageFlash != null)
            {
                flashAlpha = Mathf.MoveTowards(flashAlpha, IsDead ? 0.6f : 0.0f, Time.deltaTime * 1.5f);
                damageFlash.enabled = flashAlpha > 0.001f;
                damageFlash.GetPropertyBlock(block);
                block.SetColor(BaseColorId, new Color(flashColor.r, flashColor.g, flashColor.b, flashAlpha));
                damageFlash.SetPropertyBlock(block);
            }

            if (message != "" && Time.time - messageTime > 1.0f)
            {
                message = "";
                UpdateText();
            }
        }

        private void UpdateText()
        {
            if (healthText == null)
                return;

            if (IsDead)
            {
                healthText.text = "GAME OVER";
                return;
            }

            string hearts = new string('#', Health) + new string('-', maxHealth - Health);
            healthText.text = message == "" ? $"HP [{hearts}]" : $"HP [{hearts}] {message}";
        }
    }
}
