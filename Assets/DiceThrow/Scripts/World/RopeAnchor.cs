using UnityEngine;

namespace DiceThrow
{
    /// <summary>
    /// ロープを引っかけられる場所。ロープがこの当たり判定（トリガー）に入ると引っかかり、landingPoint まで引っ張り上げられる
    /// </summary>
    public class RopeAnchor : MonoBehaviour
    {
        [Tooltip("引っ張り上げられた後に立つ場所（足元）")]
        public Transform landingPoint = null;

        public Vector3 HookPoint => transform.position;

        public Vector3 LandingPosition => landingPoint != null ? landingPoint.position : transform.position;

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(HookPoint, 0.1f);
            if (landingPoint != null)
                Gizmos.DrawLine(HookPoint, LandingPosition);
        }
    }
}
