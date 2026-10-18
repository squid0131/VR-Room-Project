using System;
using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace DiceThrow
{
    /// <summary>
    /// XR リグ（プレイヤーの体）を動かす。ロープでの引っ張り上げと、ゲームオーバー後の戻しに使う
    /// </summary>
    [RequireComponent(typeof(XROrigin))]
    public class PlayerRig : MonoBehaviour
    {
        public static PlayerRig Instance { get; private set; }

        [Tooltip("ロープで引っ張られる速さ（m/s）")]
        public float pullSpeed = 8.0f;

        [Tooltip("引っ張られるときに弧を描く高さ（m）")]
        public float pullArcHeight = 0.6f;

        public bool IsPulling { get; private set; } = false;

        /// <summary>頭（カメラ）の位置</summary>
        public Vector3 HeadPosition => origin.Camera != null ? origin.Camera.transform.position : transform.position;

        /// <summary>足元の高さ（XR Origin は床の高さにある）</summary>
        public float FeetHeight => transform.position.y;

        private XROrigin origin = null;
        private CharacterController characterController = null;

        private void Awake()
        {
            Instance = this;
            origin = GetComponent<XROrigin>();
            characterController = GetComponent<CharacterController>();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>足元が feetPosition に来るまで引っ張る。引っ張り中なら false</summary>
        public bool StartPull(Vector3 feetPosition, Action onFinished)
        {
            if (IsPulling)
                return false;

            StartCoroutine(Pull(feetPosition, onFinished));
            return true;
        }

        private IEnumerator Pull(Vector3 feetPosition, Action onFinished)
        {
            IsPulling = true;

            // 移動・重力などと競合しないよう、引っ張っている間は止める
            var disabled = new List<Behaviour>();
            foreach (var provider in GetComponentsInChildren<LocomotionProvider>())
            {
                if (provider.enabled)
                {
                    provider.enabled = false;
                    disabled.Add(provider);
                }
            }
            bool hadController = characterController != null && characterController.enabled;
            if (hadController)
                characterController.enabled = false;

            Vector3 start = transform.position;
            Vector3 end = OriginPositionFor(feetPosition);
            float duration = Mathf.Max(0.4f, Vector3.Distance(start, end) / pullSpeed);

            for (float time = 0.0f; time < duration; time += Time.deltaTime)
            {
                float t = time / duration;
                float eased = t * t * (3.0f - 2.0f * t);
                transform.position = Vector3.Lerp(start, end, eased) + Vector3.up * (pullArcHeight * Mathf.Sin(t * Mathf.PI));
                yield return null;
            }
            transform.position = end;

            if (hadController)
                characterController.enabled = true;
            foreach (var behaviour in disabled)
            {
                if (behaviour != null)
                    behaviour.enabled = true;
            }

            IsPulling = false;
            onFinished?.Invoke();
        }

        /// <summary>足元が feetPosition に来るように一瞬で移動する</summary>
        public void Teleport(Vector3 feetPosition)
        {
            bool hadController = characterController != null && characterController.enabled;
            if (hadController)
                characterController.enabled = false;

            transform.position = OriginPositionFor(feetPosition);

            if (hadController)
                characterController.enabled = true;
        }

        /// <summary>頭の真下が feetPosition に来るような XR Origin の位置（プレイエリア内で歩いた分を打ち消す）</summary>
        private Vector3 OriginPositionFor(Vector3 feetPosition)
        {
            Vector3 headOffset = HeadPosition - transform.position;
            headOffset.y = 0.0f;
            return feetPosition - headOffset;
        }
    }
}
