using SymphonyFrameWork.System.ServiceLocate;
using UnityEngine;

namespace Taurus.Runtime.Stage
{
    /// <summary>
    /// デザイナーが編集する Stage シーンと、プログラマーが管理する Game シーンをつなぐ唯一の結合点。
    /// 各 Stage シーン（例: Stage_Default.unity）のルート配下に1つ配置し、
    /// 振り子を生成する支点位置（1本時の中央／2本時の左右）をインスペクターで指定する。
    /// シーンローカルな参照として ServiceLocator（Locator方式）へ自動登録・解除する。
    /// </summary>
    public class StageAnchor : MonoBehaviour
    {
        // 生成された振り子インスタンスをぶら下げる親Transform（未設定ならこのオブジェクト自身）
        [SerializeField] private Transform _pendulumRoot;

        // 振り子が1本だけのWave（Wave 1, 2, 5, 6）で使う中央の支点位置
        [SerializeField] private Transform _singlePivot;

        // 振り子が2本並ぶWave（Wave 3, 4, 7, 8+）で使う左右の支点位置（要素0=左、要素1=右）
        [SerializeField] private Transform[] _dualPivots;

        /// <summary> 振り子インスタンスを配置する親Transform。 </summary>
        public Transform PendulumRoot => _pendulumRoot != null ? _pendulumRoot : transform;

        private void OnEnable()
        {
            // シーンアンロードと共に破棄されるオブジェクトのため Singleton ではなく Locator で登録する
            if (!ServiceLocator.RegisterInstance(this, LocateTypeEnum.Locator))
            {
                if (ServiceLocator.TryGetInstance<StageAnchor>(out StageAnchor existing) && existing != this)
                {
                    Debug.LogWarning($"[{nameof(StageAnchor)}] 既に別の StageAnchor ({existing.name}) が登録されています。", this);
                }
            }
        }

        private void OnDisable()
        {
            ServiceLocator.UnregisterInstance(this);
        }

        /// <summary> 振り子の合計本数とインデックスに応じた支点Transformを返す。 </summary>
        public Transform GetPivot(int index, int totalCount)
        {
            // 1本構成のWaveでは中央支点を優先して返す
            if (totalCount <= 1)
            {
                if (_singlePivot != null)
                {
                    return _singlePivot;
                }

                if (_dualPivots != null && _dualPivots.Length > 0 && _dualPivots[0] != null)
                {
                    return _dualPivots[0];
                }

                Debug.LogWarning($"[{nameof(StageAnchor)}] 単振り子用の支点が未設定のため自身のTransformを使用します。", this);
                return transform;
            }

            // 2本以上の構成では配列から順番に支点を返す
            if (_dualPivots != null && index >= 0 && index < _dualPivots.Length && _dualPivots[index] != null)
            {
                return _dualPivots[index];
            }

            Debug.LogWarning($"[{nameof(StageAnchor)}] 支点インデックス {index} が未設定のため自身のTransformを使用します。", this);
            return transform;
        }

        /// <summary> エディタ構築ツール（ProjectSceneSetupTool）から支点参照を設定するためのヘルパー。 </summary>
        public void SetPivots(Transform pendulumRoot, Transform singlePivot, Transform[] dualPivots)
        {
            _pendulumRoot = pendulumRoot;
            _singlePivot = singlePivot;
            _dualPivots = dualPivots;
        }
    }
}
