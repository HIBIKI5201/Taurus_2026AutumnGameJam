using System;
using Cysharp.Threading.Tasks;
using SymphonyFrameWork.Attribute;
using SymphonyFrameWork.System.SceneLoad;
using Taurus.Runtime.Stage;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Taurus.Develop.Debug
{
    /// <summary>
    /// デザイナーや開発者が Stage_Default などの Stage シーンを単体で開いて Play したときに、
    /// 常駐シーンである GameScene を SymphonyFramework の SceneLoader で安全に Additive ロードする開発補助スクリプト。
    /// GameScene から通常起動した場合は既にロード済みのため何もしない。
    /// </summary>
    public class StageSceneBootstrapper : MonoBehaviour
    {
        [SceneNameSelector]
        [SerializeField] private string _gameSceneName = "GameScene";

        private void Start()
        {
            // Orchestrator.AfterSceneLoad の初期同期完了後に実行するため Start から開始する
            EnsureGameSceneLoadedAsync().Forget();
        }

        private async UniTaskVoid EnsureGameSceneLoadedAsync()
        {
            if (string.IsNullOrWhiteSpace(_gameSceneName))
            {
                UnityEngine.Debug.LogWarning($"[{nameof(StageSceneBootstrapper)}] 常駐シーン名が未設定です。", this);
                return;
            }

            // 現在の Stage シーンをステージ優先度で追跡登録しておき、GameScene 追加後も Active 状態を保つ
            string currentSceneName = gameObject.scene.name;
            if (!string.IsNullOrWhiteSpace(currentSceneName))
            {
                SceneLoader.RegisterLoadedScene(currentSceneName, StageLoader.STAGE_SCENE_PRIORITY);
            }

            if (SceneLoader.GetExistScene(_gameSceneName, out _) || SceneLoader.IsExist(_gameSceneName))
            {
                return;
            }

            try
            {
                var request = new SceneLoadRequest(_gameSceneName, StageLoader.BASE_SCENE_PRIORITY);
                bool loaded = await SceneLoader.LoadSceneAsync(
                    request,
                    mode: LoadSceneMode.Additive,
                    token: this.GetCancellationTokenOnDestroy());

                if (!loaded)
                {
                    UnityEngine.Debug.LogWarning($"[{nameof(StageSceneBootstrapper)}] '{_gameSceneName}' をロードできませんでした。Build Settings を確認してください。", this);
                }
            }
            catch (OperationCanceledException)
            {
                // シーン破棄によるキャンセルは正常終了として扱う
            }
        }
    }
}
