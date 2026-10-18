using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace DiceThrow
{
    /// <summary>
    /// リンゴ（出目 2・回復と囮）。持って口元に近づけ、トリガーを引くと食べて回復する（近づけるだけでは食べない）。
    /// 投げると敵をおびき寄せ、敵が食べるまで残る
    /// </summary>
    [RequireComponent(typeof(Lure))]
    public class AppleBehaviour : ItemBehaviour
    {
        [Tooltip("食べたときの回復量")]
        public int healAmount = 2;

        [Tooltip("頭からこの距離まで近づけると、トリガーで食べられる（m）")]
        public float eatDistance = 0.25f;

        [Tooltip("体力が満タンのときは食べない（取っておける）")]
        public bool keepWhenFull = true;

        private Lure lure = null;
        private TextMeshPro prompt = null;
        private bool nearMouth = false;

        protected override void Awake()
        {
            base.Awake();
            lure = GetComponent<Lure>();
            lure.edible = true;
            lure.enabled = false;

            // 持っている手のトリガー（XRI の Activate）で食べる
            Grab.activated.AddListener(_ => TryEat());
        }

        private void OnDestroy()
        {
            if (prompt != null)
                Destroy(prompt.gameObject);
        }

        protected override void OnGrabbed()
        {
            // 持っている間は敵に狙われない
            lure.enabled = false;
        }

        protected override void OnReleased()
        {
            lure.enabled = true;
            SetNearMouth(false);
        }

        private bool IsFull => PlayerHealth.Instance != null && PlayerHealth.Instance.Health >= PlayerHealth.Instance.maxHealth;

        private void TryEat()
        {
            if (!IsHeld || !nearMouth || PlayerHealth.Instance == null || PlayerHealth.Instance.IsDead)
                return;

            if (keepWhenFull && IsFull)
            {
                PlayerHealth.Instance.ShowMessage("HP Full");
                return;
            }

            PlayerHealth.Instance.Heal(healAmount);
            VanishNow();
        }

        private void Update()
        {
            bool near = IsHeld && PlayerRig.Instance != null
                && Vector3.Distance(transform.position, PlayerRig.Instance.HeadPosition) < eatDistance;
            SetNearMouth(near);

            if (prompt != null && prompt.gameObject.activeSelf)
            {
                prompt.text = keepWhenFull && IsFull ? "HP Full" : "Trigger: Eat";
                prompt.transform.position = transform.position + Vector3.up * 0.09f;
            }
        }

        /// <summary>口元に近づけたら「トリガーで食べる」と出し、手を軽く振動させる</summary>
        private void SetNearMouth(bool near)
        {
            if (near == nearMouth)
                return;
            nearMouth = near;

            if (near)
            {
                if (prompt == null)
                    prompt = CreatePrompt();
                Pulse(0.2f, 0.05f);
            }

            if (prompt != null)
                prompt.gameObject.SetActive(near);
        }

        private void Pulse(float amplitude, float duration)
        {
            if (Grab.interactorsSelecting.Count == 0)
                return;

            var haptics = Grab.interactorsSelecting[0].transform.GetComponentInParent<HapticImpulsePlayer>();
            if (haptics != null)
                haptics.SendHapticImpulse(amplitude, duration);
        }

        private TextMeshPro CreatePrompt()
        {
            // 道具の大きさの影響を受けないよう子にはしない。TextMeshPro の標準フォントに日本語がないので英語
            var go = new GameObject("Apple Eat Prompt");
            var text = go.AddComponent<TextMeshPro>();
            text.fontSize = 0.25f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.6f, 1.0f, 0.6f);
            text.outlineWidth = 0.2f;
            text.outlineColor = Color.black;
            text.rectTransform.sizeDelta = new Vector2(0.5f, 0.08f);
            go.AddComponent<FaceCamera>();
            return text;
        }
    }
}
