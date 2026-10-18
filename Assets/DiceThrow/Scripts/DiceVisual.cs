using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace DiceThrow
{
    /// <summary>
    /// さいころの見た目。振るたびに手の上で少し跳ね、出目が決まると高く放り上げられて回転し、着地すると出目を上に向ける
    /// 面の配置: 1=+Y, 6=-Y, 2=+X, 5=-X, 3=+Z, 4=-Z（向かい合う面の和が 7）
    /// </summary>
    public class DiceVisual : MonoBehaviour
    {
        [Tooltip("着地後、目標の向きへ回る速さ")]
        public float turnSpeed = 15.0f;

        [Tooltip("1 回振ったときに跳ね上がる初速（m/s）。1.3 で約 7cm")]
        public float hopSpeed = 1.3f;

        [Tooltip("出目が決まったときに放り上げる初速（m/s）。2.4 で約 24cm")]
        public float tossSpeed = 2.4f;

        [Tooltip("さいころにかかる重力（m/s²）。大きいほどきびきび落ちる")]
        public float gravity = 12.0f;

        [Tooltip("放り上げたときの回転の速さ（度/秒）")]
        public float tossSpinSpeed = 900.0f;

        [Tooltip("1 回振ったときの回転の速さ（度/秒）")]
        public float hopSpinSpeed = 400.0f;

        public bool IsTossing { get; private set; } = false;

        private Quaternion faceRotation = Quaternion.identity;
        private Quaternion tumble = Quaternion.identity;
        private Transform head = null;

        private Vector3 restLocalPosition = Vector3.zero;
        private Vector3 baseScale = Vector3.one;
        private float height = 0.0f;
        private float verticalSpeed = 0.0f;
        private bool airborne = false;
        private Vector3 spinAxis = Vector3.up;
        private float spinRate = 0.0f;
        private float landTime = -10.0f;

        private int pendingFace = 0;
        private Action onLanded = null;

        private void Awake()
        {
            restLocalPosition = transform.localPosition;
            baseScale = transform.localScale;
        }

        private void Start()
        {
            if (Camera.main != null)
                head = Camera.main.transform;
        }

        /// <summary>指定した出目を上に向ける（0 なら初期の向き）</summary>
        public void ShowFace(int face)
        {
            faceRotation = FaceUpRotation(face);
            tumble = Quaternion.identity;
        }

        /// <summary>1 回振ったときに手の上で跳ねる</summary>
        public void Hop()
        {
            if (IsTossing)
                return;

            verticalSpeed = Mathf.Min(Mathf.Max(verticalSpeed, 0.0f) + hopSpeed, hopSpeed * 1.5f);
            airborne = true;
            spinAxis = Random.onUnitSphere;
            spinRate = hopSpinSpeed;
        }

        /// <summary>高く放り上げ、着地したら出目を見せて onLanded を呼ぶ</summary>
        public void Toss(int face, Action landed)
        {
            IsTossing = true;
            pendingFace = face;
            onLanded = landed;

            verticalSpeed = tossSpeed;
            airborne = true;
            spinAxis = Random.onUnitSphere;
            spinRate = tossSpinSpeed;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;

            if (airborne)
            {
                height += verticalSpeed * dt;
                verticalSpeed -= gravity * dt;
                tumble = Quaternion.AngleAxis(spinRate * dt, spinAxis) * tumble;

                if (height <= 0.0f)
                    Land();
            }

            // 手の上（親の位置）から真上に浮く。手の傾きには影響されない
            var parent = transform.parent;
            Vector3 rest = parent != null ? parent.TransformPoint(restLocalPosition) : restLocalPosition;
            transform.position = rest + Vector3.up * height;

            // 着地した瞬間に少しつぶれて戻る
            float squash = Mathf.Clamp01((Time.time - landTime) / 0.15f);
            float s = 1.0f - 0.15f * Mathf.Sin(squash * Mathf.PI);
            transform.localScale = new Vector3(baseScale.x * (2.0f - s), baseScale.y * s, baseScale.z * (2.0f - s));

            // 出目が真上・数字の向きがプレイヤーの正面になるようにする
            Vector3 forward = head != null ? Vector3.ProjectOnPlane(head.forward, Vector3.up) : Vector3.forward;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            Quaternion target = Quaternion.LookRotation(forward, Vector3.up) * tumble * faceRotation;
            if (airborne)
                transform.rotation = target;
            else
                transform.rotation = Quaternion.Slerp(transform.rotation, target, 1.0f - Mathf.Exp(-turnSpeed * dt));
        }

        private void Land()
        {
            height = 0.0f;
            verticalSpeed = 0.0f;
            airborne = false;
            landTime = Time.time;

            if (!IsTossing)
                return;

            IsTossing = false;
            ShowFace(pendingFace);

            var callback = onLanded;
            onLanded = null;
            callback?.Invoke();
        }

        /// <summary>出目の面を +Y に向ける回転</summary>
        public static Quaternion FaceUpRotation(int face)
        {
            switch (face)
            {
                case 2: return Quaternion.Euler(0.0f, 0.0f, 90.0f);
                case 3: return Quaternion.Euler(-90.0f, 0.0f, 0.0f);
                case 4: return Quaternion.Euler(90.0f, 0.0f, 0.0f);
                case 5: return Quaternion.Euler(0.0f, 0.0f, -90.0f);
                case 6: return Quaternion.Euler(180.0f, 0.0f, 0.0f);
                default: return Quaternion.identity;
            }
        }

        /// <summary>出目ごとの面の向き（ローカル）</summary>
        public static Vector3 FaceNormal(int face)
        {
            switch (face)
            {
                case 1: return Vector3.up;
                case 2: return Vector3.right;
                case 3: return Vector3.forward;
                case 4: return Vector3.back;
                case 5: return Vector3.left;
                default: return Vector3.down;
            }
        }
    }
}
