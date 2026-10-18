using System;
using System.Collections.Generic;
using UnityEngine;

namespace DiceThrow
{
    /// <summary>投げる狙いの種類。道具ごとに、どの種類へ狙いを補正するかを選ぶ</summary>
    [Flags]
    public enum ThrowTargetKind
    {
        None = 0,
        Enemy = 1,
        RopeAnchor = 2,
        Wall = 4,
    }

    /// <summary>
    /// 投げたときの狙いの補正先（敵・ロープの引っかけ場所・壊せる壁）
    /// </summary>
    public class ThrowTarget : MonoBehaviour
    {
        public static readonly List<ThrowTarget> All = new List<ThrowTarget>();

        public ThrowTargetKind kind = ThrowTargetKind.Enemy;

        [Tooltip("狙う位置（ローカル座標）。敵なら胴体の高さ")]
        public Vector3 localOffset = Vector3.zero;

        private Damageable damageable = null;

        public Vector3 AimPoint => transform.TransformPoint(localOffset);

        /// <summary>倒された敵などは狙わない</summary>
        public bool IsAvailable => isActiveAndEnabled && (damageable == null || !damageable.IsDead);

        private void Awake()
        {
            damageable = GetComponentInParent<Damageable>();
        }

        private void OnEnable()
        {
            All.Add(this);
        }

        private void OnDisable()
        {
            All.Remove(this);
        }
    }
}
