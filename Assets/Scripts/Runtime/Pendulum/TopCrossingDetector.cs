using System;

namespace Taurus.Runtime.Pendulum
{
    /// <summary>
    /// 連続角度における真上境界 top(k) = π + 2πk の横断を検出し、通過イベントを発行する。
    /// 角度を -π..π に折り返さず累積値のまま比較するため、低FPSで1フレームに複数回通過しても取りこぼさない。
    /// </summary>
    public class TopCrossingDetector
    {
        private const double TWO_PI = Math.PI * 2.0;
        private const double EPSILON = 1e-9;

        private bool _hasPreviousAngle;
        private double _previousAngle;
        private int _observedCount;
        private float _lastTopTime = -999f;

        /// <summary> 真上境界を1回以上通過したときに発行される（引数：今回通過した回数）。 </summary>
        public event Action<int> OnTopCrossed;

        /// <summary> 実行中に観測した実測の真上通過回数（デバッグ検証用）。 </summary>
        public int ObservedCount => _observedCount;

        /// <summary> 直近で真上を通過した unscaledTime（デバッグの「TOP!」点滅表示用）。 </summary>
        public float LastTopTime => _lastTopTime;

        /// <summary> 検出状態を初期化する（Wave開始時に呼ぶ）。 </summary>
        public void Reset()
        {
            _hasPreviousAngle = false;
            _previousAngle = 0.0;
            _observedCount = 0;
            _lastTopTime = -999f;
        }

        /// <summary>
        /// 現在角度を評価し、前フレーム角度との間に含まれる真上通過回数を返す。
        /// 初回呼び出し時は基準角度を登録するだけでカウントしない。
        /// </summary>
        public int Evaluate(double currentAngle, float unscaledTime)
        {
            if (!_hasPreviousAngle)
            {
                _previousAngle = currentAngle;
                _hasPreviousAngle = true;
                return 0;
            }

            int crossed = CountTopCrossings(_previousAngle, currentAngle);
            _previousAngle = currentAngle;

            if (crossed > 0)
            {
                _observedCount += crossed;
                _lastTopTime = unscaledTime;
                OnTopCrossed?.Invoke(crossed);
            }

            return crossed;
        }

        /// <summary>
        /// 2つの連続角度の間に含まれる真上境界 top(k) = π + 2πk の個数を数える。
        /// Floor((θ - π) / 2π) のバケット番号の差分を取るため、
        /// 境界値ちょうどを通過しても隣接フレームで二重カウントしない（半開区間 (min, max] と等価）。
        /// </summary>
        public static int CountTopCrossings(double previousAngle, double currentAngle)
        {
            long prevBucket = (long)Math.Floor((previousAngle - Math.PI) / TWO_PI);
            long currBucket = (long)Math.Floor((currentAngle - Math.PI) / TWO_PI);
            return (int)Math.Abs(currBucket - prevBucket);
        }

        /// <summary>
        /// 区間 [startAngle, endAngle] の内部で指定境界（真上=π, 真下=0）を横断した回数を数える。
        /// MotionScheduleGenerator の終点選定・事前検算で使用する（HTML参照実装の crossedCount と同式）。
        /// </summary>
        public static int CountBoundaryCrossings(double startAngle, double endAngle, double boundary)
        {
            double low = Math.Min(startAngle, endAngle);
            double high = Math.Max(startAngle, endAngle);
            long first = (long)Math.Floor((low - boundary) / TWO_PI) + 1;
            long last = (long)Math.Floor((high - boundary - EPSILON) / TWO_PI);
            return (int)Math.Max(0, last - first + 1);
        }
    }
}
