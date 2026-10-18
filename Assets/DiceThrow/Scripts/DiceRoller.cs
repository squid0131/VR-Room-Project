using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace DiceThrow
{
    /// <summary>
    /// 左手に持つさいころ。左手を強く数回振ると出目が決まり、出目に応じた道具が左手の前に現れる
    /// </summary>
    public class DiceRoller : MonoBehaviour
    {
        /// <summary>さいころを振っている間は投げられない（企画書: 振っている間は攻撃や投擲はできない）</summary>
        public static bool BlocksThrowing { get; private set; }

        [Header("道具")]
        [Tooltip("出目 1〜6 に対応する道具のプレハブ（要素 0 が出目 1）")]
        public GameObject[] itemPrefabs = new GameObject[6];

        [Tooltip("出目 1〜6 の表示名（要素 0 が出目 1）")]
        public string[] itemNames = { "Stone", "Apple", "Rope", "Spear", "Shield", "Bomb" };

        [Tooltip("投げた後に場に残しておく道具の最大数。超えたら古いものから消す")]
        public int maxItemsInWorld = 12;

        [Header("振る判定")]
        [Tooltip("出目が決まるまでに振る回数")]
        [Range(1, 6)] public int shakesToRoll = 3;

        [Tooltip("1 回の「振り」と判定する手の速さ（m/s）")]
        public float shakeSpeed = 1.6f;

        [Tooltip("手の速さがこれより下がったら、次の振りを受け付ける（m/s）")]
        public float releaseSpeed = 0.8f;

        [Tooltip("前の振りから次の振りまでの最短間隔（秒）")]
        public float minShakeInterval = 0.12f;

        [Tooltip("この秒数振らないと、振った回数がリセットされる")]
        public float shakeTimeout = 1.0f;

        [Tooltip("出目が決まった直後、振りを受け付けない時間（秒）。振った勢いで次が始まるのを防ぐ")]
        public float cooldownAfterRoll = 0.5f;

        [Header("救済補正（プレイヤーには見せない）")]
        [Tooltip("今の場面で必要な出目（1〜6）。空なら補正なし")]
        public List<int> neededFaces = new List<int>();

        [Tooltip("必要な出目がこの回数続けて出なかったら、次は必ず必要な出目にする")]
        public int pityRolls = 3;

        [Header("表示")]
        public DiceVisual diceVisual = null;
        public TMP_Text statusText = null;

        [Header("道具の出現位置（右手）")]
        [Tooltip("右手のコントローラー。空なら XR リグから自動で探す")]
        public Transform rightHand = null;

        [Tooltip("右手から見て前方向の距離（m）")]
        public float spawnForward = 0.1f;

        [Tooltip("右手と道具の間の上方向のすき間（m）")]
        public float spawnGap = 0.06f;

        public int LastFace { get; private set; } = 0;

        public Transform RightHand => rightHand != null ? rightHand : transform;

        private Transform hand = null;
        private Transform head = null;
        private HapticImpulsePlayer haptics = null;
        private HapticImpulsePlayer rightHaptics = null;
        private bool tossing = false;
        private InputAction shakeKeyAction = null;

        private Vector3 lastHandPosition = Vector3.zero;
        private bool hasLastHandPosition = false;
        private Vector3 handVelocity = Vector3.zero;

        private int shakeCount = 0;
        private float lastShakeTime = -10.0f;
        private float lastRollTime = -10.0f;
        private bool inSwing = false;
        private Vector3 swingDirection = Vector3.zero;
        private int missStreak = 0;

        private DiceSpawnedItem currentItem = null;
        private readonly List<DiceSpawnedItem> releasedItems = new List<DiceSpawnedItem>();

        /// <summary>振っている途中、またはさいころが宙にある間</summary>
        public bool IsShaking => shakeCount > 0 || tossing;

        private void Awake()
        {
            // さいころは Left Controller の子に置く前提
            hand = transform.parent != null ? transform.parent : transform;
            haptics = GetComponentInParent<HapticImpulsePlayer>();

            if (rightHand == null && hand.parent != null)
                rightHand = hand.parent.Find("Right Controller");
            if (rightHand != null)
                rightHaptics = rightHand.GetComponent<HapticImpulsePlayer>();

            // ヘッドセットなし（XR Device Simulator）でも試せるように T キーでも 1 回振ったことにする
            shakeKeyAction = new InputAction("Shake Dice", InputActionType.Button);
            shakeKeyAction.AddBinding("<Keyboard>/t");
            shakeKeyAction.performed += _ => RegisterShake(Vector3.up * (shakeCount % 2 == 0 ? 1.0f : -1.0f));
        }

        private void OnEnable()
        {
            shakeKeyAction.Enable();
        }

        private void OnDisable()
        {
            shakeKeyAction.Disable();
            BlocksThrowing = false;
        }

        private void OnDestroy()
        {
            shakeKeyAction.Dispose();
        }

        private void Start()
        {
            if (Camera.main != null)
                head = Camera.main.transform;

            UpdateStatus();
        }

        private void Update()
        {
            DetectShake();

            // しばらく振らなければ振った回数をリセットする
            if (shakeCount > 0 && Time.time - lastShakeTime > shakeTimeout)
            {
                shakeCount = 0;
                diceVisual?.ShowFace(LastFace);
                UpdateStatus();
            }

            BlocksThrowing = IsShaking;
        }

        private void LateUpdate()
        {
            // ステータス表示を常に顔のほうへ向ける
            if (statusText != null && head != null)
                statusText.transform.rotation = Quaternion.LookRotation(statusText.transform.position - head.position);
        }

        /// <summary>
        /// 左手の速さから「振り」を数える。スティック移動や旋回で誤判定しないよう、
        /// XR リグの中での位置（トラッキング空間の位置）の変化で速さを測る
        /// </summary>
        private void DetectShake()
        {
            Vector3 position = hand.localPosition;
            if (!hasLastHandPosition || Time.deltaTime <= 0.0f)
            {
                lastHandPosition = position;
                hasLastHandPosition = true;
                return;
            }

            Vector3 rawVelocity = (position - lastHandPosition) / Time.deltaTime;
            lastHandPosition = position;
            handVelocity = Vector3.Lerp(handVelocity, rawVelocity, 0.5f);

            float speed = handVelocity.magnitude;
            if (speed >= shakeSpeed)
            {
                Vector3 direction = handVelocity / speed;

                // 速さが一度落ちてから再び上がったとき、または振る向きが反転したときを 1 回と数える
                bool reversed = inSwing && Vector3.Dot(direction, swingDirection) < -0.2f;
                if (!inSwing || reversed)
                    RegisterShake(direction);

                inSwing = true;
            }
            else if (speed <= releaseSpeed)
            {
                inSwing = false;
            }
        }

        private void RegisterShake(Vector3 direction)
        {
            if (tossing || Time.time - lastShakeTime < minShakeInterval || Time.time - lastRollTime < cooldownAfterRoll)
                return;

            swingDirection = direction;
            lastShakeTime = Time.time;
            shakeCount++;

            haptics?.SendHapticImpulse(0.3f, 0.05f);

            if (shakeCount >= shakesToRoll)
            {
                Roll();
            }
            else
            {
                diceVisual?.Hop();
                UpdateStatus();
            }
        }

        /// <summary>
        /// 出目を決めてさいころを放り上げる。着地したら道具が右手に現れる。
        /// 前に出した道具がまだ投げられていなければ消える
        /// </summary>
        public void Roll()
        {
            if (tossing)
                return;

            shakeCount = 0;
            LastFace = PickFace();
            haptics?.SendHapticImpulse(0.5f, 0.08f);

            // 振り直すと、持っていた道具は消える
            if (currentItem != null)
                currentItem.Vanish();
            currentItem = null;

            if (diceVisual != null)
            {
                tossing = true;
                diceVisual.Toss(LastFace, OnDiceLanded);
                UpdateStatus();
            }
            else
            {
                OnDiceLanded();
            }
        }

        private void OnDiceLanded()
        {
            tossing = false;
            lastRollTime = Time.time;

            haptics?.SendHapticImpulse(0.8f, 0.12f);
            rightHaptics?.SendHapticImpulse(0.6f, 0.1f);

            SpawnItem(LastFace);
            UpdateStatus();
        }

        private int PickFace()
        {
            int face = Random.Range(1, 7);

            if (neededFaces.Count > 0)
            {
                // 必要な出目が続けて出なかったら、次は必ず出す
                if (missStreak >= pityRolls)
                    face = neededFaces[Random.Range(0, neededFaces.Count)];

                missStreak = neededFaces.Contains(face) ? 0 : missStreak + 1;
            }

            return face;
        }

        /// <summary>救済補正で使う「今必要な出目」を設定する。部屋の入口などから呼ぶ</summary>
        public void SetNeededFaces(params int[] faces)
        {
            neededFaces.Clear();
            neededFaces.AddRange(faces);
            missStreak = 0;
        }

        private void SpawnItem(int face)
        {
            int index = face - 1;
            if (itemPrefabs == null || index >= itemPrefabs.Length || itemPrefabs[index] == null)
            {
                Debug.LogWarning($"[DiceRoller] 出目 {face} の道具プレハブが設定されていません。");
                return;
            }

            var instance = Instantiate(itemPrefabs[index], RightHand.position, Quaternion.identity);
            currentItem = instance.AddComponent<DiceSpawnedItem>();
            currentItem.Init(this);
        }

        /// <summary>
        /// 出した道具が浮かんで待つ位置と向き。右手のすぐ上・少し前に、前向き（やりは穂先が前）で浮かぶ。
        /// upright なら円盤（ローカル Y が面の向き）を縦に立て、面を前に向ける（盾用）
        /// </summary>
        public Pose GetFloatPose(float halfHeight, bool upright = false)
        {
            Vector3 forward = head != null ? Vector3.ProjectOnPlane(head.forward, Vector3.up) : RightHand.forward;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            forward.Normalize();

            Vector3 position = RightHand.position
                + forward * spawnForward
                + Vector3.up * (spawnGap + halfHeight);

            Quaternion rotation = upright
                ? Quaternion.LookRotation(Vector3.down, forward)
                : Quaternion.LookRotation(forward, Vector3.up);

            return new Pose(position, rotation);
        }

        /// <summary>
        /// 投げた道具を手元に戻す（盾のブーメラン用）。すでに別の道具が手元にあれば戻せない
        /// </summary>
        internal bool TryReturnToHand(DiceSpawnedItem item)
        {
            if (currentItem != null && currentItem != item)
                return false;

            currentItem = item;
            releasedItems.Remove(item);
            rightHaptics?.SendHapticImpulse(0.5f, 0.08f);
            return true;
        }

        internal void OnItemReleased(DiceSpawnedItem item)
        {
            // 投げた道具はもう「手元の道具」ではないので、振り直しても消えない
            if (item == currentItem)
                currentItem = null;

            if (!releasedItems.Contains(item))
                releasedItems.Add(item);

            releasedItems.RemoveAll(i => i == null);
            while (releasedItems.Count > maxItemsInWorld)
            {
                var oldest = releasedItems[0];
                releasedItems.RemoveAt(0);
                if (oldest != null)
                    oldest.Vanish();
            }
        }

        internal void OnItemDestroyed(DiceSpawnedItem item)
        {
            if (item == currentItem)
                currentItem = null;
            releasedItems.Remove(item);
        }

        private void UpdateStatus()
        {
            if (statusText == null)
                return;

            if (tossing)
            {
                statusText.text = "Rolling...";
            }
            else if (shakeCount > 0)
            {
                string marks = new string('#', shakeCount) + new string('-', shakesToRoll - shakeCount);
                statusText.text = $"Shake! [{marks}]";
            }
            else if (LastFace > 0)
            {
                statusText.text = $"{LastFace} : {itemNames[LastFace - 1]}";
            }
            else
            {
                statusText.text = $"Shake x{shakesToRoll}";
            }
        }
    }
}
