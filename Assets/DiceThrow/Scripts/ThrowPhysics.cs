using UnityEngine;

namespace DiceThrow
{
    /// <summary>
    /// 投げる計算：手の速さの推定、飛ぶ道筋の予測、狙いの補正
    /// </summary>
    public static class ThrowPhysics
    {
        public const float StepTime = 0.02f;

        private static readonly RaycastHit[] HitBuffer = new RaycastHit[16];
        private static readonly Vector3[] VelocityBuffer = new Vector3[64];

        /// <summary>
        /// 手の位置の記録から「投げようとした速さ」を推定する。
        /// 直近 window 秒のうち一番速かった瞬間の速さを使い、向きは速い瞬間ほど重く平均する。
        /// 離す瞬間に手首がぶれたり減速したりしても、振った方向・勢いで飛ぶ
        /// </summary>
        public static Vector3 EstimateThrowVelocity(Vector3[] positions, float[] times, int count, int newest, float window)
        {
            if (count < 2)
                return Vector3.zero;

            int capacity = positions.Length;
            float now = times[newest];

            // 新しい順にフレームごとの速度を集める
            int used = 0;
            for (int i = 0; i < count - 1 && used < VelocityBuffer.Length; i++)
            {
                int b = (newest - i + capacity) % capacity;
                int a = (b - 1 + capacity) % capacity;
                if (now - times[a] > window)
                    break;

                float dt = times[b] - times[a];
                if (dt > 0.0001f)
                    VelocityBuffer[used++] = (positions[b] - positions[a]) / dt;
            }

            if (used == 0)
                return Vector3.zero;

            // 向き：速い瞬間ほど重く平均する
            Vector3 weighted = Vector3.zero;
            for (int i = 0; i < used; i++)
                weighted += VelocityBuffer[i] * VelocityBuffer[i].sqrMagnitude;
            if (weighted.sqrMagnitude < 0.000001f)
                return Vector3.zero;

            // 速さ：連続 3 フレームの平均が一番速いところ（1 フレームだけの跳ねは拾わない）
            int span = Mathf.Min(3, used);
            float peakSpeed = 0.0f;
            for (int i = 0; i + span <= used; i++)
            {
                Vector3 sum = Vector3.zero;
                for (int j = 0; j < span; j++)
                    sum += VelocityBuffer[i + j];
                peakSpeed = Mathf.Max(peakSpeed, (sum / span).magnitude);
            }

            return weighted.normalized * peakSpeed;
        }

        /// <summary>
        /// 1 ステップ進める。Unity（PhysX）と同じく、重力 → 空気抵抗の順にかける
        /// </summary>
        private static void Step(ref Vector3 position, ref Vector3 velocity, float drag, bool useGravity)
        {
            if (useGravity)
                velocity += Physics.gravity * StepTime;
            velocity *= 1.0f / (1.0f + drag * StepTime);
            position += velocity * StepTime;
        }

        /// <summary>
        /// 飛ぶ道筋を予測して points に入れる。何かに当たったらそこで止まる。点の数を返す
        /// </summary>
        public static int Simulate(Vector3 start, Vector3 velocity, float drag, bool useGravity, float maxTime, Rigidbody self, Vector3[] points, out bool hit)
        {
            hit = false;
            points[0] = start;
            int count = 1;
            int steps = Mathf.Min(points.Length - 1, Mathf.CeilToInt(maxTime / StepTime));

            Vector3 position = start;
            for (int i = 0; i < steps; i++)
            {
                Vector3 previous = position;
                Step(ref position, ref velocity, drag, useGravity);

                if (Cast(previous, position, self, out Vector3 point))
                {
                    points[count++] = point;
                    hit = true;
                    break;
                }

                points[count++] = position;
            }

            return count;
        }

        private static bool Cast(Vector3 from, Vector3 to, Rigidbody self, out Vector3 point)
        {
            point = to;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.0001f)
                return false;

            int hits = Physics.RaycastNonAlloc(from, delta / distance, HitBuffer, distance, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < hits; i++)
            {
                var h = HitBuffer[i];
                // 自分自身とプレイヤーの体は無視する
                if (h.rigidbody == self || h.collider is CharacterController)
                    continue;
                if (h.distance < nearest)
                {
                    nearest = h.distance;
                    point = h.point;
                }
            }
            return nearest < float.MaxValue;
        }

        /// <summary>
        /// 速さを変えずに target へ届く初速を探す（低い弧のほう）。届かなければ false
        /// </summary>
        public static bool TrySolve(Vector3 start, float speed, Vector3 target, float drag, bool useGravity, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            Vector3 flat = target - start;
            flat.y = 0.0f;
            float distance = flat.magnitude;

            if (!useGravity || distance < 0.3f)
            {
                velocity = (target - start).normalized * speed;
                return true;
            }

            Vector3 direction = flat / distance;

            // 角度を下から順に試し、初めて的より上を通る角度との間を二分探索する
            const float minPitch = -60.0f;
            const float maxPitch = 50.0f;
            const float coarseStep = 5.0f;

            float lower = minPitch;
            float lowerError = HeightError(start, direction, speed, lower, drag, distance, target.y);
            if (lowerError >= 0.0f)
            {
                velocity = Launch(direction, speed, lower);
                return true;
            }

            float upper = float.NaN;
            for (float pitch = minPitch + coarseStep; pitch <= maxPitch; pitch += coarseStep)
            {
                if (HeightError(start, direction, speed, pitch, drag, distance, target.y) >= 0.0f)
                {
                    upper = pitch;
                    break;
                }
                lower = pitch;
            }

            if (float.IsNaN(upper))
                return false;

            for (int i = 0; i < 12; i++)
            {
                float middle = (lower + upper) * 0.5f;
                if (HeightError(start, direction, speed, middle, drag, distance, target.y) >= 0.0f)
                    upper = middle;
                else
                    lower = middle;
            }

            velocity = Launch(direction, speed, upper);
            return true;
        }

        private static Vector3 Launch(Vector3 direction, float speed, float pitch)
        {
            float radians = pitch * Mathf.Deg2Rad;
            return (direction * Mathf.Cos(radians) + Vector3.up * Mathf.Sin(radians)) * speed;
        }

        /// <summary>水平距離 distance に来たときの高さと的の高さの差。届かなければ -∞</summary>
        private static float HeightError(Vector3 start, Vector3 direction, float speed, float pitch, float drag, float distance, float targetY)
        {
            Vector3 position = start;
            Vector3 velocity = Launch(direction, speed, pitch);

            for (int i = 0; i < 300; i++)
            {
                Vector3 previous = position;
                Step(ref position, ref velocity, drag, true);

                float progress = Vector3.Dot(position - start, direction);
                if (progress >= distance)
                {
                    float previousProgress = Vector3.Dot(previous - start, direction);
                    float t = Mathf.InverseLerp(previousProgress, progress, distance);
                    return Mathf.Lerp(previous.y, position.y, t) - targetY;
                }

                // 的より大きく下に落ちたら届かない
                if (position.y < targetY - 20.0f)
                    break;
            }

            return float.NegativeInfinity;
        }

        /// <summary>
        /// 振った向きから maxAngle 度以内にある的のうち、一番近い向きの的へ届く初速に補正する。
        /// 的がなければ元の速度のまま false
        /// </summary>
        public static bool TryAimAssist(Vector3 start, Vector3 velocity, ThrowTargetKind kinds, float maxAngle, float maxDistance,
            float drag, bool useGravity, out Vector3 assisted, out ThrowTarget target)
        {
            assisted = velocity;
            target = null;
            if (kinds == ThrowTargetKind.None || maxAngle <= 0.0f || velocity.sqrMagnitude < 0.01f)
                return false;

            float speed = velocity.magnitude;
            float bestAngle = maxAngle;

            foreach (var candidate in ThrowTarget.All)
            {
                if ((candidate.kind & kinds) == 0 || !candidate.IsAvailable)
                    continue;

                Vector3 aim = candidate.AimPoint;
                if (Vector3.Distance(start, aim) > maxDistance)
                    continue;

                if (!TrySolve(start, speed, aim, drag, useGravity, out Vector3 solved))
                    continue;

                float angle = Vector3.Angle(velocity, solved);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    assisted = solved;
                    target = candidate;
                }
            }

            return target != null;
        }
    }
}
