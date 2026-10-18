using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace DiceThrow
{
    /// <summary>投げ方</summary>
    public enum ThrowControl
    {
        /// <summary>掴んだ位置から動かした向きと、離した位置までの距離で飛ぶ</summary>
        Range,
        /// <summary>離す直前に振った勢いと向きで飛ぶ</summary>
        Swing,
        /// <summary>XRI 標準（離した瞬間の手の動き）</summary>
        Standard,
    }

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
        [Tooltip("投げ方。Range: 掴んだ位置から手を動かした向きに、離した位置の距離に応じた力で飛ぶ（狙いやすい）。" +
                 "Swing: 振った勢いと向きで飛ぶ。Standard: XRI 標準の投げ方")]
        public ThrowControl throwControl = ThrowControl.Range;

        [Tooltip("この手の速さ（m/s）より遅く離したときは、投げずにそっと置く（投擲力の倍率をかけない）")]
        public float minThrowSpeed = 1.0f;

        [Tooltip("Swing: 振った速さを測る時間（秒）。離す直前のこの時間で一番速かった勢いで飛ぶ")]
        public float swingWindow = 0.12f;

        [Header("範囲で投げる（Range）")]
        [Tooltip("掴んだ位置からこの距離（m）まで手を動かすと最大の力になる。それより先は同じ力")]
        public float rangeRadius = 0.4f;

        [Tooltip("掴んだ位置からこの距離（m）以内で離すと、投げずにそっと置く")]
        public float rangeDeadZone = 0.06f;

        [Tooltip("力 0% のときの飛距離の目安（m）。基準の重さの道具を約 45 度で投げたとき。投擲力の倍率はかからない")]
        public float rangeMinDistance = 1.0f;

        [Tooltip("力 100% のときの飛距離の目安（m）。基準の重さの道具を約 45 度で投げたとき。軽いほど遠く、重いほど短くなる")]
        public float rangeMaxDistance = 10.0f;

        [Tooltip("手を顔の向きより後ろへ引いている間は、基準の位置も手に付いてくる。振りかぶってから前に出して投げられる")]
        public bool rangeFollowPullBack = true;

        [Tooltip("掴んでいる間、力の範囲（円）と今の力のゲージを表示する")]
        public bool showRangeGuide = true;

        [Tooltip("狙いの補正: 振った向きからこの角度（度）以内に的があれば、的に届くように向きを直す。0 で補正なし")]
        [Range(0, 30)] public float aimAssistAngle = 10.0f;

        [Tooltip("狙いの補正をする最大距離（m）")]
        public float aimAssistDistance = 30.0f;

        [Header("投げる道筋の表示")]
        [Tooltip("投げようとしている間、離したら飛ぶ道筋を表示し、投げた後もしばらく残す")]
        public bool showTrajectory = true;

        [Tooltip("飛んでいる道具に軌跡を付ける")]
        public bool showTrail = true;

        [Header("視点の回転（右スティック）")]
        [Tooltip("オン: 倒している間なめらかに回る。オフ: 一定角度ずつ切り替わる（企画書の酔い対策の案）")]
        public bool smoothTurn = true;

        [Tooltip("なめらかに回るときの速さ（度/秒）")]
        public float smoothTurnSpeed = 90.0f;

        [Tooltip("右スティックを前に倒したときのテレポート（瞬間移動）を使う。オフなら右スティックは回転だけになる")]
        public bool allowTeleport = false;

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

        private void Start()
        {
            ApplyTurnSettings();
        }

        /// <summary>
        /// 右スティックの回転を、なめらかな回転（Continuous Turn）か一定角度ずつの回転（Snap Turn）に切り替える。
        /// XR リグ（XRI のサンプル）の Controller Input Action Manager が、どちらの回転を使うかを決めている
        /// </summary>
        private void ApplyTurnSettings()
        {
            foreach (var manager in FindObjectsByType<ControllerInputActionManager>(FindObjectsSortMode.None))
                manager.smoothTurnEnabled = smoothTurn;
            foreach (var turn in FindObjectsByType<ContinuousTurnProvider>(FindObjectsSortMode.None))
                turn.turnSpeed = smoothTurnSpeed;

            ApplyTeleportSetting();
        }

        /// <summary>
        /// XR リグ（XRI のサンプル）は、右スティックを前に倒すとテレポートの狙いが出て、戻すと瞬間移動する。
        /// 回転しようとして少し前に入っただけでも瞬間移動するので、使わないときはテレポートのボタン割り当てを空にする。
        /// Controller Input Action Manager がアクションを有効に戻しても、割り当てが空なので反応しない
        /// </summary>
        private void ApplyTeleportSetting()
        {
            foreach (var actionManager in FindObjectsByType<InputActionManager>(FindObjectsSortMode.None))
            {
                if (actionManager.actionAssets == null)
                    continue;

                foreach (var asset in actionManager.actionAssets)
                {
                    if (asset == null)
                        continue;

                    foreach (var name in TeleportActions)
                    {
                        var action = asset.FindAction(name);
                        if (action == null)
                            continue;

                        if (allowTeleport)
                        {
                            action.RemoveAllBindingOverrides();
                        }
                        else
                        {
                            // 割り当てを空文字で上書きすると、そのボタン割り当ては無効になる
                            for (int i = 0; i < action.bindings.Count; i++)
                                action.ApplyBindingOverride(i, "");
                        }
                    }
                }
            }
        }

        private static readonly string[] TeleportActions =
        {
            "XRI Left Locomotion/Teleport Mode",
            "XRI Right Locomotion/Teleport Mode",
        };

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
            ApplyTurnSettings();
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
