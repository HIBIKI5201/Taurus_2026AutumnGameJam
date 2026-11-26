using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Taurus.Runtime.Stage
{
    /// <summary>
    /// GameScene を常駐させたまま、背景と支点アンカーを持つ Stage シーンを
    /// UniTask で非同期 Additive ロード／切り替えするローダー。
    /// チーム作業でシーン競合を避けるため、背景・3D配置は Stage シーン側に分離している。
    /// </summary>
    public class StageLoader : MonoBehaviour
    {
        // 起動時に自動で読み込むデフォルトのステージシーン名（Build Settings に登録必須）
        [SerializeField] private string _defaultStageSceneName = "Stage_Default";

        private string _loadedSceneName;
        private StageAnchor _currentAnchor;

        /// <summary> 現在ロードされているステージのアンカー。未ロード時は null。 </summary>
        public StageAnchor CurrentAnchor => _currentAnchor;

        /// <summary> 現在ロードされているステージシーン名。 </summary>
        public string LoadedSceneName => _loadedSceneName;

        /// <summary> デフォルトステージを非同期で加算ロードする。 </summary>
        public UniTask<StageAnchor> LoadDefaultStageAsync(CancellationToken cancellationToken = default)
        {
            return LoadStageAsync(_defaultStageSceneName, cancellationToken);
        }

        /// <summary> 指定したStageシーンをAdditiveロードし、シーン内のStageAnchorを取得して返す。 </summary>
        public async UniTask<StageAnchor> LoadStageAsync(string sceneName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogError($"[{nameof(StageLoader)}] ステージシーン名が空です。", this);
                return null;
            }

            // 既に別のステージが開かれている場合は先にアンロードして重複を防ぐ
            if (!string.IsNullOrEmpty(_loadedSceneName) && _loadedSceneName != sceneName)
            {
                await UnloadCurrentStageAsync(cancellationToken);
            }

            // Stage シーン単体再生時（StageSceneBootstrapper経由）など既にロード済みなら再ロードせず流用する
            Scene existingScene = SceneManager.GetSceneByName(sceneName);
            if (!existingScene.isLoaded)
            {
                AsyncOperation loadOp = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                if (loadOp == null)
                {
                    Debug.LogError($"[{nameof(StageLoader)}] シーン '{sceneName}' のロードを開始できませんでした。Build Settings を確認してください。", this);
                    return null;
                }

                await loadOp.ToUniTask(cancellationToken: cancellationToken);
                existingScene = SceneManager.GetSceneByName(sceneName);
            }

            _loadedSceneName = sceneName;
            _currentAnchor = FindStageAnchor(existingScene);

            if (_currentAnchor == null)
            {
                Debug.LogError($"[{nameof(StageLoader)}] シーン '{sceneName}' に StageAnchor が見つかりません。", this);
            }

            return _currentAnchor;
        }

        /// <summary> 現在ロード中のStageシーンをアンロードする。 </summary>
        public async UniTask UnloadCurrentStageAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_loadedSceneName))
            {
                return;
            }

            Scene scene = SceneManager.GetSceneByName(_loadedSceneName);
            _loadedSceneName = null;
            _currentAnchor = null;

            if (scene.isLoaded)
            {
                AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(scene);
                if (unloadOp != null)
                {
                    await unloadOp.ToUniTask(cancellationToken: cancellationToken);
                }
            }
        }

        /// <summary> ロードした Stage シーンのルートオブジェクト配下から StageAnchor を探す。 </summary>
        private static StageAnchor FindStageAnchor(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                StageAnchor anchor = roots[i].GetComponentInChildren<StageAnchor>(true);
                if (anchor != null)
                {
                    return anchor;
                }
            }

            return null;
        }
    }
}
