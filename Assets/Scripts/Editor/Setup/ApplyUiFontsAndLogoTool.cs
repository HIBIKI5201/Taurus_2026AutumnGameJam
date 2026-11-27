using System.Collections.Generic;
using Taurus.Runtime.UI;
using TMPro;
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
        private const string SUCCESS_BANNER_PATH = "Assets/Level/UI/UI_SuccessBanner.png";
        private const string TITLE_CHARA_PATH = "Assets/AssetStoreTools/Characters/Chara_Girl_Title.png";
        private const string GAME_SCENE_PATH = "Assets/Scenes/GameScene.unity";

        // トンマナ確定カラー（#7A544E ベース茶）
        private static readonly Color TONMANA_BROWN = new Color(0.478f, 0.329f, 0.306f, 1f);

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

        [MenuItem("Taurus/Apply Success Banner Tonmana")]
        public static void ApplySuccessBannerTonmana()
        {
            EnsureSpriteImport(SUCCESS_BANNER_PATH);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Sprite bannerSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SUCCESS_BANNER_PATH);
            Font zenMaru = AssetDatabase.LoadAssetAtPath<Font>(ZEN_MARU_PATH);
            if (bannerSprite == null || zenMaru == null)
            {
                Debug.LogError($"[{nameof(ApplyUiFontsAndLogoTool)}] バナー素材またはフォントが見つかりません。");
                return;
            }

            Scene scene = EditorSceneManager.GetSceneByPath(GAME_SCENE_PATH);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(GAME_SCENE_PATH, OpenSceneMode.Single);
            }

            Transform canvas = FindChildByName(scene, "Canvas");
            Transform banner = canvas != null ? canvas.Find("GamePanel/SuccessBanner") : null;
            if (banner == null)
            {
                Debug.LogError($"[{nameof(ApplyUiFontsAndLogoTool)}] Canvas/GamePanel/SuccessBanner が見つかりません。");
                return;
            }

            Image bannerImage = banner.GetComponent<Image>();
            if (bannerImage != null)
            {
                bannerImage.sprite = bannerSprite;
                bannerImage.color = Color.white;
                bannerImage.type = Image.Type.Simple;
                bannerImage.preserveAspect = true;
                bannerImage.raycastTarget = false;
                EditorUtility.SetDirty(bannerImage);
            }

            if (banner is RectTransform bannerRt)
            {
                bannerRt.anchoredPosition = new Vector2(0f, 240f);
                bannerRt.sizeDelta = new Vector2(720f, 116f);
                EditorUtility.SetDirty(bannerRt);
            }

            Text label = banner.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.font = zenMaru;
                label.color = TONMANA_BROWN;
                label.fontSize = 38;
                label.fontStyle = FontStyle.Bold;
                label.alignment = TextAnchor.MiddleCenter;
                label.rectTransform.anchoredPosition = Vector2.zero;
                label.rectTransform.sizeDelta = new Vector2(560f, 76f);
                EditorUtility.SetDirty(label);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(ApplyUiFontsAndLogoTool)}] SuccessBanner のトンマナ適用完了");
        }

        [MenuItem("Taurus/Apply Fullscreen and Canvas Expand")]
        public static void ApplyFullscreenAndCanvasExpand()
        {
            // 1. PlayerSettings：ボーダレスフルスクリーン（ネイティブ解像度）へ変更
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;

            // 2. GameScene の CanvasScaler を Expand（16:9 / 16:10 等で1920x1080が必ず画面内に収まる）へ変更
            bool wasGameSceneLoaded = EditorSceneManager.GetSceneByPath(GAME_SCENE_PATH).isLoaded;
            Scene scene = wasGameSceneLoaded
                ? EditorSceneManager.GetSceneByPath(GAME_SCENE_PATH)
                : EditorSceneManager.OpenScene(GAME_SCENE_PATH, OpenSceneMode.Additive);

            Transform canvas = FindChildByName(scene, "Canvas");
            if (canvas != null)
            {
                CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920f, 1080f);
                    scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                    EditorUtility.SetDirty(scaler);
                }

                // Expand 時に 16:10 等で縦に余白ができてもタイトルキャラの下端が浮かないよう下端基準のフィットに調整
                Transform titleChara = canvas.Find("TitlePanel/TitleCharacterImage");
                if (titleChara != null && titleChara is RectTransform charaRt)
                {
                    charaRt.anchorMin = new Vector2(0.5f, 0f);
                    charaRt.anchorMax = new Vector2(0.5f, 0f);
                    charaRt.pivot = new Vector2(0.5f, 0f);
                    charaRt.anchoredPosition = Vector2.zero;
                    charaRt.sizeDelta = new Vector2(1920f, 1080f);
                    EditorUtility.SetDirty(charaRt);
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!wasGameSceneLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(ApplyUiFontsAndLogoTool)}] フルスクリーン(FullScreenWindow) & CanvasScaler(Expand 1920x1080) 適用完了");
        }

        [MenuItem("Taurus/Apply Final UI Deliverables")]
        public static void ApplyFinalUiDeliverables()
        {
            const string UI_ROOT = "Assets/AssetStoreTools/UI/";
            string[] newSprites =
            {
                UI_ROOT + "UI_Btn_GameStart/gamestart_script.png",
                UI_ROOT + "UI_Btn_ResultActions/ResultPanelTab.png",
                UI_ROOT + "UI_Btn_ResultActions/RetryIcon.png",
                UI_ROOT + "UI_Btn_ResultActions/ReturnToTitleIcon.png",
                UI_ROOT + "UI_Btn_ResultActions/SpeechBubble.png",
            };

            foreach (string path in newSprites)
            {
                EnsureSpriteImport(path);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Sprite ruleCardSprite = LoadMainSprite(UI_ROOT + "UI_Rules_DemoIllust/rule.png");
            Sprite wavesClearedSprite = LoadMainSprite(UI_ROOT + "UI_Btn_ResultActions/wavesclered.png");
            Sprite top3RecordsSprite = LoadMainSprite(UI_ROOT + "UI_Btn_ResultActions/top3records.png");
            Sprite retryIconSprite = LoadMainSprite(UI_ROOT + "UI_Btn_ResultActions/RetryIcon.png");
            Sprite returnIconSprite = LoadMainSprite(UI_ROOT + "UI_Btn_ResultActions/ReturnToTitleIcon.png");
            Sprite resultTabSprite = LoadMainSprite(UI_ROOT + "UI_Btn_ResultActions/ResultPanelTab.png");
            Sprite speechBubbleSprite = LoadMainSprite(UI_ROOT + "UI_Btn_ResultActions/SpeechBubble.png");
            Sprite gameStartScriptSprite = LoadMainSprite(UI_ROOT + "UI_Btn_GameStart/gamestart_script.png");

            // 1. UISkin_WIP.asset の仮画像をすべて本番素材へ差し替え
            UISkin skin = AssetDatabase.LoadAssetAtPath<UISkin>("Assets/Level/UI/UISkin_WIP.asset");
            if (skin != null)
            {
                SerializedObject so = new SerializedObject(skin);
                SerializedProperty entries = so.FindProperty("_entries");
                SetSkinSlot(entries, (int)UISkinSlot.RulesCard, ruleCardSprite);
                SetSkinSlot(entries, (int)UISkinSlot.ResultClearedFrame, wavesClearedSprite);
                SetSkinSlot(entries, (int)UISkinSlot.ResultRecordsFrame, top3RecordsSprite);
                SetSkinSlot(entries, (int)UISkinSlot.RetryIcon, retryIconSprite);
                SetSkinSlot(entries, (int)UISkinSlot.ReturnToTitleIcon, returnIconSprite);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(skin);
            }

            // 2. GameScene のレイアウトを完成見本(UI1/UI2/UI3)へ合わせて更新
            bool wasLoaded = EditorSceneManager.GetSceneByPath(GAME_SCENE_PATH).isLoaded;
            Scene scene = wasLoaded
                ? EditorSceneManager.GetSceneByPath(GAME_SCENE_PATH)
                : EditorSceneManager.OpenScene(GAME_SCENE_PATH, OpenSceneMode.Additive);

            Transform canvas = FindChildByName(scene, "Canvas");
            if (canvas != null)
            {
                // タイトル画面（UI1.png 準拠）
                Transform titlePanel = canvas.Find("TitlePanel");
                if (titlePanel != null)
                {
                    SetRect(titlePanel.Find("TitleLogoImage"), new Vector2(-410f, 145f), new Vector2(1060f, 505f));
                    Transform startBtn = titlePanel.Find("TitleStartButton");
                    SetRect(startBtn, new Vector2(0f, -395f), new Vector2(620f, 112f));
                    if (startBtn != null && gameStartScriptSprite != null)
                    {
                        Image scriptOverlay = EnsureImage(startBtn, "GameStartScriptOverlay");
                        scriptOverlay.sprite = gameStartScriptSprite;
                        scriptOverlay.preserveAspect = true;
                        scriptOverlay.raycastTarget = false;
                        SetRect(scriptOverlay.transform, new Vector2(155f, -42f), new Vector2(270f, 100f));
                    }
                }

                // リザルト画面（UI2.png 準拠 + リトライUI崩れ解消）
                Transform resultPanel = canvas.Find("ResultPanel");
                if (resultPanel != null)
                {
                    // 右側リザルトタブ背景
                    Transform back = resultPanel.Find("Back");
                    if (back != null)
                    {
                        if (back is RectTransform backRt)
                        {
                            backRt.anchorMin = new Vector2(0.5f, 0.5f);
                            backRt.anchorMax = new Vector2(0.5f, 0.5f);
                            backRt.pivot = new Vector2(0.5f, 0.5f);
                            backRt.anchoredPosition = new Vector2(730f, -10f);
                            backRt.sizeDelta = new Vector2(460f, 870f);
                            EditorUtility.SetDirty(backRt);
                        }

                        Image backImg = back.GetComponent<Image>();
                        if (backImg != null && resultTabSprite != null)
                        {
                            backImg.sprite = resultTabSprite;
                            backImg.color = Color.white;
                            backImg.preserveAspect = true;
                            EditorUtility.SetDirty(backImg);
                        }
                    }

                    // 上中央: - GAME OVER - と クリア数カプセル
                    SetRect(resultPanel.Find("GameOverImage"), new Vector2(-130f, 415f), new Vector2(720f, 180f));
                    SetRect(resultPanel.Find("ClearedFrame"), new Vector2(-130f, 245f), new Vector2(280f, 174f));
                    SetRect(resultPanel.Find("ClearedWavesText"), new Vector2(-130f, 272f), new Vector2(240f, 100f));
                    SetTextSize(resultPanel.Find("ClearedWavesText"), 86);
                    SetActiveSafe(resultPanel.Find("ClearedCaption"), false);

                    // リプレイ答えカウンターを左上へ独立配置（中央振り子と被らない）
                    SetRect(resultPanel.Find("ReplayCounter"), new Vector2(-690f, 385f), new Vector2(280f, 210f));

                    // 左下吹き出し + ResultDetailText
                    if (speechBubbleSprite != null)
                    {
                        Image bubble = EnsureImage(resultPanel, "SpeechBubble");
                        bubble.sprite = speechBubbleSprite;
                        bubble.preserveAspect = true;
                        bubble.raycastTarget = false;
                        SetRect(bubble.transform, new Vector2(-210f, -400f), new Vector2(440f, 92f));
                        Transform detail = resultPanel.Find("ResultDetailText");
                        if (detail != null)
                        {
                            bubble.transform.SetSiblingIndex(detail.GetSiblingIndex());
                            SetRect(detail, new Vector2(-195f, -400f), new Vector2(380f, 72f));
                            Text detailText = detail.GetComponent<Text>();
                            if (detailText != null)
                            {
                                detailText.fontSize = 24;
                                detailText.color = new Color(0.988f, 0.949f, 0.902f, 1f);
                                EditorUtility.SetDirty(detailText);
                            }
                        }
                    }

                    // 右タブ上段: TOP3 RECORDS（画像に1st/2nd/3rd文字入りなのでテキスト重複を非表示）
                    SetRect(resultPanel.Find("RecordsFrame"), new Vector2(740f, 185f), new Vector2(340f, 205f));
                    SetActiveSafe(resultPanel.Find("RecordsTitle"), false);
                    SetActiveSafe(resultPanel.Find("RecordRank_1"), false);
                    SetActiveSafe(resultPanel.Find("RecordRank_2"), false);
                    SetActiveSafe(resultPanel.Find("RecordRank_3"), false);
                    SetRect(resultPanel.Find("RecordValue_1"), new Vector2(740f, 132f), new Vector2(110f, 56f));
                    SetRect(resultPanel.Find("RecordValue_2"), new Vector2(638f, 76f), new Vector2(110f, 56f));
                    SetRect(resultPanel.Find("RecordValue_3"), new Vector2(842f, 76f), new Vector2(110f, 56f));
                    SetTextSize(resultPanel.Find("RecordValue_1"), 46);
                    SetTextSize(resultPanel.Find("RecordValue_2"), 44);
                    SetTextSize(resultPanel.Find("RecordValue_3"), 44);

                    // 右タブ下段: リトライ・タイトルへボタン（KeyIconを下段独立配置で重なり解消）
                    LayoutResultActionButton(resultPanel.Find("RetryButton"), new Vector2(665f, -210f));
                    LayoutResultActionButton(resultPanel.Find("HomeButton"), new Vector2(815f, -210f));
                }

                // UISkinApplier を即時反映してシーン上の Image 参照も最新に同期
                UISkinApplier applier = canvas.GetComponent<UISkinApplier>();
                if (applier != null)
                {
                    applier.ApplyAll();
                    EditorUtility.SetDirty(applier);
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!wasLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(ApplyUiFontsAndLogoTool)}] 完成UIデータ一括反映完了（UISkin_WIP & GameScene）");
        }

        [MenuItem("Taurus/Fix Result Retry UI")]
        public static void FixResultRetryUi()
        {
            bool wasLoaded = EditorSceneManager.GetSceneByPath(GAME_SCENE_PATH).isLoaded;
            Scene scene = wasLoaded
                ? EditorSceneManager.GetSceneByPath(GAME_SCENE_PATH)
                : EditorSceneManager.OpenScene(GAME_SCENE_PATH, OpenSceneMode.Additive);

            Transform canvas = FindChildByName(scene, "Canvas");
            Transform resultPanel = canvas != null ? canvas.Find("ResultPanel") : null;
            if (resultPanel == null)
            {
                Debug.LogError($"[{nameof(ApplyUiFontsAndLogoTool)}] ResultPanel が見つかりません。");
                return;
            }

            // 1. 右タブ下段の楕円枠（unity_y = -111 〜 -264, 中心 -188）へ RetryButton / HomeButton を精密配置
            FixResultActionButton(resultPanel.Find("RetryButton"), new Vector2(660f, -188f));
            FixResultActionButton(resultPanel.Find("HomeButton"), new Vector2(830f, -188f));

            // 2. 右タブ上段（TOP3 RECORDS 帯 + 枠 unity_y = +260 〜 +53）へ表彰台スコアを精密配置
            SetRect(resultPanel.Find("RecordsFrame"), new Vector2(745f, 250f), new Vector2(340f, 204f));
            SetRect(resultPanel.Find("RecordValue_1"), new Vector2(745f, 180f), new Vector2(110f, 56f));
            SetRect(resultPanel.Find("RecordValue_2"), new Vector2(648f, 120f), new Vector2(110f, 56f));
            SetRect(resultPanel.Find("RecordValue_3"), new Vector2(842f, 120f), new Vector2(110f, 56f));
            SetTextSize(resultPanel.Find("RecordValue_1"), 48);
            SetTextSize(resultPanel.Find("RecordValue_2"), 46);
            SetTextSize(resultPanel.Find("RecordValue_3"), 46);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!wasLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[{nameof(ApplyUiFontsAndLogoTool)}] ResultPanel のリトライ/タイトルボタンおよびTOP3配置を精密修正完了");
        }

        private static void FixResultActionButton(Transform btn, Vector2 pos)
        {
            if (btn == null)
            {
                return;
            }

            SetRect(btn, pos, new Vector2(150f, 148f));
            SetRect(btn.Find("Icon"), new Vector2(0f, 26f), new Vector2(66f, 66f));
            SetRect(btn.Find("Label"), new Vector2(0f, -24f), new Vector2(145f, 30f));
            SetTextSize(btn.Find("Label"), 20);

            Transform keyIcon = btn.Find("KeyIcon");
            if (keyIcon != null)
            {
                SetRect(keyIcon, new Vector2(0f, -54f), new Vector2(120f, 32f));
                TMP_Text tmp = keyIcon.GetComponent<TMP_Text>();
                if (tmp != null)
                {
                    tmp.alignment = TextAlignmentOptions.Center;
                    tmp.fontSize = 24f;
                    EditorUtility.SetDirty(tmp);
                }
            }
        }

        private static Sprite LoadMainSprite(string assetPath)
        {
            Object[] all = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            foreach (Object obj in all)
            {
                if (obj is Sprite sp)
                {
                    return sp;
                }
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static void SetSkinSlot(SerializedProperty entries, int slotValue, Sprite sprite)
        {
            if (entries == null || sprite == null)
            {
                return;
            }

            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty elem = entries.GetArrayElementAtIndex(i);
                if (elem.FindPropertyRelative("_slot").intValue == slotValue)
                {
                    elem.FindPropertyRelative("_sprite").objectReferenceValue = sprite;
                    return;
                }
            }

            int idx = entries.arraySize;
            entries.InsertArrayElementAtIndex(idx);
            SerializedProperty added = entries.GetArrayElementAtIndex(idx);
            added.FindPropertyRelative("_slot").intValue = slotValue;
            added.FindPropertyRelative("_sprite").objectReferenceValue = sprite;
        }

        private static void LayoutResultActionButton(Transform btn, Vector2 pos)
        {
            if (btn == null)
            {
                return;
            }

            SetRect(btn, pos, new Vector2(145f, 165f));
            SetRect(btn.Find("Icon"), new Vector2(0f, 30f), new Vector2(76f, 76f));
            SetRect(btn.Find("Label"), new Vector2(0f, -30f), new Vector2(145f, 34f));
            SetTextSize(btn.Find("Label"), 22);
            SetRect(btn.Find("KeyIcon"), new Vector2(0f, -66f), new Vector2(120f, 40f));
        }

        private static void SetRect(Transform target, Vector2 pos, Vector2 size)
        {
            if (target is RectTransform rt)
            {
                rt.anchoredPosition = pos;
                rt.sizeDelta = size;
                EditorUtility.SetDirty(rt);
            }
        }

        private static void SetTextSize(Transform target, int fontSize)
        {
            if (target == null)
            {
                return;
            }

            Text txt = target.GetComponent<Text>();
            if (txt != null)
            {
                txt.fontSize = fontSize;
                EditorUtility.SetDirty(txt);
            }
        }

        private static void SetActiveSafe(Transform target, bool active)
        {
            if (target != null)
            {
                target.gameObject.SetActive(active);
                EditorUtility.SetDirty(target.gameObject);
            }
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
