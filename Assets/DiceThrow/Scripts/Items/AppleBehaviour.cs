using UnityEngine;

namespace DiceThrow
{
    /// <summary>
    /// リンゴ（出目 2・回復と囮）。持って口元に近づけると食べて回復する。投げると敵をおびき寄せ、敵が食べるまで残る
    /// </summary>
    [RequireComponent(typeof(Lure))]
    public class AppleBehaviour : ItemBehaviour
    {
        [Tooltip("食べたときの回復量")]
        public int healAmount = 2;

        [Tooltip("頭からこの距離まで近づけると食べる（m）")]
        public float eatDistance = 0.2f;

        private Lure lure = null;

        protected override void Awake()
        {
            base.Awake();
            lure = GetComponent<Lure>();
            lure.edible = true;
            lure.enabled = false;
        }

        protected override void OnGrabbed()
        {
            // 持っている間は敵に狙われない
            lure.enabled = false;
        }

        protected override void OnReleased()
        {
            lure.enabled = true;
        }

        private void Update()
        {
            if (!IsHeld || PlayerRig.Instance == null || PlayerHealth.Instance == null)
                return;

            if (Vector3.Distance(transform.position, PlayerRig.Instance.HeadPosition) < eatDistance)
            {
                PlayerHealth.Instance.Heal(healAmount);
                VanishNow();
            }
        }
    }
}
