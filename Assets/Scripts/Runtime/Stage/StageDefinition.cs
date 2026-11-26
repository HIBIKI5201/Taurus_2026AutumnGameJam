using System.Collections.Generic;
using UnityEngine;

namespace Taurus.Runtime.Stage
{
    /// <summary>
    /// 任意数の Wave の振り子構成・分割区間・量制約 [Q_min, Q_max]・区間抽選重みを定義する ScriptableObject。
    /// カスタム Inspector（StageDefinitionEditor）から Wave の追加・複製・並び替え・削除や、
    /// 数理モデル・前向き/後ろ向きDPによる難易度曲線と制約達成確率をリアルタイムに確認しながらレベルデザインを調整できる。
    /// </summary>
    [CreateAssetMenu(fileName = "StageDefinition", menuName = "Taurus/Stage Definition")]
    public class StageDefinition : ScriptableObject
    {
        // 仕様書（pendulum-count-spec.md 第3章）の重み表に完全一致する初期フォールバック定義
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

        // 複数本の振り子を同時視認する際の追加負荷係数（独立・非同期動作の分割注意負荷を反映）
        [SerializeField, Min(0f)] private float _multiPendulumPenalty = 0.40f;

        // 二重振り子（親玉に連動する子玉の追従負荷）の追加負荷係数（二重振り子1本ごとの加算倍率）
        [SerializeField, Min(0f)] private float _doublePendulumPenalty = 0.50f;

        // 三重振り子（3段連鎖の追従負荷）の追加負荷係数（三重振り子1本ごとの加算倍率）
        [SerializeField, Min(0f)] private float _triplePendulumPenalty = 1.10f;

        [Header("Wave 拡張・超過時設定")]
        // 定義済み Wave 数を超えたときの振る舞い（末尾固定 / 先頭ループ / 末尾ループ）
        [SerializeField] private WaveOverflowBehavior _overflowBehavior = WaveOverflowBehavior.ClampToLast;

        // LoopLastSection 選択時に繰り返す末尾の Wave 数
        [SerializeField, Min(1)] private int _loopTailCount = 3;

        [Header("Wave 定義（任意数に拡張可能）")]
        // インスペクターまたは上部ダッシュボードから任意の Wave 数へ追加・複製・並び替え・削除できる
        [SerializeField] private WaveConfig[] _waves;

        /// <summary> 総合難易度スコアにおける L2 ピーク評価の重み。 </summary>
        public float PeakWeight => Mathf.Clamp01(_peakWeight);

        /// <summary> 複数本振り子の同時視認ペナルティ係数。 </summary>
        public float MultiPendulumPenalty => Mathf.Max(0f, _multiPendulumPenalty);

        /// <summary> 二重振り子の追従負荷ペナルティ係数。 </summary>
        public float DoublePendulumPenalty => Mathf.Max(0f, _doublePendulumPenalty);

        /// <summary> 三重振り子の追従負荷ペナルティ係数。 </summary>
        public float TriplePendulumPenalty => Mathf.Max(0f, _triplePendulumPenalty);

        /// <summary> 定義済み Wave 数を超えたときの振る舞い。 </summary>
        public WaveOverflowBehavior OverflowBehavior => _overflowBehavior;

        /// <summary> LoopLastSection 選択時にループする末尾 Wave 数。 </summary>
        public int LoopTailCount => Mathf.Max(1, _loopTailCount);

        /// <summary> 定義済み Wave 一覧（未設定時はデフォルト定義を返す）。 </summary>
        public IReadOnlyList<WaveConfig> Waves => (_waves != null && _waves.Length > 0) ? _waves : DEFAULT_WAVES;

        /// <summary> 指定Wave番号（1始まり）の構成を取得する。定義数を超える場合は OverflowBehavior に従う。 </summary>
        public WaveConfig GetWaveConfig(int wave)
        {
            WaveConfig[] table = (_waves != null && _waves.Length > 0) ? _waves : DEFAULT_WAVES;
            int index = ResolveWaveIndex(wave, table.Length, _overflowBehavior, LoopTailCount);
            return table[index];
        }

        /// <summary> Wave番号（1始まり）をテーブル長と超過ポリシーに従ってインデックスへ変換する。 </summary>
        public static int ResolveWaveIndex(int wave, int waveCount, WaveOverflowBehavior behavior, int loopTailCount = 3)
        {
            if (waveCount <= 1)
            {
                return 0;
            }

            int zeroBased = Mathf.Max(0, wave - 1);
            if (zeroBased < waveCount)
            {
                return zeroBased;
            }

            switch (behavior)
            {
                case WaveOverflowBehavior.LoopFromStart:
                    return zeroBased % waveCount;
                case WaveOverflowBehavior.LoopLastSection:
                    int tail = Mathf.Clamp(loopTailCount, 1, waveCount);
                    int start = waveCount - tail;
                    return start + ((zeroBased - start) % tail);
                default:
                    return waveCount - 1;
            }
        }

        /// <summary> 超過時の振る舞い設定を更新する。 </summary>
        public void SetOverflowBehavior(WaveOverflowBehavior behavior, int loopTailCount = 3)
        {
            _overflowBehavior = behavior;
            _loopTailCount = Mathf.Max(1, loopTailCount);
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
            int clampedIndex = Mathf.Clamp(wave - 1, 0, DEFAULT_WAVES.Length - 1);
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

        /// <summary> 末尾に新しい Wave を追加する（未指定時は末尾 Wave のディープコピーを追加）。 </summary>
        public int AddWave(WaveConfig wave = null)
        {
            EnsureWavesEditable();
            WaveConfig toAdd = wave != null
                ? wave.Clone()
                : _waves[_waves.Length - 1].Clone();

            List<WaveConfig> list = new List<WaveConfig>(_waves) { toAdd };
            _waves = list.ToArray();
            return _waves.Length - 1;
        }

        /// <summary> 指定インデックスの Wave をディープコピーして直後に挿入する。 </summary>
        public int DuplicateWave(int index)
        {
            EnsureWavesEditable();
            int clamped = Mathf.Clamp(index, 0, _waves.Length - 1);
            List<WaveConfig> list = new List<WaveConfig>(_waves);
            list.Insert(clamped + 1, _waves[clamped].Clone());
            _waves = list.ToArray();
            return clamped + 1;
        }

        /// <summary> 指定インデックスの Wave を削除する（最低 1 Wave は残す）。 </summary>
        public bool RemoveWave(int index)
        {
            EnsureWavesEditable();
            if (_waves.Length <= 1 || index < 0 || index >= _waves.Length)
            {
                return false;
            }

            List<WaveConfig> list = new List<WaveConfig>(_waves);
            list.RemoveAt(index);
            _waves = list.ToArray();
            return true;
        }

        /// <summary> 指定インデックスの Wave を別の位置へ移動する。 </summary>
        public bool MoveWave(int fromIndex, int toIndex)
        {
            EnsureWavesEditable();
            if (fromIndex < 0 || fromIndex >= _waves.Length || toIndex < 0 || toIndex >= _waves.Length || fromIndex == toIndex)
            {
                return false;
            }

            List<WaveConfig> list = new List<WaveConfig>(_waves);
            WaveConfig item = list[fromIndex];
            list.RemoveAt(fromIndex);
            list.Insert(toIndex, item);
            _waves = list.ToArray();
            return true;
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
        /// Discord 感情曲線（Wave 1〜15・三重振り子含む）に加え、
        /// 複数振り子の独立・非同期初期位置による認知負荷増を相殺するようカウント量と頻度を抑えた最適 15 Wave プリセットを適用する。
        /// </summary>
        public void ApplyOptimal15WavePreset()
        {
            _multiPendulumPenalty = 0.40f;
            _waves = CreateOptimal15Waves();
        }

        /// <summary> UX論文＋Discord感情曲線＋独立初期位置負荷調整に基づく最適 15 Wave 定義を生成する。 </summary>
        public static WaveConfig[] CreateOptimal15Waves()
        {
            return new[]
            {
                // W01: 平坦型（チュートリアル導入 / 1重×1 / 前半4秒で最低1回・後半4秒で最低1回を保証）
                BuildWave(1, 0, 0, 2, 3, new[]
                {
                    new StageIntervalConfig("前半(平坦)", 4f, new[] { new WeightedCountChoice(76, 0), new WeightedCountChoice(24, 1) }, true, 1, 2),
                    new StageIntervalConfig("後半(平坦)", 4f, new[] { new WeightedCountChoice(74, 0), new WeightedCountChoice(26, 1) }, true, 1, 2),
                }),
                // W02: 線形増加型（チュートリアル的 / 1重×1）
                BuildWave(1, 0, 0, 2, 5, new[]
                {
                    new StageIntervalConfig("序盤(低)", 3f, new[] { new WeightedCountChoice(78, 0), new WeightedCountChoice(22, 1) }, true, 0, 1),
                    new StageIntervalConfig("中盤(中)", 3f, new[] { new WeightedCountChoice(62, 0), new WeightedCountChoice(33, 1), new WeightedCountChoice(5, 2) }, true, 1, 2),
                    new StageIntervalConfig("終盤(高)", 2f, new[] { new WeightedCountChoice(48, 0), new WeightedCountChoice(40, 1), new WeightedCountChoice(12, 2) }, true, 1, 2),
                }),
                // W03: 線形増加型（チュートリアル仕上げ / 1重×1）
                BuildWave(1, 0, 0, 3, 6, new[]
                {
                    new StageIntervalConfig("序盤(低)", 3f, new[] { new WeightedCountChoice(68, 0), new WeightedCountChoice(28, 1), new WeightedCountChoice(4, 2) }, true, 1, 2),
                    new StageIntervalConfig("中盤(中)", 3f, new[] { new WeightedCountChoice(54, 0), new WeightedCountChoice(34, 1), new WeightedCountChoice(12, 2) }, true, 1, 2),
                    new StageIntervalConfig("終盤(高)", 2f, new[] { new WeightedCountChoice(40, 0), new WeightedCountChoice(38, 1), new WeightedCountChoice(18, 2), new WeightedCountChoice(4, 3) }, true, 1, 3),
                }),
                // W04: 平坦型（振り子×2初登場・独立動作に合わせて頻度軽減 / 1重×2）
                BuildWave(2, 0, 0, 1, 3, new[]
                {
                    new StageIntervalConfig("前半(平坦)", 4f, new[] { new WeightedCountChoice(76, 0), new WeightedCountChoice(22, 1), new WeightedCountChoice(2, 2) }, true, 0, 2),
                    new StageIntervalConfig("後半(平坦)", 4f, new[] { new WeightedCountChoice(74, 0), new WeightedCountChoice(24, 1), new WeightedCountChoice(2, 2) }, true, 1, 2),
                }),
                // W05: 線形増加型（振り子×2加速・独立動作調整 / 1重×2）
                BuildWave(2, 0, 0, 2, 4, new[]
                {
                    new StageIntervalConfig("序盤(低)", 3f, new[] { new WeightedCountChoice(76, 0), new WeightedCountChoice(24, 1) }, true, 0, 1),
                    new StageIntervalConfig("中盤(中)", 3f, new[] { new WeightedCountChoice(62, 0), new WeightedCountChoice(32, 1), new WeightedCountChoice(6, 2) }, true, 1, 2),
                    new StageIntervalConfig("終盤(高)", 2f, new[] { new WeightedCountChoice(48, 0), new WeightedCountChoice(40, 1), new WeightedCountChoice(12, 2) }, true, 1, 2),
                }),
                // W06: 二次曲線型（振り子×2「難・一度目の死」 / 1重×2）
                BuildWave(2, 0, 0, 3, 5, new[]
                {
                    new StageIntervalConfig("序盤(静)", 3f, new[] { new WeightedCountChoice(76, 0), new WeightedCountChoice(24, 1) }, true, 0, 1),
                    new StageIntervalConfig("中盤(加速)", 3f, new[] { new WeightedCountChoice(56, 0), new WeightedCountChoice(34, 1), new WeightedCountChoice(10, 2) }, true, 1, 2),
                    new StageIntervalConfig("終盤(急騰)", 2f, new[] { new WeightedCountChoice(28, 0), new WeightedCountChoice(40, 1), new WeightedCountChoice(24, 2), new WeightedCountChoice(8, 3) }, true, 2, 3),
                }),
                // W07: 平坦型（易・二重振り子初登場 / 2重×1）
                BuildWave(0, 1, 0, 2, 3, new[]
                {
                    new StageIntervalConfig("前半(平坦)", 4f, new[] { new WeightedCountChoice(74, 0), new WeightedCountChoice(24, 1), new WeightedCountChoice(2, 2) }, true, 1, 2),
                    new StageIntervalConfig("後半(平坦)", 4f, new[] { new WeightedCountChoice(72, 0), new WeightedCountChoice(26, 1), new WeightedCountChoice(2, 2) }, true, 1, 2),
                }),
                // W08: 線形増加型（二重・横ばい帯① / 2重×1）
                BuildWave(0, 1, 0, 2, 4, new[]
                {
                    new StageIntervalConfig("序盤(低)", 3f, new[] { new WeightedCountChoice(78, 0), new WeightedCountChoice(22, 1) }, true, 0, 1),
                    new StageIntervalConfig("中盤(中)", 3f, new[] { new WeightedCountChoice(64, 0), new WeightedCountChoice(32, 1), new WeightedCountChoice(4, 2) }, true, 1, 2),
                    new StageIntervalConfig("終盤(高)", 2f, new[] { new WeightedCountChoice(50, 0), new WeightedCountChoice(40, 1), new WeightedCountChoice(10, 2) }, true, 1, 2),
                }),
                // W09: 線形増加型（二重・横ばい帯② / 2重×1）
                BuildWave(0, 1, 0, 2, 4, new[]
                {
                    new StageIntervalConfig("序盤(低)", 3f, new[] { new WeightedCountChoice(74, 0), new WeightedCountChoice(26, 1) }, true, 0, 1),
                    new StageIntervalConfig("中盤(中)", 3f, new[] { new WeightedCountChoice(58, 0), new WeightedCountChoice(34, 1), new WeightedCountChoice(8, 2) }, true, 1, 2),
                    new StageIntervalConfig("終盤(高)", 2f, new[] { new WeightedCountChoice(44, 0), new WeightedCountChoice(42, 1), new WeightedCountChoice(14, 2) }, true, 1, 2),
                }),
                // W10: 二次曲線型（二重・溜め / 2重×1）
                BuildWave(0, 1, 0, 3, 5, new[]
                {
                    new StageIntervalConfig("序盤(静)", 3f, new[] { new WeightedCountChoice(76, 0), new WeightedCountChoice(24, 1) }, true, 0, 1),
                    new StageIntervalConfig("中盤(加速)", 3f, new[] { new WeightedCountChoice(56, 0), new WeightedCountChoice(34, 1), new WeightedCountChoice(10, 2) }, true, 1, 2),
                    new StageIntervalConfig("終盤(急騰)", 2f, new[] { new WeightedCountChoice(32, 0), new WeightedCountChoice(42, 1), new WeightedCountChoice(20, 2), new WeightedCountChoice(6, 3) }, true, 2, 3),
                }),
                // W11: 平坦型（💀×2初登場・4玉独立動作に合わせて頻度抑制 / 2重×2）
                BuildWave(0, 2, 0, 1, 3, new[]
                {
                    new StageIntervalConfig("前半(平坦)", 4f, new[] { new WeightedCountChoice(76, 0), new WeightedCountChoice(22, 1), new WeightedCountChoice(2, 2) }, true, 0, 2),
                    new StageIntervalConfig("後半(平坦)", 4f, new[] { new WeightedCountChoice(74, 0), new WeightedCountChoice(24, 1), new WeightedCountChoice(2, 2) }, true, 1, 2),
                }),
                // W12: 二次曲線型（💀×2猛攻 / 2重×2）
                BuildWave(0, 2, 0, 2, 4, new[]
                {
                    new StageIntervalConfig("序盤(静)", 3f, new[] { new WeightedCountChoice(78, 0), new WeightedCountChoice(22, 1) }, true, 0, 1),
                    new StageIntervalConfig("中盤(加速)", 3f, new[] { new WeightedCountChoice(62, 0), new WeightedCountChoice(32, 1), new WeightedCountChoice(6, 2) }, true, 1, 2),
                    new StageIntervalConfig("終盤(急騰)", 2f, new[] { new WeightedCountChoice(40, 0), new WeightedCountChoice(42, 1), new WeightedCountChoice(18, 2) }, true, 1, 2),
                }),
                // W13: 平坦型（三重振り子初登場 / 3重×1 + 1重×1）
                BuildWave(1, 0, 1, 2, 3, new[]
                {
                    new StageIntervalConfig("前半(平坦)", 4f, new[] { new WeightedCountChoice(74, 0), new WeightedCountChoice(24, 1), new WeightedCountChoice(2, 2) }, true, 1, 2),
                    new StageIntervalConfig("後半(平坦)", 4f, new[] { new WeightedCountChoice(72, 0), new WeightedCountChoice(26, 1), new WeightedCountChoice(2, 2) }, true, 1, 2),
                }),
                // W14: 二次曲線型（三重＋二重 / 3重×1 + 2重×1）
                BuildWave(0, 1, 1, 2, 4, new[]
                {
                    new StageIntervalConfig("序盤(静)", 3f, new[] { new WeightedCountChoice(80, 0), new WeightedCountChoice(20, 1) }, true, 0, 1),
                    new StageIntervalConfig("中盤(加速)", 3f, new[] { new WeightedCountChoice(64, 0), new WeightedCountChoice(30, 1), new WeightedCountChoice(6, 2) }, true, 1, 2),
                    new StageIntervalConfig("終盤(急騰)", 2f, new[] { new WeightedCountChoice(42, 0), new WeightedCountChoice(42, 1), new WeightedCountChoice(16, 2) }, true, 1, 2),
                }),
                // W15: 二次曲線型（三重×2 Final Peak / 3重×2）
                BuildWave(0, 0, 2, 2, 4, new[]
                {
                    new StageIntervalConfig("序盤(静)", 3f, new[] { new WeightedCountChoice(82, 0), new WeightedCountChoice(18, 1) }, true, 0, 1),
                    new StageIntervalConfig("中盤(加速)", 3f, new[] { new WeightedCountChoice(66, 0), new WeightedCountChoice(28, 1), new WeightedCountChoice(6, 2) }, true, 1, 2),
                    new StageIntervalConfig("終盤(極限)", 2f, new[] { new WeightedCountChoice(44, 0), new WeightedCountChoice(40, 1), new WeightedCountChoice(16, 2) }, true, 1, 2),
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
