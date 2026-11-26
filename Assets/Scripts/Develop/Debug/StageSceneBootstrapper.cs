using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Taurus.Develop.Debug
{
    /// <summary>
    /// デザイナーや開発者が Stage_Default などの Stage シーンを単体で開いて Play したときに、
    /// 常駐シーンである GameScene を自動で Additive ロードする開発補助スクリプト。
    /// GameScene から通常起動した場合は既にロード済みのため何もしない。
    /// </summary>
    public class StageSceneBootstrapper : MonoBehaviour
    {
        [SerializeField] private string _gameSceneName = "GameScene";

        private void Awake()
        {
            EnsureGameSceneLoadedAsync().Forget();
        }

        private async UniTaskVoid EnsureGameSceneLoadedAsync()
        {
            Scene gameScene = SceneManager.GetSceneByName(_gameSceneName);
            if (gameScene.isLoaded)
            {
                return;
            }

            AsyncOperation loadOp = SceneManager.LoadSceneAsync(_gameSceneName, LoadSceneMode.Additive);
            if (loadOp == null)
            {
                UnityEngine.Debug.LogWarning($"[{nameof(StageSceneBootstrapper)}] '{_gameSceneName}' をロードできませんでした。Build Settings を確認してください。", this);
                return;
            }

            await loadOp.ToUniTask(cancellationToken: this.GetCancellationTokenOnDestroy());
        }
    }
}
