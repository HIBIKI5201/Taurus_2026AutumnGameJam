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
            "Assets/AssetStoreTools/Audio/SE/SE_Title_SpaceGauge.mp3",
            "Assets/AssetStoreTools/Audio/SE/SE_UI_Click.mp3",
            "Assets/AssetStoreTools/Audio/SE/SE_Count_Up.mp3",
            "Assets/AssetStoreTools/Audio/SE/SE_Count_Down.mp3",
            "Assets/AssetStoreTools/Audio/SE/SE_Count_Floor.mp3",
            "Assets/AssetStoreTools/Audio/SE/SE_TopPass_Parent.mp3",
            "Assets/AssetStoreTools/Audio/SE/SE_TopPass_Child.mp3",
            "Assets/AssetStoreTools/Audio/SE/SE_Wave_Start.mp3",
            "Assets/AssetStoreTools/Audio/SE/SE_Wave_Success.mp3",
            "Assets/AssetStoreTools/Audio/SE/SE_Game_Over.mp3",
            "Assets/AssetStoreTools/Audio/SE/SE_Record_High.mp3",
            "Assets/AssetStoreTools/Audio/BGM/BGM_Title.mp3",
            "Assets/AssetStoreTools/Audio/BGM/BGM_Wave_Main.mp3",
            "Assets/AssetStoreTools/Audio/BGM/BGM_Result.mp3",
            "Assets/AssetStoreTools/Audio/BGM/BGM_Result_Alt.mp3",
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
                    "_waveStartClip",
                    "_countFloorClip",
                    "_recordHighClip",
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

        [TestCase(0, 0, true, TestName = "0で減算しても0のまま＝下限キャンセル")]
        [TestCase(1, 0, false, TestName = "1から0への減算＝通常の減算")]
        [TestCase(3, 2, false, TestName = "3から2への減算＝通常の減算")]
        public void IsCountFloorHit_DetectsOnlyDecrementAtZero(int previous, int current, bool expected)
        {
            Assert.AreEqual(expected, GameAudioHooks.IsCountFloorHit(previous, current));
        }

        [TestCase(5, new[] { 7, 5, 2 }, true, TestName = "トップ3に入った記録＝新記録")]
        [TestCase(1, new[] { 7, 5, 2 }, false, TestName = "トップ3に入らない記録＝新記録ではない")]
        [TestCase(0, new[] { 0, 0, 0 }, false, TestName = "0Waveは新記録扱いしない")]
        public void IsTopRecord_TrueOnlyWhenClearedWavesIsInRecords(int clearedWaves, int[] records, bool expected)
        {
            Assert.AreEqual(expected, GameAudioHooks.IsTopRecord(clearedWaves, records));
        }
    }
}
