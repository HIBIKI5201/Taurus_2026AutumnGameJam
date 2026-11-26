using UnityEngine;

namespace Taurus.Runtime.Stage
{
    /// <summary>
    /// Wave 1〜8+ の振り子構成と区間抽選重みを定義する ScriptableObject。
    /// 数値の正本は仕様書（pendulum-count-spec.md の重み表）と一致させており、
    /// インスペクターで上書きしない限りデフォルトテーブルが使用される。
    /// アセット本体は Assets/Settings/StageDefinition.asset に配置される。
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

        // インスペクターでバランス調整したい場合はここを編集する（空なら DEFAULT_WAVES が使われる）
        [SerializeField] private WaveConfig[] _waves;

        /// <summary> 指定Wave番号（1始まり）の構成を取得する。Wave 8以降は末尾（Wave 8）の設定を返す。 </summary>
        public WaveConfig GetWaveConfig(int wave)
        {
            WaveConfig[] table = (_waves != null && _waves.Length > 0) ? _waves : DEFAULT_WAVES;
            int clampedIndex = Mathf.Clamp(wave - 1, 0, table.Length - 1);
            return table[clampedIndex];
        }

        /// <summary> 仕様書準拠のデフォルトWave設定を取得する（テストやアセット未割り当て時のフォールバック用）。 </summary>
        public static WaveConfig GetDefaultWaveConfig(int wave)
        {
            int clampedIndex = Mathf.Clamp(wave - 1, 0, MAX_DEFINED_WAVE - 1);
            return DEFAULT_WAVES[clampedIndex];
        }

        private void Reset()
        {
            // ScriptableObject 新規作成時にデフォルト表を自動で流し込む
            _waves = DEFAULT_WAVES;
        }
    }
}
