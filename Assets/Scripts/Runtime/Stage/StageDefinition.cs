using System.Collections.Generic;
using UnityEngine;

namespace Taurus.Runtime.Stage
{
    /// <summary>
    /// Wave 1〜8+ の振り子構成・分割区間・量制約 [Q_min, Q_max]・区間抽選重みを定義する ScriptableObject。
    /// カスタム Inspector（StageDefinitionEditor）から数理モデル・前向き/後ろ向きDPによる難易度曲線と
    /// 制約達成確率をリアルタイムに確認しながらレベルデザインを調整できる。
    /// </summary>
    [CreateAssetMenu(fileName = "StageDefinition", menuName = "Taurus/Stage Definition")]
    public class StageDefinition : ScriptableObject
    {
        // 仕様上、Wave 8 以降はすべて Wave 8 の構成・重みを繰り返し使用する
        private const int MAX_DEFINED_WAVE = 8;

        // 仕様書（pendulum-count-spec.md 第3章）の重み表に完全一致するデフォルト定義
        private static readonly WaveConfig[] DEFAULT_WAVES =
        {
            // Wave 1: 1重×1（1玉） / 70:0, 20:1, 10:2
            new WaveConfig(1, 0, new[]
            {
                new WeightedCountChoice(70, 0),
                new WeightedCountChoice(20, 1),
                new WeightedCountChoice(10, 2),
            }),
            // Wave 2: 1重×1（1玉） / 60:0, 25:1, 10:2, 5:3
            new WaveConfig(1, 0, new[]
            {
                new WeightedCountChoice(60, 0),
                new WeightedCountChoice(25, 1),
                new WeightedCountChoice(10, 2),
                new WeightedCountChoice(5, 3),
            }),
            // Wave 3: 1重×2（2玉） / 70:0, 20:1, 10:2
            new WaveConfig(2, 0, new[]
            {
                new WeightedCountChoice(70, 0),
                new WeightedCountChoice(20, 1),
                new WeightedCountChoice(10, 2),
            }),
            // Wave 4: 1重×2（2玉） / 53:0, 25:1, 15:2, 5:3, 2:4
            new WaveConfig(2, 0, new[]
            {
                new WeightedCountChoice(53, 0),
                new WeightedCountChoice(25, 1),
                new WeightedCountChoice(15, 2),
                new WeightedCountChoice(5, 3),
                new WeightedCountChoice(2, 4),
            }),
            // Wave 5: 2重×1（2玉） / 69:0, 31:1
            new WaveConfig(0, 1, new[]
            {
                new WeightedCountChoice(69, 0),
                new WeightedCountChoice(31, 1),
            }),
            // Wave 6: 2重×1（2玉） / 65:0, 25:1, 7:2, 2:3, 1:4
            new WaveConfig(0, 1, new[]
            {
                new WeightedCountChoice(65, 0),
                new WeightedCountChoice(25, 1),
                new WeightedCountChoice(7, 2),
                new WeightedCountChoice(2, 3),
                new WeightedCountChoice(1, 4),
            }),
            // Wave 7: 2重×2（4玉） / 80:0, 17:1, 3:2
            new WaveConfig(0, 2, new[]
            {
                new WeightedCountChoice(80, 0),
                new WeightedCountChoice(17, 1),
                new WeightedCountChoice(3, 2),
            }),
            // Wave 8+: 2重×2（4玉） / 70:0, 18:1, 8:2, 4:3
            new WaveConfig(0, 2, new[]
            {
                new WeightedCountChoice(70, 0),
                new WeightedCountChoice(18, 1),
                new WeightedCountChoice(8, 2),
                new WeightedCountChoice(4, 3),
            }),
        };

        [Header("難易度評価パラメータ")]
        // 総合難易度スコアにおける L2 ピーク評価の重み（0=平均密度のみ, 1=L2ピークのみ）
        [SerializeField, Range(0f, 1f)] private float _peakWeight = 0.35f;

        // 複数本の振り子を同時視認する際の追加負荷係数（本数が1本増えるごとの加算倍率）
        [SerializeField, Min(0f)] private float _multiPendulumPenalty = 0.25f;

        // 二重振り子（親玉に連動する子玉の追従負荷）の追加負荷係数（二重振り子1本ごとの加算倍率）
        [SerializeField, Min(0f)] private float _doublePendulumPenalty = 0.50f;

        [Header("Wave 定義")]
        // インスペクターでバランス調整したい場合はここを編集する（空なら DEFAULT_WAVES が使われる）
        [SerializeField] private WaveConfig[] _waves;

        /// <summary> 総合難易度スコアにおける L2 ピーク評価の重み。 </summary>
        public float PeakWeight => Mathf.Clamp01(_peakWeight);

        /// <summary> 複数本振り子の同時視認ペナルティ係数。 </summary>
        public float MultiPendulumPenalty => Mathf.Max(0f, _multiPendulumPenalty);

        /// <summary> 二重振り子の追従負荷ペナルティ係数。 </summary>
        public float DoublePendulumPenalty => Mathf.Max(0f, _doublePendulumPenalty);

        /// <summary> 定義済み Wave 一覧（未設定時はデフォルト8 Waveを返す）。 </summary>
        public IReadOnlyList<WaveConfig> Waves => (_waves != null && _waves.Length > 0) ? _waves : DEFAULT_WAVES;

        /// <summary> 指定Wave番号（1始まり）の構成を取得する。Wave 8以降は末尾（Wave 8）の設定を返す。 </summary>
        public WaveConfig GetWaveConfig(int wave)
        {
            WaveConfig[] table = (_waves != null && _waves.Length > 0) ? _waves : DEFAULT_WAVES;
            int clampedIndex = Mathf.Clamp(wave - 1, 0, table.Length - 1);
            return table[clampedIndex];
        }

        /// <summary> 指定Wave番号の難易度評価レポート（前向き/後ろ向きDP・台形積分・L2・構成負荷）を算出する。 </summary>
        public WaveDifficultyReport EvaluateWave(int wave)
        {
            return StageDifficultyEvaluator.EvaluateWave(
                GetWaveConfig(wave),
                PeakWeight,
                MultiPendulumPenalty,
                DoublePendulumPenalty);
        }

        /// <summary> 仕様書準拠のデフォルトWave設定を取得する（テストやアセット未割り当て時のフォールバック用）。 </summary>
        public static WaveConfig GetDefaultWaveConfig(int wave)
        {
            int clampedIndex = Mathf.Clamp(wave - 1, 0, MAX_DEFINED_WAVE - 1);
            return DEFAULT_WAVES[clampedIndex];
        }

        /// <summary> _waves が未設定のときデフォルト定義をコピーして編集可能な状態にする。 </summary>
        public void EnsureWavesEditable()
        {
            if (_waves == null || _waves.Length == 0)
            {
                ResetToDefaultWaves();
            }
        }

        /// <summary> デフォルトの Wave 1〜8 定義をディープコピーして設定する（エディタ初期化用）。 </summary>
        public void ResetToDefaultWaves()
        {
            WaveConfig[] copy = new WaveConfig[DEFAULT_WAVES.Length];
            for (int i = 0; i < DEFAULT_WAVES.Length; i++)
            {
                WaveConfig src = DEFAULT_WAVES[i];
                IReadOnlyList<WeightedCountChoice> srcChoices = src.Choices;
                WeightedCountChoice[] choicesCopy = new WeightedCountChoice[srcChoices.Count];
                for (int j = 0; j < srcChoices.Count; j++)
                {
                    choicesCopy[j] = srcChoices[j];
                }

                copy[i] = new WaveConfig(src.SinglePendulumCount, src.DoublePendulumCount, choicesCopy);
            }

            _waves = copy;
        }

        private void Reset()
        {
            ResetToDefaultWaves();
        }
    }
}
