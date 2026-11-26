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

        // 三重振り子（3段連鎖の追従負荷）の追加負荷係数（三重振り子1本ごとの加算倍率）
        [SerializeField, Min(0f)] private float _triplePendulumPenalty = 1.10f;

        [Header("Wave 定義")]
        // インスペクターでバランス調整したい場合はここを編集する（空なら DEFAULT_WAVES が使われる）
        [SerializeField] private WaveConfig[] _waves;

        /// <summary> 総合難易度スコアにおける L2 ピーク評価の重み。 </summary>
        public float PeakWeight => Mathf.Clamp01(_peakWeight);

        /// <summary> 複数本振り子の同時視認ペナルティ係数。 </summary>
        public float MultiPendulumPenalty => Mathf.Max(0f, _multiPendulumPenalty);

        /// <summary> 二重振り子の追従負荷ペナルティ係数。 </summary>
        public float DoublePendulumPenalty => Mathf.Max(0f, _doublePendulumPenalty);

        /// <summary> 三重振り子の追従負荷ペナルティ係数。 </summary>
        public float TriplePendulumPenalty => Mathf.Max(0f, _triplePendulumPenalty);

        /// <summary> 定義済み Wave 一覧（未設定時はデフォルト8 Waveを返す）。 </summary>
        public IReadOnlyList<WaveConfig> Waves => (_waves != null && _waves.Length > 0) ? _waves : DEFAULT_WAVES;

        /// <summary> 指定Wave番号（1始まり）の構成を取得する。定義数を超える場合は末尾の設定を返す。 </summary>
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
                DoublePendulumPenalty,
                TriplePendulumPenalty);
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

        /// <summary>
        /// UX論文知見（平坦型=簡単・線形増加型=チュートリアル的・二次曲線型=高難度スパイク）と
        /// Discord 感情曲線（Wave 1〜15・三重振り子含む）に準拠した最適 15 Wave プリセットを適用する。
        /// </summary>
        public void ApplyOptimal15WavePreset()
        {
            _waves = CreateOptimal15Waves();
        }

        /// <summary> UX論文＋Discord感情曲線＋区間形状理論に基づく最適 15 Wave 定義を生成する。 </summary>
        public static WaveConfig[] CreateOptimal15Waves()
        {
            return new[]
            {
                // W01: 平坦型（超簡単・導入 / 1重×1）
                BuildWave(1, 0, 0, 1, 3, new[]
                {
                    new StageIntervalConfig("前半(平坦)", 4f, new[] { new WeightedCountChoice(75, 0), new WeightedCountChoice(25, 1) }, true, 0, 2),
                    new StageIntervalConfig("後半(平坦)", 4f, new[] { new WeightedCountChoice(72, 0), new WeightedCountChoice(28, 1) }, true, 1, 2),
                }),
                // W02: 線形増加型（チュートリアル的 / 1重×1）
                BuildWave(1, 0, 0, 2, 5, new[]
                {
                    new StageIntervalConfig("序盤(低)", 3f, new[] { new WeightedCountChoice(78, 0), new WeightedCountChoice(22, 1) }, true, 0, 1),
                    new StageIntervalConfig("中盤(中)", 3f, new[] { new WeightedCountChoice(60, 0), new WeightedCountChoice(35, 1), new WeightedCountChoice(5, 2) }, true, 1, 2),
                    new StageIntervalConfig("終盤(高)", 2f, new[] { new WeightedCountChoice(45, 0), new WeightedCountChoice(42, 1), new WeightedCountChoice(13, 2) }, true, 1, 2),
                }),
                // W03: 線形増加型（チュートリアル仕上げ / 1重×1）
                BuildWave(1, 0, 0, 3, 7, new[]
                {
                    new StageIntervalConfig("序盤(低)", 3f, new[] { new WeightedCountChoice(66, 0), new WeightedCountChoice(30, 1), new WeightedCountChoice(4, 2) }, true, 1, 2),
                    new StageIntervalConfig("中盤(中)", 3f, new[] { new WeightedCountChoice(50, 0), new WeightedCountChoice(34, 1), new WeightedCountChoice(16, 2) }, true, 1, 3),
                    new StageIntervalConfig("終盤(高)", 2f, new[] { new WeightedCountChoice(36, 0), new WeightedCountChoice(38, 1), new WeightedCountChoice(20, 2), new WeightedCountChoice(6, 3) }, true, 1, 3),
                }),
                // W04: 平坦型（振り子×2初登場・新ギミック慣れ / 1重×2）
                BuildWave(2, 0, 0, 2, 4, new[]
                {
                    new StageIntervalConfig("前半(平坦)", 4f, new[] { new WeightedCountChoice(68, 0), new WeightedCountChoice(28, 1), new WeightedCountChoice(4, 2) }, true, 1, 2),
                    new StageIntervalConfig("後半(平坦)", 4f, new[] { new WeightedCountChoice(66, 0), new WeightedCountChoice(30, 1), new WeightedCountChoice(4, 2) }, true, 1, 2),
                }),
                // W05: 線形増加型（振り子×2加速 / 1重×2）
                BuildWave(2, 0, 0, 3, 6, new[]
                {
                    new StageIntervalConfig("序盤(低)", 3f, new[] { new WeightedCountChoice(68, 0), new WeightedCountChoice(28, 1), new WeightedCountChoice(4, 2) }, true, 1, 2),
                    new StageIntervalConfig("中盤(中)", 3f, new[] { new WeightedCountChoice(52, 0), new WeightedCountChoice(34, 1), new WeightedCountChoice(14, 2) }, true, 1, 3),
                    new StageIntervalConfig("終盤(高)", 2f, new[] { new WeightedCountChoice(38, 0), new WeightedCountChoice(38, 1), new WeightedCountChoice(18, 2), new WeightedCountChoice(6, 3) }, true, 1, 3),
                }),
                // W06: 二次曲線型（振り子×2「難・一度目の死」 / 1重×2）
                BuildWave(2, 0, 0, 4, 7, new[]
                {
                    new StageIntervalConfig("序盤(静)", 3f, new[] { new WeightedCountChoice(68, 0), new WeightedCountChoice(28, 1), new WeightedCountChoice(4, 2) }, true, 1, 2),
                    new StageIntervalConfig("中盤(加速)", 3f, new[] { new WeightedCountChoice(44, 0), new WeightedCountChoice(36, 1), new WeightedCountChoice(16, 2), new WeightedCountChoice(4, 3) }, true, 2, 3),
                    new StageIntervalConfig("終盤(急騰)", 2f, new[] { new WeightedCountChoice(20, 0), new WeightedCountChoice(35, 1), new WeightedCountChoice(30, 2), new WeightedCountChoice(15, 3) }, true, 2, 4),
                }),
                // W07: 平坦型（易・二重振り子初登場 / 2重×1）
                BuildWave(0, 1, 0, 2, 4, new[]
                {
                    new StageIntervalConfig("前半(平坦)", 4f, new[] { new WeightedCountChoice(66, 0), new WeightedCountChoice(30, 1), new WeightedCountChoice(4, 2) }, true, 1, 2),
                    new StageIntervalConfig("後半(平坦)", 4f, new[] { new WeightedCountChoice(64, 0), new WeightedCountChoice(32, 1), new WeightedCountChoice(4, 2) }, true, 1, 2),
                }),
                // W08: 線形増加型（二重・横ばい帯① / 2重×1）
                BuildWave(0, 1, 0, 3, 5, new[]
                {
                    new StageIntervalConfig("序盤(低)", 3f, new[] { new WeightedCountChoice(72, 0), new WeightedCountChoice(26, 1), new WeightedCountChoice(2, 2) }, true, 1, 2),
                    new StageIntervalConfig("中盤(中)", 3f, new[] { new WeightedCountChoice(58, 0), new WeightedCountChoice(34, 1), new WeightedCountChoice(8, 2) }, true, 1, 2),
                    new StageIntervalConfig("終盤(高)", 2f, new[] { new WeightedCountChoice(44, 0), new WeightedCountChoice(42, 1), new WeightedCountChoice(14, 2) }, true, 1, 2),
                }),
                // W09: 線形増加型（二重・横ばい帯② / 2重×1）
                BuildWave(0, 1, 0, 3, 5, new[]
                {
                    new StageIntervalConfig("序盤(低)", 3f, new[] { new WeightedCountChoice(70, 0), new WeightedCountChoice(26, 1), new WeightedCountChoice(4, 2) }, true, 1, 2),
                    new StageIntervalConfig("中盤(中)", 3f, new[] { new WeightedCountChoice(54, 0), new WeightedCountChoice(34, 1), new WeightedCountChoice(12, 2) }, true, 1, 3),
                    new StageIntervalConfig("終盤(高)", 2f, new[] { new WeightedCountChoice(40, 0), new WeightedCountChoice(42, 1), new WeightedCountChoice(18, 2) }, true, 1, 2),
                }),
                // W10: 二次曲線型（二重・溜め / 2重×1）
                BuildWave(0, 1, 0, 3, 6, new[]
                {
                    new StageIntervalConfig("序盤(静)", 3f, new[] { new WeightedCountChoice(72, 0), new WeightedCountChoice(26, 1), new WeightedCountChoice(2, 2) }, true, 1, 2),
                    new StageIntervalConfig("中盤(加速)", 3f, new[] { new WeightedCountChoice(52, 0), new WeightedCountChoice(34, 1), new WeightedCountChoice(12, 2), new WeightedCountChoice(2, 3) }, true, 1, 3),
                    new StageIntervalConfig("終盤(急騰)", 2f, new[] { new WeightedCountChoice(30, 0), new WeightedCountChoice(40, 1), new WeightedCountChoice(24, 2), new WeightedCountChoice(6, 3) }, true, 1, 3),
                }),
                // W11: 平坦型（💀×2初登場・急上昇 / 2重×2）
                BuildWave(0, 2, 0, 2, 4, new[]
                {
                    new StageIntervalConfig("前半(平坦)", 4f, new[] { new WeightedCountChoice(68, 0), new WeightedCountChoice(28, 1), new WeightedCountChoice(4, 2) }, true, 1, 2),
                    new StageIntervalConfig("後半(平坦)", 4f, new[] { new WeightedCountChoice(65, 0), new WeightedCountChoice(30, 1), new WeightedCountChoice(5, 2) }, true, 1, 2),
                }),
                // W12: 二次曲線型（💀×2猛攻 / 2重×2）
                BuildWave(0, 2, 0, 3, 5, new[]
                {
                    new StageIntervalConfig("序盤(静)", 3f, new[] { new WeightedCountChoice(72, 0), new WeightedCountChoice(26, 1), new WeightedCountChoice(2, 2) }, true, 1, 2),
                    new StageIntervalConfig("中盤(加速)", 3f, new[] { new WeightedCountChoice(54, 0), new WeightedCountChoice(34, 1), new WeightedCountChoice(12, 2) }, true, 1, 3),
                    new StageIntervalConfig("終盤(急騰)", 2f, new[] { new WeightedCountChoice(30, 0), new WeightedCountChoice(38, 1), new WeightedCountChoice(24, 2), new WeightedCountChoice(8, 3) }, true, 1, 3),
                }),
                // W13: 平坦型（三重振り子初登場 / 3重×1 + 1重×1）
                BuildWave(1, 0, 1, 3, 5, new[]
                {
                    new StageIntervalConfig("前半(平坦)", 4f, new[] { new WeightedCountChoice(62, 0), new WeightedCountChoice(30, 1), new WeightedCountChoice(8, 2) }, true, 1, 3),
                    new StageIntervalConfig("後半(平坦)", 4f, new[] { new WeightedCountChoice(60, 0), new WeightedCountChoice(32, 1), new WeightedCountChoice(8, 2) }, true, 1, 3),
                }),
                // W14: 二次曲線型（三重＋二重 / 3重×1 + 2重×1）
                BuildWave(0, 1, 1, 3, 5, new[]
                {
                    new StageIntervalConfig("序盤(静)", 3f, new[] { new WeightedCountChoice(74, 0), new WeightedCountChoice(24, 1), new WeightedCountChoice(2, 2) }, true, 1, 2),
                    new StageIntervalConfig("中盤(加速)", 3f, new[] { new WeightedCountChoice(58, 0), new WeightedCountChoice(34, 1), new WeightedCountChoice(8, 2) }, true, 1, 2),
                    new StageIntervalConfig("終盤(急騰)", 2f, new[] { new WeightedCountChoice(32, 0), new WeightedCountChoice(40, 1), new WeightedCountChoice(22, 2), new WeightedCountChoice(6, 3) }, true, 1, 3),
                }),
                // W15: 二次曲線型（三重×2 Final Peak / 3重×2）
                BuildWave(0, 0, 2, 3, 5, new[]
                {
                    new StageIntervalConfig("序盤(静)", 3f, new[] { new WeightedCountChoice(76, 0), new WeightedCountChoice(22, 1), new WeightedCountChoice(2, 2) }, true, 1, 2),
                    new StageIntervalConfig("中盤(加速)", 3f, new[] { new WeightedCountChoice(60, 0), new WeightedCountChoice(32, 1), new WeightedCountChoice(8, 2) }, true, 1, 2),
                    new StageIntervalConfig("終盤(極限)", 2f, new[] { new WeightedCountChoice(34, 0), new WeightedCountChoice(38, 1), new WeightedCountChoice(22, 2), new WeightedCountChoice(6, 3) }, true, 1, 3),
                }),
            };
        }

        private static WaveConfig BuildWave(int singleCount, int doubleCount, int tripleCount, int waveMinQ, int waveMaxQ, StageIntervalConfig[] intervals)
        {
            WaveConfig wave = new WaveConfig(singleCount, doubleCount, intervals, DrawStopRule.StopWhenNoFit, 1.0f, 0f, tripleCount);
            wave.SetWaveQuantityConstraint(true, waveMinQ, waveMaxQ);
            return wave;
        }

        private void Reset()
        {
            ResetToDefaultWaves();
        }
    }
}
