using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Taurus.Editor.Setup
{
    /// <summary> フォントメモ準拠のフォント適用、最新UIレイアウト反映、および開発チームロゴ登録を行うエディタツール。 </summary>
    public static class ApplyUiFontsAndLogoTool
    {
        private const string ZEN_MARU_PATH = "Assets/AssetStoreTools/Fonts/ZenMaruGothic/ZenMaruGothic-Bold.ttf";
        private const string FIRJAR_PATH = "Assets/AssetStoreTools/Fonts/Firjar/static/Firjar_SemiCondensed-SemiBold.ttf";
        private const string TEAM_LOGO_PATH = "Assets/Level/UI/Logo_TeamTaurus.png";
        private const string TITLE_CHARA_PATH = "Assets/AssetStoreTools/Characters/Chara_Girl_Title.png";
        private const string GAME_SCENE_PATH = "Assets/Scenes/GameScene.unity";

        // 英字・大きく表示する数字の Text オブジェクト名（フォントメモ.txt 準拠で Firjar を割り当てる）
        private static readonly HashSet<string> FIRJAR_TEXT_NAMES = new HashSet<string>
        {
            "WaveText",
            "TimerText",
            "CounterText",
            "CountLabel",
            "CounterLabel",
            "ReplayLabel",
            "ReplayCountText",
            "ClearedWavesText",
            "ClearedCaption",
            "RecordsTitle",
            "RecordRank_1",
            "RecordRank_2",
            "RecordRank_3",
            "RecordValue_1",
            "RecordValue_2",
            "RecordValue_3",
            "ResultEyebrow",
            "ResultTitle",
            "ResultDetailText",
            "Heading",
            "Eyebrow",
            "StartCountText",
            "DebugText",
        };

        [MenuItem("Taurus/Apply Latest UI, Fonts and Team Logo")]
        public static void ApplyAll()
        {
            EnsureSpriteImport(TEAM_LOGO_PATH);
            EnsureSpriteImport(TITLE_CHARA_PATH);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Sprite teamLogo = AssetDatabase.LoadAssetAtPath<Sprite>(TEAM_LOGO_PATH);
            Sprite titleChara = AssetDatabase.LoadAssetAtPath<Sprite>(TITLE_CHARA_PATH);
            Font zenMaru = AssetDatabase.LoadAssetAtPath<Font>(ZEN_MARU_PATH);
            Font firjar = AssetDatabase.LoadAssetAtPath<Font>(FIRJAR_PATH);

            if (teamLogo == null || zenMaru == null || firjar == null)
            {
                Debug.LogError($"[{nameof(ApplyUiFontsAndLogoTool)}] 必要なアセットが読み込めません。logo={teamLogo != null}, zenMaru={zenMaru != null}, firjar={firjar != null}");
                return;
            }

            // 1. Unity PlayerSettings に開発チーム名と Splash Screen ロゴを登録
            PlayerSettings.companyName = "Team Taurus";
            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.showUnityLogo = true;
            PlayerSettings.SplashScreen.logos = new[]
            {
                PlayerSettings.SplashScreenLogo.Create(2.0f, teamLogo),
            };

            // 2. GameScene の UI を更新
            Scene scene = EditorSceneManager.GetSceneByPath(GAME_SCENE_PATH);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(GAME_SCENE_PATH, OpenSceneMode.Single);
            }

            int zenCount = 0;
            int firjarCount = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Text[] texts = root.GetComponentsInChildren<Text>(true);
                foreach (Text text in texts)
                {
                    bool useFirjar = ShouldUseFirjar(text);
                    text.font = useFirjar ? firjar : zenMaru;
                    EditorUtility.SetDirty(text);
                    if (useFirjar)
                    {
                        firjarCount++;
                    }
                    else
                    {
                        zenCount++;
                    }
                }
            }

            Transform canvas = FindChildByName(scene, "Canvas");
            if (canvas != null)
            {
                Transform titlePanel = canvas.Find("TitlePanel");
                if (titlePanel != null)
                {
                    // タイトル画面2Dキャラを背面に配置（1920x1080 透過PNG）
                    if (titleChara != null)
                    {
                        Image charaImg = EnsureImage(titlePanel, "TitleCharacterImage");
                        charaImg.sprite = titleChara;
                        charaImg.preserveAspect = true;
                        charaImg.raycastTarget = false;
                        RectTransform rt = charaImg.rectTransform;
                        rt.anchorMin = Vector2.zero;
                        rt.anchorMax = Vector2.one;
                        rt.offsetMin = Vector2.zero;
                        rt.offsetMax = Vector2.zero;
                        rt.SetSiblingIndex(0);
                    }

                    // タイトル画面右上に開発チームロゴを配置
                    Image titleLogo = EnsureImage(titlePanel, "TeamLogoImage");
                    titleLogo.sprite = teamLogo;
                    titleLogo.preserveAspect = true;
                    titleLogo.raycastTarget = false;
                    RectTransform logoRt = titleLogo.rectTransform;
                    logoRt.anchorMin = new Vector2(0.5f, 0.5f);
                    logoRt.anchorMax = new Vector2(0.5f, 0.5f);
                    logoRt.pivot = new Vector2(0.5f, 0.5f);
                    logoRt.anchoredPosition = new Vector2(845f, 435f);
                    logoRt.sizeDelta = new Vector2(140f, 140f);
                }

                // クレジット画面のカード上部（Heading の左）に開発チームロゴを配置
                Transform creditsCard = canvas.Find("CreditsPanel/Card");
                if (creditsCard != null)
                {
                    Image creditsLogo = EnsureImage(creditsCard, "TeamLogoImage");
                    creditsLogo.sprite = teamLogo;
                    creditsLogo.preserveAspect = true;
                    creditsLogo.raycastTarget = false;
                    RectTransform logoRt = creditsLogo.rectTransform;
                    logoRt.anchorMin = new Vector2(0.5f, 0.5f);
                    logoRt.anchorMax = new Vector2(0.5f, 0.5f);
                    logoRt.pivot = new Vector2(0.5f, 0.5f);
                    logoRt.anchoredPosition = new Vector2(-270f, 320f);
                    logoRt.sizeDelta = new Vector2(104f, 104f);
                }

                // リザルト画面：UI.ai 最新案に合わせて TOP3 RECORDS を表彰台配置（1st 中央上・2nd 左・3rd 右）へ更新
                Transform resultPanel = canvas.Find("ResultPanel");
                if (resultPanel != null)
                {
                    SetAnchoredPos(resultPanel.Find("RecordRank_1"), new Vector2(610f, -256f));
                    SetAnchoredPos(resultPanel.Find("RecordValue_1"), new Vector2(610f, -298f));
                    SetAnchoredPos(resultPanel.Find("RecordRank_2"), new Vector2(430f, -286f));
                    SetAnchoredPos(resultPanel.Find("RecordValue_2"), new Vector2(430f, -328f));
                    SetAnchoredPos(resultPanel.Find("RecordRank_3"), new Vector2(790f, -286f));
                    SetAnchoredPos(resultPanel.Find("RecordValue_3"), new Vector2(790f, -328f));
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log($"[{nameof(ApplyUiFontsAndLogoTool)}] 適用完了: ZenMaruGothic={zenCount}件, Firjar={firjarCount}件, SplashLogo設定済, Title/Creditsロゴ配置済");
        }

        private static bool ShouldUseFirjar(Text text)
        {
            if (FIRJAR_TEXT_NAMES.Contains(text.gameObject.name))
            {
                return true;
            }

            // PlusButton / MinusButton 内の "SPACE +1" / "CTRL -1" ラベルは英字・数字のみ
            Transform parent = text.transform.parent;
            if (parent != null && (parent.name == "PlusButton" || parent.name == "MinusButton"))
            {
                return true;
            }

            return false;
        }

        private static void EnsureSpriteImport(string assetPath)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            bool changed = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                changed = true;
            }

            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                changed = true;
            }

            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
            }
        }

        private static Image EnsureImage(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (existing == null)
            {
                go.transform.SetParent(parent, false);
            }

            Image image = go.GetComponent<Image>();
            if (image == null)
            {
                image = go.AddComponent<Image>();
            }

            return image;
        }

        private static void SetAnchoredPos(Transform target, Vector2 pos)
        {
            if (target is RectTransform rt)
            {
                rt.anchoredPosition = pos;
                EditorUtility.SetDirty(rt);
            }
        }

        private static Transform FindChildByName(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root.transform;
                }
            }

            return null;
        }
    }
}
