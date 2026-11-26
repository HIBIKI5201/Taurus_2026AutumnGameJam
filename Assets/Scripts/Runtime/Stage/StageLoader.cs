using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using SymphonyFramework.Attribute;
using SymphonyFramework.Scenes;
using SymphonyFrameWork.System.SceneLoad;
using SymphonyFrameWork.System.ServiceLocate;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Taurus.Runtime.Stage
{
    /// <summary>
    /// SymphonyFramework の SceneLoader を利用して Stage シーン群を非同期 Additive ロード／切り替えし、
    /// 排他制御・Active Scene 優先度・ServiceLocator 連携を安全に管理するローダー。
    /// </summary>
    public class StageLoader : MonoBehaviour
    {
        /// <summary> 常駐ベースシーン（GameScene）の Active Scene 優先度。 </summary>
        public const int BASE_SCENE_PRIORITY = 0;

        /// <summary> メインステージシーンの Active Scene 優先度（ライティング反映のためベースより高くする）。 </summary>
        public const int STAGE_SCENE_PRIORITY = 10;

        /// <summary> 追加サブシーンの Active Scene 優先度。 </summary>
        public const int SUB_SCENE_PRIORITY = 5;

        // 起動時に自動で読み込むデフォルトのステージシーン名（Build Settings に登録必須）
        [SceneNameSelector]
        [SerializeField] private string _defaultStageSceneName = "Stage_Default";

        // 必要に応じてステージと同時に読み込む追加サブシーン群
        [SceneNameSelector]
        [SerializeField] private string[] _additionalSubSceneNames;

        private readonly List<string> _loadedSceneNames = new List<string>();
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);

        private string _ownerSceneName;
        private string _loadedSceneName;
        private StageAnchor _currentAnchor;
        private bool _isDuplicateInstance;

        /// <summary> 現在ロードされているステージのアンカー。未ロード時は null。 </summary>
        public StageAnchor CurrentAnchor => _currentAnchor;

        /// <summary> 現在ロードされているメインステージシーン名。 </summary>
        public string LoadedSceneName => _loadedSceneName;

        /// <summary> 現在このローダーが管理・ロードしている全シーン名一覧。 </summary>
        public IReadOnlyList<string> LoadedSceneNames => _loadedSceneNames;

        private void Awake()
        {
            _ownerSceneName = gameObject.scene.name;
        }

        private void OnEnable()
        {
            if (!ServiceLocator.RegisterInstance(this, LocateTypeEnum.Locator))
            {
                if (ServiceLocator.TryGetInstance<StageLoader>(out StageLoader existing) && existing != this)
                {
                    _isDuplicateInstance = true;
                    Debug.LogWarning($"[{nameof(StageLoader)}] 既に別の StageLoader が登録されているため登録をスキップします。", this);
                    return;
                }
            }

            _isDuplicateInstance = false;
            EnsureOwnerSceneTracked();
        }

        private void OnDisable()
        {
            if (!_isDuplicateInstance)
            {
                ServiceLocator.UnregisterInstance(this);
            }

            _isDuplicateInstance = false;
        }

        private void OnDestroy()
        {
            _operationLock.Dispose();
        }

        /// <summary> デフォルトステージおよび設定された追加サブシーンを非同期で加算ロードする。 </summary>
        public UniTask<StageAnchor> LoadDefaultStageAsync(CancellationToken cancellationToken = default)
        {
            return LoadStageWithSubScenesAsync(_defaultStageSceneName, _additionalSubSceneNames, cancellationToken);
        }

        /// <summary> 指定した単一 Stage シーンを安全にロード／切り替えし、シーン内の StageAnchor を返す。 </summary>
        public UniTask<StageAnchor> LoadStageAsync(string sceneName, CancellationToken cancellationToken = default)
        {
            return LoadStageWithSubScenesAsync(sceneName, null, cancellationToken);
        }

        /// <summary>
        /// メインステージと複数サブシーンをまとめて安全にロード／切り替えする。
        /// 同時実行をロックで直列化し、重複ロード・不整合・Active Scene 迷子を防ぐ。
        /// </summary>
        public async UniTask<StageAnchor> LoadStageWithSubScenesAsync(
            string primaryStageSceneName,
            IReadOnlyList<string> subSceneNames,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(primaryStageSceneName))
            {
                Debug.LogError($"[{nameof(StageLoader)}] ステージシーン名が空です。", this);
                return null;
            }

            List<string> targetSceneNames = BuildTargetSceneList(primaryStageSceneName, subSceneNames);

            await _operationLock.WaitAsync(cancellationToken);
            try
            {
                EnsureOwnerSceneTracked();

                // 既に同じ構成がすべてロード済みなら再ロードを省き、Active Scene と Anchor 参照だけ整える
                if (IsAlreadyLoadedConfiguration(primaryStageSceneName, targetSceneNames))
                {
                    SceneLoader.SetActiveScene(primaryStageSceneName);
                    _currentAnchor = ResolveStageAnchor(primaryStageSceneName);
                    return _currentAnchor;
                }

                List<string> scenesToUnload = CollectScenesToUnload(targetSceneNames);
                List<SceneLoadRequest> requestsToLoad = BuildLoadRequests(primaryStageSceneName, targetSceneNames);

                // Unity は最後の1シーンをアンロードできないため、残るシーンが複数ある場合のみ先にアンロードして旧ステージを片付ける
                bool canUnloadFirst = scenesToUnload.Count > 0 && (SceneManager.sceneCount - scenesToUnload.Count) >= 1;
                if (canUnloadFirst)
                {
                    await UnloadScenesInternalAsync(scenesToUnload, cancellationToken);
                    scenesToUnload.Clear();
                }

                if (requestsToLoad.Count > 0)
                {
                    bool loadSucceeded = await SceneLoader.LoadScenesAsync(requestsToLoad.ToArray(), token: cancellationToken);
                    if (!loadSucceeded)
                    {
                        Debug.LogError($"[{nameof(StageLoader)}] ステージシーン群のロードに失敗しました（主シーン: '{primaryStageSceneName}'）。Build Settings を確認してください。", this);
                        SyncLoadedListWithSceneLoader();
                        _currentAnchor = null;
                        return null;
                    }
                }

                if (scenesToUnload.Count > 0)
                {
                    await UnloadScenesInternalAsync(scenesToUnload, cancellationToken);
                }

                // 複数シーンロード後もメインステージを確実に Active Scene へ設定する
                SceneLoader.SetActiveScene(primaryStageSceneName);

                _loadedSceneNames.Clear();
                _loadedSceneNames.AddRange(targetSceneNames);
                _loadedSceneName = primaryStageSceneName;
                _currentAnchor = ResolveStageAnchor(primaryStageSceneName);

                if (_currentAnchor == null)
                {
                    Debug.LogError($"[{nameof(StageLoader)}] シーン '{primaryStageSceneName}' に StageAnchor が見つかりません。", this);
                }

                return _currentAnchor;
            }
            catch (OperationCanceledException)
            {
                SyncLoadedListWithSceneLoader();
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{nameof(StageLoader)}] ステージロード中にエラーが発生しました: {ex}", this);
                SyncLoadedListWithSceneLoader();
                _currentAnchor = null;
                return null;
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary> 現在ロード中の全 Stage シーン（メイン・サブ含む）を安全にアンロードする。 </summary>
        public async UniTask UnloadCurrentStageAsync(CancellationToken cancellationToken = default)
        {
            await _operationLock.WaitAsync(cancellationToken);
            try
            {
                if (_loadedSceneNames.Count == 0 && string.IsNullOrEmpty(_loadedSceneName))
                {
                    return;
                }

                List<string> targets = new List<string>(_loadedSceneNames);
                if (!string.IsNullOrEmpty(_loadedSceneName) && !targets.Contains(_loadedSceneName))
                {
                    targets.Add(_loadedSceneName);
                }

                _loadedSceneNames.Clear();
                _loadedSceneName = null;
                _currentAnchor = null;

                await UnloadScenesInternalAsync(targets, cancellationToken);
            }
            finally
            {
                _operationLock.Release();
            }
        }

        private void EnsureOwnerSceneTracked()
        {
            if (string.IsNullOrWhiteSpace(_ownerSceneName) || _ownerSceneName == "DontDestroyOnLoad")
            {
                return;
            }

            if (SceneLoader.GetExistScene(_ownerSceneName, out _))
            {
                SceneLoader.RegisterLoadedScene(_ownerSceneName, BASE_SCENE_PRIORITY);
            }
        }

        private List<string> BuildTargetSceneList(string primarySceneName, IReadOnlyList<string> subSceneNames)
        {
            List<string> list = new List<string> { primarySceneName };
            if (subSceneNames == null)
            {
                return list;
            }

            for (int i = 0; i < subSceneNames.Count; i++)
            {
                string subName = subSceneNames[i];
                if (string.IsNullOrWhiteSpace(subName))
                {
                    continue;
                }

                if (!list.Contains(subName))
                {
                    list.Add(subName);
                }
            }

            return list;
        }

        private bool IsAlreadyLoadedConfiguration(string primarySceneName, IReadOnlyList<string> targetSceneNames)
        {
            if (_loadedSceneName != primarySceneName || _loadedSceneNames.Count != targetSceneNames.Count)
            {
                return false;
            }

            for (int i = 0; i < targetSceneNames.Count; i++)
            {
                string sceneName = targetSceneNames[i];
                if (!_loadedSceneNames.Contains(sceneName) || !SceneLoader.GetExistScene(sceneName, out _))
                {
                    return false;
                }
            }

            return true;
        }

        private List<string> CollectScenesToUnload(IReadOnlyList<string> targetSceneNames)
        {
            List<string> unloadList = new List<string>();
            for (int i = 0; i < _loadedSceneNames.Count; i++)
            {
                string loaded = _loadedSceneNames[i];
                if (!IsInList(targetSceneNames, loaded) && SceneLoader.GetExistScene(loaded, out _))
                {
                    unloadList.Add(loaded);
                }
            }

            if (!string.IsNullOrEmpty(_loadedSceneName)
                && !IsInList(targetSceneNames, _loadedSceneName)
                && !unloadList.Contains(_loadedSceneName)
                && SceneLoader.GetExistScene(_loadedSceneName, out _))
            {
                unloadList.Add(_loadedSceneName);
            }

            return unloadList;
        }

        private List<SceneLoadRequest> BuildLoadRequests(string primarySceneName, IReadOnlyList<string> targetSceneNames)
        {
            List<SceneLoadRequest> requests = new List<SceneLoadRequest>(targetSceneNames.Count);
            for (int i = 0; i < targetSceneNames.Count; i++)
            {
                string sceneName = targetSceneNames[i];
                int priority = sceneName == primarySceneName ? STAGE_SCENE_PRIORITY : SUB_SCENE_PRIORITY;
                requests.Add(new SceneLoadRequest(sceneName, priority));
            }

            return requests;
        }

        private async UniTask UnloadScenesInternalAsync(IReadOnlyList<string> sceneNames, CancellationToken cancellationToken)
        {
            List<string> existing = new List<string>(sceneNames.Count);
            for (int i = 0; i < sceneNames.Count; i++)
            {
                string name = sceneNames[i];
                if (!string.IsNullOrWhiteSpace(name) && SceneLoader.GetExistScene(name, out _))
                {
                    existing.Add(name);
                }
            }

            if (existing.Count == 0)
            {
                return;
            }

            // 破棄時は他シーンを道連れにせず最低1シーンを残す
            if (SceneManager.sceneCount - existing.Count < 1)
            {
                Debug.LogWarning($"[{nameof(StageLoader)}] 最後の1シーンになるためアンロードをスキップします。", this);
                return;
            }

            bool unloaded = await SceneLoader.UnloadScenesAsync(existing.ToArray(), token: cancellationToken);
            if (!unloaded)
            {
                Debug.LogWarning($"[{nameof(StageLoader)}] 一部のステージシーンのアンロードに失敗しました。", this);
            }
        }

        private void SyncLoadedListWithSceneLoader()
        {
            for (int i = _loadedSceneNames.Count - 1; i >= 0; i--)
            {
                if (!SceneLoader.GetExistScene(_loadedSceneNames[i], out _))
                {
                    _loadedSceneNames.RemoveAt(i);
                }
            }

            if (!string.IsNullOrEmpty(_loadedSceneName) && !SceneLoader.GetExistScene(_loadedSceneName, out _))
            {
                _loadedSceneName = null;
            }
        }

        private StageAnchor ResolveStageAnchor(string primarySceneName)
        {
            if (ServiceLocator.TryGetInstance<StageAnchor>(out StageAnchor locatedAnchor) && locatedAnchor != null)
            {
                return locatedAnchor;
            }

            if (SceneLoader.GetExistScene(primarySceneName, out Scene scene))
            {
                return FindStageAnchor(scene);
            }

            return null;
        }

        private static bool IsInList(IReadOnlyList<string> list, string target)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == target)
                {
                    return true;
                }
            }

            return false;
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
