using System;
using UnityEngine;

namespace Taurus.Runtime.Stage
{
    /// <summary>
    /// 1秒区間ごとの「真上通過回数」と「抽選重み」の組を表すデータ構造。
    /// StageDefinition のインスペクター上から Wave 別の出題確率を調整するために使用する。
    /// </summary>
    [Serializable]
    public struct WeightedCountChoice
    {
        // 抽選時の重み（パーセント表記でなくてもよいが、仕様書では合計100になる整数で定義）
        [SerializeField] private int _weight;

        // この1秒区間で玉が真上を通過する予定回数（0回の場合はフェイント区間になる）
        [SerializeField] private int _topCount;

        // この区間の長さ（秒）。現行仕様ではすべて 1.0 秒固定
        [SerializeField] private float _durationSeconds;

        /// <summary> 抽選重み。 </summary>
        public int Weight => _weight;

        /// <summary> この区間で真上を通過する予定回数。 </summary>
        public int TopCount => _topCount;

        /// <summary> 区間の長さ（秒）。0以下が設定された場合は安全のため1秒にフォールバックする。 </summary>
        public float DurationSeconds => _durationSeconds > 0f ? _durationSeconds : 1f;

        /// <summary> 重みと通過回数を指定して初期化する。 </summary>
        public WeightedCountChoice(int weight, int topCount, float durationSeconds = 1f)
        {
            _weight = weight;
            _topCount = topCount;
            _durationSeconds = durationSeconds;
        }
    }
}
