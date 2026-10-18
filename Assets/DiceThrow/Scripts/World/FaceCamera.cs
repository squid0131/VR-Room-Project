using UnityEngine;

namespace DiceThrow
{
    /// <summary>
    /// 文字などを常にカメラのほうへ向ける
    /// </summary>
    public class FaceCamera : MonoBehaviour
    {
        private Transform head = null;

        private void LateUpdate()
        {
            if (head == null)
            {
                if (Camera.main == null)
                    return;
                head = Camera.main.transform;
            }

            transform.rotation = Quaternion.LookRotation(transform.position - head.position);
        }
    }
}
