using NUnit.Framework;
using Taurus.Runtime.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Taurus.Editor.Tests
{
    /// <summary>
    /// AudioClip アセット存在確認および GameScene の GameAudioHooks 配線確認テスト。
    /// </summary>
    public class GameAudioHooksTests
    {
        private static readonly string[] REQUIRED_CLIP_PATHS =
        {
            "Assets/AssetStoreTools/Audio/SE/SE_TitlePress.wav",
            "Assets/AssetStoreTools/Audio/SE/SE_GameStart.wav",
            "Assets/AssetStoreTools/Audio/SE/SE_CountUp.wav",
            "Assets/AssetStoreTools/Audio/SE/SE_CountDown.wav",
            "Assets/AssetStoreTools/Audio/SE/SE_ParentTop.wav",
            "Assets/AssetStoreTools/Audio/SE/SE_ChildTop.wav",
            "Assets/AssetStoreTools/Audio/SE/SE_WaveSuccess.wav",
            "Assets/AssetStoreTools/Audio/SE/SE_GameOver.wav",
            "Assets/AssetStoreTools/Audio/BGM/BGM_Title.wav",
            "Assets/AssetStoreTools/Audio/BGM/BGM_Game.wav",
            "Assets/AssetStoreTools/Audio/BGM/BGM_Result.wav",
        };

        [Test]
        public void AudioClips_AllSpecAssetsExistAndLoad()
        {
            foreach (string path in REQUIRED_CLIP_PATHS)
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                Assert.IsNotNull(clip, $"AudioClip が読み込めません: {path}");
                Assert.Greater(clip.length, 0f, $"AudioClip の長さが 0 以下です: {path}");
            }
        }

        [Test]
        public void GameScene_GameAudioHooks_HasAllClipsAndSourcesConfigured()
        {
            Scene gameScene = EditorSceneManager.GetSceneByPath("Assets/Scenes/GameScene.unity");
            bool loadedTemporarily = false;
            if (!gameScene.isLoaded)
            {
                gameScene = EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity", OpenSceneMode.Additive);
                loadedTemporarily = true;
            }

            try
            {
                GameAudioHooks hooks = null;
                AudioListener listener = null;
                foreach (GameObject root in gameScene.GetRootGameObjects())
                {
                    if (hooks == null)
                    {
                        hooks = root.GetComponentInChildren<GameAudioHooks>(true);
                    }

                    if (listener == null)
                    {
                        listener = root.GetComponentInChildren<AudioListener>(true);
                    }
                }

                Assert.IsNotNull(listener, "GameScene に AudioListener が見つかりません。");
                Assert.IsNotNull(hooks, "GameScene に GameAudioHooks が見つかりません。");

                SerializedObject so = new SerializedObject(hooks);
                Assert.IsNotNull(so.FindProperty("_gameController").objectReferenceValue, "_gameController が未設定です。");

                AudioSource seSource = so.FindProperty("_seSource").objectReferenceValue as AudioSource;
                AudioSource bgmSource = so.FindProperty("_bgmSource").objectReferenceValue as AudioSource;
                Assert.IsNotNull(seSource, "_seSource が未設定です。");
                Assert.IsNotNull(bgmSource, "_bgmSource が未設定です。");
                Assert.IsFalse(seSource.loop, "_seSource.loop は false である必要があります。");
                Assert.IsTrue(bgmSource.loop, "_bgmSource.loop は true である必要があります。");

                string[] clipFields =
                {
                    "_titlePressClip",
                    "_gameStartClip",
                    "_countUpClip",
                    "_countDownClip",
                    "_parentTopClip",
                    "_childTopClip",
                    "_waveSuccessClip",
                    "_gameOverClip",
                    "_titleBgmClip",
                    "_gameBgmClip",
                    "_resultBgmClip",
                };

                foreach (string field in clipFields)
                {
                    SerializedProperty prop = so.FindProperty(field);
                    Assert.IsNotNull(prop, $"SerializedProperty が見つかりません: {field}");
                    Assert.IsNotNull(prop.objectReferenceValue, $"GameAudioHooks.{field} が未設定です。");
                }
            }
            finally
            {
                if (loadedTemporarily)
                {
                    EditorSceneManager.CloseScene(gameScene, true);
                }
            }
        }
    }
}
