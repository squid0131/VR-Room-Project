using UnityEngine;

namespace DiceThrow
{
    /// <summary>
    /// 爆弾で壊せる壁。壊れるとがれきに分かれて飛び散る。
    /// 見た目と当たり判定は子の立方体（拡大縮小して壁の形にしたもの）に持たせる
    /// </summary>
    public class BreakableWall : MonoBehaviour
    {
        [Tooltip("がれきの分割数（横・縦）")]
        public Vector2Int pieces = new Vector2Int(4, 3);

        [Tooltip("がれきが消えるまでの秒数")]
        public float debrisLifetime = 4.0f;

        private bool broken = false;

        public void Break(Vector3 explosionPosition, float force, float radius)
        {
            if (broken)
                return;
            broken = true;

            // 刺さっているやりは残す
            foreach (var spear in GetComponentsInChildren<SpearBehaviour>())
                spear.Unstick();

            var visual = GetComponentInChildren<MeshRenderer>();
            if (visual != null)
            {
                Transform shape = visual.transform;
                Vector3 size = shape.lossyScale;
                Vector3 pieceSize = new Vector3(size.x / pieces.x, size.y / pieces.y, size.z);

                for (int x = 0; x < pieces.x; x++)
                {
                    for (int y = 0; y < pieces.y; y++)
                    {
                        Vector3 local = new Vector3((x + 0.5f) / pieces.x - 0.5f, (y + 0.5f) / pieces.y - 0.5f, 0.0f);
                        var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        piece.name = "Debris";
                        piece.transform.SetPositionAndRotation(shape.TransformPoint(local), shape.rotation);
                        piece.transform.localScale = pieceSize * 0.95f;
                        piece.GetComponent<Renderer>().sharedMaterial = visual.sharedMaterial;

                        var body = piece.AddComponent<Rigidbody>();
                        body.mass = 3.0f;
                        body.AddExplosionForce(force, explosionPosition, radius * 2.0f, 0.5f, ForceMode.Impulse);
                        Destroy(piece, debrisLifetime + Random.value);
                    }
                }
            }

            Destroy(gameObject);
        }
    }
}
