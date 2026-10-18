using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DiceThrow
{
    /// <summary>
    /// 投擲テストの全体設定、飛距離の表示、道具のリセットを行う
    /// </summary>
    public class ThrowTestManager : MonoBehaviour
    {
        public static ThrowTestManager Instance { get; private set; }

        [Tooltip("投擲力の倍率。1 で手の速さそのまま")]
        public float throwPower = 2.0f;

        [Tooltip("基準の重さ（kg）。これより軽いと速く、重いと遅く飛ぶ")]
        public float referenceMass = 0.5f;

        [Tooltip("重さが飛び方に与える影響。0 で重さに関係なく同じ速さ")]
        [Range(0, 1)] public float weightInfluence = 0.35f;

        [Header("投げる操作の補助")]
        [Tooltip("オン: 振った勢いと向きから投げる速さを決める（離す瞬間のぶれに強い）。オフ: XRI 標準の投げ方")]
        public bool useSwingThrow = true;

        [Tooltip("この手の速さ（m/s）より遅く離したときは、投げずにそっと置く（投擲力の倍率をかけない）")]
        public float minThrowSpeed = 1.0f;

        [Tooltip("振った速さを測る時間（秒）。離す直前のこの時間で一番速かった勢いで飛ぶ")]
        public float swingWindow = 0.12f;

        [Tooltip("狙いの補正: 振った向きからこの角度（度）以内に的があれば、的に届くように向きを直す。0 で補正なし")]
        [Range(0, 30)] public float aimAssistAngle = 10.0f;

        [Tooltip("狙いの補正をする最大距離（m）")]
        public float aimAssistDistance = 30.0f;

        [Header("投げる道筋の表示")]
        [Tooltip("振っている間、離したら飛ぶ道筋を表示し、投げた後もしばらく残す")]
        public bool showTrajectory = true;

        [Tooltip("飛んでいる道具に軌跡を付ける")]
        public bool showTrail = true;

        [Tooltip("結果ボードに出す操作の説明")]
        public string hint = "B button: reset items";

        [Tooltip("飛距離を表示するテキスト")]
        public TMP_Text resultText = null;

        private InputAction resetAction = null;
        private readonly Dictionary<string, float> bestDistances = new Dictionary<string, float>();
        private string lastThrow = "";

        private void Awake()
        {
            Instance = this;

            // 右手 B ボタン（シミュレーター用に R キー）で道具を元の位置に戻す
            resetAction = new InputAction("Reset Items", InputActionType.Button);
            resetAction.AddBinding("<XRController>{RightHand}/secondaryButton");
            resetAction.AddBinding("<Keyboard>/r");
            resetAction.performed += _ => ResetAll();

            UpdateText();
        }

        private void OnEnable()
        {
            resetAction.Enable();
        }

        private void OnDisable()
        {
            resetAction.Disable();
        }

        private void OnDestroy()
        {
            resetAction.Dispose();
            if (Instance == this)
                Instance = null;
        }

        private void OnValidate()
        {
            // Play 中に Inspector で値を変えたらすぐ反映する
            if (!Application.isPlaying)
                return;

            foreach (var item in FindObjectsByType<ThrowableItem>(FindObjectsSortMode.None))
                item.ApplySettings();
        }

        public void ReportThrow(string itemName, float distance)
        {
            lastThrow = $"{itemName}  {distance:F1} m";

            if (!bestDistances.TryGetValue(itemName, out float best) || distance > best)
                bestDistances[itemName] = distance;

            UpdateText();
        }

        public void ResetAll()
        {
            foreach (var item in FindObjectsByType<ThrowableItem>(FindObjectsSortMode.None))
                item.ResetToSpawn();
        }

        private void UpdateText()
        {
            if (resultText == null)
                return;

            var builder = new StringBuilder();
            builder.AppendLine(lastThrow == "" ? "Grab and throw!" : $"Last: {lastThrow}");
            builder.AppendLine($"<size=70%>{hint}</size>");

            foreach (var pair in bestDistances)
                builder.AppendLine($"<size=70%>Best {pair.Key}: {pair.Value:F1} m</size>");

            resultText.text = builder.ToString();
        }
    }
}
