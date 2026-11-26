using System.Collections.Generic;
using System.IO;
using Taurus.Develop.Debug;
using Taurus.Runtime.Game;
using Taurus.Runtime.Pendulum;
using Taurus.Runtime.Stage;
using Taurus.Runtime.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Taurus.Editor.Setup
{
    /// <summary>
    /// 初期プロトタイプ構築用に GameScene・Stage_Default・仮振り子Prefab・StageDefinition・Build Settings を一括生成するエディタツール。
    ///
    /// 【引き継ぎ時の注意】
    /// - メニュー「Taurus/Setup Project Scenes &amp; Prefabs」を実行すると、GameScene.unity / Stage_Default.unity / 仮Prefab が上書きされる。
    /// - デザイナーやプログラマーがシーンやPrefabへ本番素材を組み込んだ後は、誤って再実行しないよう注意すること。
    /// </summary>
    public static class ProjectSceneSetupTool
    {
        private const string STAGE_DEFINITION_PATH = "Assets/Settings/StageDefinition.asset";
        private const string SPRITE_DIR = "Assets/Level/PendulumSprites";
        private const string CIRCLE_SPRITE_PATH = SPRITE_DIR + "/Circle.png";
        private const string SQUARE_SPRITE_PATH = SPRITE_DIR + "/Square.png";
        private const string PREFAB_DIR = "Assets/Level/Prefabs";
        private const string SINGLE_PREFAB_PATH = PREFAB_DIR + "/PendulumSingle.prefab";
        private const string DOUBLE_PREFAB_PATH = PREFAB_DIR + "/PendulumDouble.prefab";
        private const string GAME_SCENE_PATH = "Assets/Scenes/GameScene.unity";
        private const string STAGE_SCENE_PATH = "Assets/Scenes/Stage_Default.unity";

        private static readonly Color BG_COLOR = new Color(0.090f, 0.169f, 0.161f, 1f); // #172b29
        private static readonly Color PANEL_COLOR = new Color(0.114f, 0.204f, 0.192f, 0.94f); // #1d3431
        private static readonly Color TEXT_COLOR = new Color(0.949f, 0.933f, 0.890f, 1f); // #f2eee3
        private static readonly Color MUTED_COLOR = new Color(0.604f, 0.647f, 0.627f, 1f);
        private static readonly Color LIME_COLOR = new Color(0.835f, 1.0f, 0.345f, 1f); // #d5ff58
        private static readonly Color CORAL_COLOR = new Color(1.0f, 0.451f, 0.357f, 1f); // #ff735b
        private static readonly Color DARK_BTN_COLOR = new Color(0.20f, 0.28f, 0.26f, 1f);

        /// <summary> 必要なアセット・Prefab・シーン・Build Settingsをすべて生成・更新する。 </summary>
        [MenuItem("Taurus/Setup Project Scenes & Prefabs")]
        public static void SetupAll()
        {
            EnsureDirectory("Assets/Settings");
            EnsureDirectory(SPRITE_DIR);
            EnsureDirectory(PREFAB_DIR);
            EnsureDirectory("Assets/Scenes");

            Sprite circleSprite = EnsureCircleSprite();
            Sprite squareSprite = EnsureSquareSprite();
            StageDefinition stageDef = EnsureStageDefinition();

            PendulumMotion singlePrefab = CreatePendulumPrefab(SINGLE_PREFAB_PATH, false, circleSprite, squareSprite);
            PendulumMotion doublePrefab = CreatePendulumPrefab(DOUBLE_PREFAB_PATH, true, circleSprite, squareSprite);

            CreateStageDefaultScene();
            CreateGameScene(stageDef, singlePrefab, doublePrefab, circleSprite, squareSprite);
            UpdateBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            UnityEngine.Debug.Log($"[{nameof(ProjectSceneSetupTool)}] GameScene・Stage_Default・仮Prefab・Build Settings の構築が完了しました。");
        }

        private static void EnsureDirectory(string assetFolder)
        {
            if (!Directory.Exists(assetFolder))
            {
                Directory.CreateDirectory(assetFolder);
            }
        }

        private static Sprite EnsureCircleSprite()
        {
            if (!File.Exists(CIRCLE_SPRITE_PATH))
            {
                const int size = 128;
                Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                float center = (size - 1) * 0.5f;
                float radius = size * 0.48f;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                        float alpha = Mathf.Clamp01((radius - dist) * 1.5f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                }

                tex.Apply();
                File.WriteAllBytes(CIRCLE_SPRITE_PATH, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(CIRCLE_SPRITE_PATH, ImportAssetOptions.ForceUpdate);
            }

            ConfigureSpriteImporter(CIRCLE_SPRITE_PATH, 128f);
            return AssetDatabase.LoadAssetAtPath<Sprite>(CIRCLE_SPRITE_PATH);
        }

        private static Sprite EnsureSquareSprite()
        {
            if (!File.Exists(SQUARE_SPRITE_PATH))
            {
                const int size = 16;
                Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                Color[] pixels = new Color[size * size];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = Color.white;
                }

                tex.SetPixels(pixels);
                tex.Apply();
                File.WriteAllBytes(SQUARE_SPRITE_PATH, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(SQUARE_SPRITE_PATH, ImportAssetOptions.ForceUpdate);
            }

            ConfigureSpriteImporter(SQUARE_SPRITE_PATH, 16f);
            return AssetDatabase.LoadAssetAtPath<Sprite>(SQUARE_SPRITE_PATH);
        }

        private static void ConfigureSpriteImporter(string path, float ppu)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && (importer.textureType != TextureImporterType.Sprite || !Mathf.Approximately(importer.spritePixelsPerUnit, ppu)))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spritePixelsPerUnit = ppu;
                importer.SaveAndReimport();
            }
        }

        private static StageDefinition EnsureStageDefinition()
        {
            StageDefinition existing = AssetDatabase.LoadAssetAtPath<StageDefinition>(STAGE_DEFINITION_PATH);
            if (existing != null)
            {
                return existing;
            }

            StageDefinition asset = ScriptableObject.CreateInstance<StageDefinition>();
            AssetDatabase.CreateAsset(asset, STAGE_DEFINITION_PATH);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static PendulumMotion CreatePendulumPrefab(string path, bool isDouble, Sprite circleSprite, Sprite squareSprite)
        {
            GameObject root = new GameObject(isDouble ? "PendulumDouble" : "PendulumSingle");
            PendulumMotion motion = root.AddComponent<PendulumMotion>();
            PendulumVisualFeedback feedback = root.AddComponent<PendulumVisualFeedback>();

            // 支点ビジュアル
            GameObject pivotVisual = CreateSpriteChild("PivotVisual", root.transform, circleSprite, LIME_COLOR, Vector3.zero, new Vector3(0.22f, 0.22f, 1f), 5);
            pivotVisual.transform.localPosition = Vector3.zero;

            // 親アーム（θ=0で真下 -Y に伸びる）
            const float parentArmLength = 2.0f;
            GameObject parentArm = new GameObject("ParentArm");
            parentArm.transform.SetParent(root.transform, false);

            CreateSpriteChild("ParentRod", parentArm.transform, squareSprite, new Color(0.75f, 0.82f, 0.78f, 1f), new Vector3(0f, -parentArmLength * 0.5f, 0f), new Vector3(0.08f, parentArmLength, 1f), 1);

            GameObject parentBob = CreateSpriteChild("ParentBob", parentArm.transform, circleSprite, new Color(0.17f, 0.27f, 0.25f, 1f), new Vector3(0f, -parentArmLength, 0f), new Vector3(0.55f, 0.55f, 1f), 3);
            CreateSpriteChild("ParentBobRim", parentBob.transform, circleSprite, LIME_COLOR, Vector3.zero, new Vector3(1.16f, 1.16f, 1f), 2);
            SpriteRenderer parentBobRenderer = parentBob.GetComponent<SpriteRenderer>();

            Transform childArmTransform = null;
            Transform childBobTransform = null;
            SpriteRenderer childBobRenderer = null;

            if (isDouble)
            {
                const float childArmLength = 1.45f;
                GameObject childArm = new GameObject("ChildArm");
                childArm.transform.SetParent(parentArm.transform, false);
                childArm.transform.localPosition = new Vector3(0f, -parentArmLength, 0f);
                childArmTransform = childArm.transform;

                CreateSpriteChild("ChildRod", childArm.transform, squareSprite, new Color(1.0f, 0.58f, 0.50f, 1f), new Vector3(0f, -childArmLength * 0.5f, 0f), new Vector3(0.07f, childArmLength, 1f), 1);

                GameObject childBob = CreateSpriteChild("ChildBob", childArm.transform, circleSprite, new Color(0.29f, 0.23f, 0.20f, 1f), new Vector3(0f, -childArmLength, 0f), new Vector3(0.46f, 0.46f, 1f), 4);
                CreateSpriteChild("ChildBobRim", childBob.transform, circleSprite, CORAL_COLOR, Vector3.zero, new Vector3(1.18f, 1.18f, 1f), 3);
                childBobTransform = childBob.transform;
                childBobRenderer = childBob.GetComponent<SpriteRenderer>();
            }

            motion.ConfigureArms(parentArm.transform, childArmTransform, isDouble);
            feedback.ConfigureBobs(parentBob.transform, parentBobRenderer, childBobTransform, childBobRenderer);

            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return savedPrefab.GetComponent<PendulumMotion>();
        }

        private static GameObject CreateSpriteChild(
            string name,
            Transform parent,
            Sprite sprite,
            Color color,
            Vector3 localPosition,
            Vector3 localScale,
            int sortingOrder)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = sortingOrder;
            return go;
        }

        private static void CreateStageDefaultScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject stageRoot = new GameObject("StageRoot");
            StageAnchor anchor = stageRoot.AddComponent<StageAnchor>();
            stageRoot.AddComponent<StageSceneBootstrapper>();

            GameObject pendulumRoot = new GameObject("PendulumRoot");
            pendulumRoot.transform.SetParent(stageRoot.transform, false);

            GameObject singlePivot = new GameObject("Pivot_Single");
            singlePivot.transform.SetParent(stageRoot.transform, false);
            singlePivot.transform.position = new Vector3(0f, 0.55f, 0f);

            GameObject leftPivot = new GameObject("Pivot_Dual_Left");
            leftPivot.transform.SetParent(stageRoot.transform, false);
            leftPivot.transform.position = new Vector3(-3.2f, 0.55f, 0f);

            GameObject rightPivot = new GameObject("Pivot_Dual_Right");
            rightPivot.transform.SetParent(stageRoot.transform, false);
            rightPivot.transform.position = new Vector3(3.2f, 0.55f, 0f);

            anchor.SetPivots(
                pendulumRoot.transform,
                singlePivot.transform,
                new[] { leftPivot.transform, rightPivot.transform });

            EditorSceneManager.SaveScene(scene, STAGE_SCENE_PATH);
        }

        private static void CreateGameScene(
            StageDefinition stageDef,
            PendulumMotion singlePrefab,
            PendulumMotion doublePrefab,
            Sprite circleSprite,
            Sprite squareSprite)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // カメラ
            GameObject camObj = new GameObject("Main Camera");
            camObj.tag = "MainCamera";
            Camera cam = camObj.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5.0f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BG_COLOR;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            camObj.AddComponent<AudioListener>();

            // EventSystem (New Input System)
            GameObject eventSystemObj = new GameObject("EventSystem");
            eventSystemObj.AddComponent<EventSystem>();
            eventSystemObj.AddComponent<InputSystemUIInputModule>();

            // GameSystem
            GameObject systemObj = new GameObject("GameSystem");
            StageLoader stageLoader = systemObj.AddComponent<StageLoader>();
            GameController gameController = systemObj.AddComponent<GameController>();
            gameController.ConfigureReferences(stageLoader, stageDef, singlePrefab, doublePrefab);

            AudioSource seSource = systemObj.AddComponent<AudioSource>();
            seSource.playOnAwake = false;
            AudioSource bgmSource = systemObj.AddComponent<AudioSource>();
            bgmSource.playOnAwake = false;
            bgmSource.loop = true;

            GameAudioHooks audioHooks = systemObj.AddComponent<GameAudioHooks>();
            audioHooks.ConfigureReferences(gameController, seSource, bgmSource);

            // Canvas
            Font uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GameObject canvasObj = new GameObject("Canvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObj.AddComponent<GraphicRaycaster>();

            HUDController hud = canvasObj.AddComponent<HUDController>();
            DebugOverlay debugOverlay = canvasObj.AddComponent<DebugOverlay>();

            // Title Panel
            GameObject titlePanel = CreatePanel("TitlePanel", canvasObj.transform, PANEL_COLOR);
            CreateText("Eyebrow", titlePanel.transform, uiFont, "PENDULUM CHALLENGE", 24, LIME_COLOR, new Vector2(0f, 260f), new Vector2(800f, 50f));
            CreateText("TitleLogo", titlePanel.transform, uiFont, "10カウント", 96, TEXT_COLOR, new Vector2(0f, 165f), new Vector2(900f, 120f), FontStyle.Bold);
            CreateText("SubText", titlePanel.transform, uiFont, "揺れる振り子の真上通過を数えろ。\nSPACE を10回押してルールを見る", 28, MUTED_COLOR, new Vector2(0f, 45f), new Vector2(900f, 90f));

            GameObject meterBg = CreateImage("StartMeterBg", titlePanel.transform, squareSprite, DARK_BTN_COLOR, new Vector2(0f, -45f), new Vector2(520f, 22f));
            GameObject meterFillObj = CreateImage("StartMeterFill", meterBg.transform, squareSprite, LIME_COLOR, Vector2.zero, new Vector2(520f, 22f));
            Image meterFill = meterFillObj.GetComponent<Image>();
            meterFill.type = Image.Type.Filled;
            meterFill.fillMethod = Image.FillMethod.Horizontal;
            meterFill.fillAmount = 0f;

            Text startCountText = CreateText("StartCountText", titlePanel.transform, uiFont, "0 / 10", 34, LIME_COLOR, new Vector2(0f, -100f), new Vector2(400f, 50f), FontStyle.Bold);
            Button titleStartBtn = CreateButton("TitleStartButton", titlePanel.transform, uiFont, squareSprite, "スペースキーを10回押す", LIME_COLOR, BG_COLOR, new Vector2(0f, -195f), new Vector2(440f, 76f));

            // Rules Panel
            GameObject rulesPanel = CreatePanel("RulesPanel", canvasObj.transform, PANEL_COLOR);
            CreateText("RulesTitle", rulesPanel.transform, uiFont, "真上を見逃すな", 56, TEXT_COLOR, new Vector2(0f, 350f), new Vector2(900f, 80f), FontStyle.Bold);
            CreateText("RulesLead", rulesPanel.transform, uiFont, "一周の終わりではなく、頂点を通過した瞬間にカウント（光ったら +1）", 26, MUTED_COLOR, new Vector2(0f, 285f), new Vector2(1200f, 50f));

            RuleDemoAnimator demoAnimator = rulesPanel.AddComponent<RuleDemoAnimator>();
            (RectTransform spinArm, Image spinBob) = CreateRuleDemoCard("DemoSpin", rulesPanel.transform, uiFont, circleSprite, squareSprite, new Vector2(-420f, 55f), "真上を通ったら数える", "頂点を通過した瞬間に+1");
            (RectTransform feintArm, Image _) = CreateRuleDemoCard("DemoFeint", rulesPanel.transform, uiFont, circleSprite, squareSprite, new Vector2(0f, 55f), "0回でも油断しない", "頂点の手前で反転するフェイント");
            (RectTransform reverseArm, Image reverseBob) = CreateRuleDemoCard("DemoReverse", rulesPanel.transform, uiFont, circleSprite, squareSprite, new Vector2(420f, 55f), "区間ごとに方向が変わる", "回数を終えたら真下を通って反転");
            demoAnimator.ConfigureDemos(spinArm, spinBob, feintArm, reverseArm, reverseBob);

            CreateText("RulesControls", rulesPanel.transform, uiFont, "[SPACE] +1    /    [CTRL] -1    /    各ウェーブ 10秒", 28, LIME_COLOR, new Vector2(0f, -200f), new Vector2(1000f, 50f), FontStyle.Bold);
            Button rulesStartBtn = CreateButton("RulesStartButton", rulesPanel.transform, uiFont, squareSprite, "ゲームスタート (SPACE)", LIME_COLOR, BG_COLOR, new Vector2(0f, -295f), new Vector2(440f, 76f));

            // Game Panel (透明にして中央の振り子が見えるようにする)
            GameObject gamePanel = CreatePanel("GamePanel", canvasObj.transform, new Color(0f, 0f, 0f, 0f));
            Text waveText = CreateText("WaveText", gamePanel.transform, uiFont, "WAVE 01", 36, LIME_COLOR, new Vector2(-720f, 455f), new Vector2(360f, 60f), FontStyle.Bold, TextAnchor.MiddleLeft);
            Text timerText = CreateText("TimerText", gamePanel.transform, uiFont, "10.0 SEC", 48, TEXT_COLOR, new Vector2(700f, 455f), new Vector2(360f, 68f), FontStyle.Bold, TextAnchor.MiddleRight);

            Text counterLabel = CreateText("CounterLabel", gamePanel.transform, uiFont, "YOUR COUNT", 22, MUTED_COLOR, new Vector2(-240f, -340f), new Vector2(260f, 36f));
            counterLabel.alignment = TextAnchor.MiddleCenter;
            Text counterText = CreateText("CounterText", gamePanel.transform, uiFont, "00", 78, LIME_COLOR, new Vector2(-240f, -405f), new Vector2(260f, 95f), FontStyle.Bold);

            Button plusBtn = CreateButton("PlusButton", gamePanel.transform, uiFont, squareSprite, "SPACE  +1", LIME_COLOR, BG_COLOR, new Vector2(60f, -395f), new Vector2(240f, 76f));
            Button minusBtn = CreateButton("MinusButton", gamePanel.transform, uiFont, squareSprite, "CTRL  -1", DARK_BTN_COLOR, TEXT_COLOR, new Vector2(330f, -395f), new Vector2(220f, 76f));

            Text feedbackText = CreateText("FeedbackText", gamePanel.transform, uiFont, string.Empty, 28, TEXT_COLOR, new Vector2(0f, -485f), new Vector2(960f, 48f), FontStyle.Bold);

            GameObject successBanner = CreateImage("SuccessBanner", gamePanel.transform, squareSprite, new Color(0.114f, 0.204f, 0.192f, 0.92f), new Vector2(0f, 250f), new Vector2(640f, 96f));
            CreateText("SuccessBannerLabel", successBanner.transform, uiFont, "CLEAR! 次のウェーブへ", 40, LIME_COLOR, Vector2.zero, new Vector2(600f, 70f), FontStyle.Bold);
            successBanner.SetActive(false);

            // Result Panel
            GameObject resultPanel = CreatePanel("ResultPanel", canvasObj.transform, PANEL_COLOR);
            CreateText("ResultEyebrow", resultPanel.transform, uiFont, "RUN COMPLETE", 24, LIME_COLOR, new Vector2(0f, 340f), new Vector2(600f, 44f));
            CreateText("ResultTitle", resultPanel.transform, uiFont, "GAME OVER", 76, TEXT_COLOR, new Vector2(0f, 255f), new Vector2(800f, 100f), FontStyle.Bold);
            Text clearedWavesText = CreateText("ClearedWavesText", resultPanel.transform, uiFont, "0", 120, LIME_COLOR, new Vector2(0f, 120f), new Vector2(400f, 140f), FontStyle.Bold);
            CreateText("ClearedCaption", resultPanel.transform, uiFont, "WAVES CLEARED", 26, MUTED_COLOR, new Vector2(0f, 30f), new Vector2(400f, 44f));
            Text resultDetailText = CreateText("ResultDetailText", resultPanel.transform, uiFont, string.Empty, 26, TEXT_COLOR, new Vector2(0f, -20f), new Vector2(700f, 44f));

            CreateText("RecordsTitle", resultPanel.transform, uiFont, "TOP 3 RECORDS", 24, LIME_COLOR, new Vector2(0f, -90f), new Vector2(500f, 40f), FontStyle.Bold);
            Text[] recordValueTexts = new Text[3];
            string[] rankLabels = { "1ST", "2ND", "3RD" };
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 190f;
                recordValueTexts[i] = CreateText($"RecordValue_{i + 1}", resultPanel.transform, uiFont, "—", 42, i == 0 ? LIME_COLOR : TEXT_COLOR, new Vector2(x, -150f), new Vector2(160f, 56f), FontStyle.Bold);
                CreateText($"RecordRank_{i + 1}", resultPanel.transform, uiFont, rankLabels[i], 20, MUTED_COLOR, new Vector2(x, -195f), new Vector2(160f, 34f));
            }

            Button retryBtn = CreateButton("RetryButton", resultPanel.transform, uiFont, squareSprite, "もう一度遊ぶ", LIME_COLOR, BG_COLOR, new Vector2(-150f, -310f), new Vector2(260f, 74f));
            Button homeBtn = CreateButton("HomeButton", resultPanel.transform, uiFont, squareSprite, "タイトルへ戻る", DARK_BTN_COLOR, TEXT_COLOR, new Vector2(150f, -310f), new Vector2(260f, 74f));

            // Debug Overlay Panel
            GameObject debugRoot = CreateImage("DebugPanel", canvasObj.transform, squareSprite, new Color(0.06f, 0.12f, 0.11f, 0.86f), new Vector2(620f, 240f), new Vector2(620f, 360f));
            Text debugText = CreateText("DebugText", debugRoot.transform, uiFont, "DEBUG", 20, TEXT_COLOR, Vector2.zero, new Vector2(590f, 330f), FontStyle.Normal, TextAnchor.UpperLeft);
            debugText.supportRichText = true;
            debugOverlay.ConfigureReferences(gameController, debugRoot, debugText);

            hud.ConfigureReferences(
                gameController,
                titlePanel,
                rulesPanel,
                gamePanel,
                resultPanel,
                successBanner,
                startCountText,
                meterFill,
                titleStartBtn,
                rulesStartBtn,
                waveText,
                timerText,
                counterText,
                feedbackText,
                plusBtn,
                minusBtn,
                clearedWavesText,
                resultDetailText,
                recordValueTexts,
                retryBtn,
                homeBtn);

            EditorSceneManager.SaveScene(scene, GAME_SCENE_PATH);
        }

        private static (RectTransform arm, Image bob) CreateRuleDemoCard(
            string name,
            Transform parent,
            Font font,
            Sprite circleSprite,
            Sprite squareSprite,
            Vector2 anchoredPosition,
            string title,
            string description)
        {
            GameObject card = CreateImage(name, parent, squareSprite, new Color(0.106f, 0.196f, 0.180f, 1f), anchoredPosition, new Vector2(380f, 340f));

            GameObject demoBox = CreateImage("DemoBox", card.transform, squareSprite, new Color(0.09f, 0.16f, 0.145f, 1f), new Vector2(0f, 55f), new Vector2(330f, 180f));
            CreateImage("Pivot", demoBox.transform, circleSprite, LIME_COLOR, Vector2.zero, new Vector2(14f, 14f));

            GameObject armObj = new GameObject("DemoArm", typeof(RectTransform));
            armObj.transform.SetParent(demoBox.transform, false);
            RectTransform armRect = armObj.GetComponent<RectTransform>();
            armRect.anchoredPosition = Vector2.zero;
            armRect.sizeDelta = new Vector2(4f, 64f);

            CreateImage("Rod", armObj.transform, squareSprite, new Color(0.74f, 0.80f, 0.76f, 1f), new Vector2(0f, -32f), new Vector2(4f, 64f));
            GameObject bobObj = CreateImage("Bob", armObj.transform, circleSprite, new Color(0.16f, 0.27f, 0.24f, 1f), new Vector2(0f, -64f), new Vector2(26f, 26f));

            CreateText("CardTitle", card.transform, font, title, 24, LIME_COLOR, new Vector2(0f, -75f), new Vector2(340f, 40f), FontStyle.Bold);
            CreateText("CardDesc", card.transform, font, description, 20, MUTED_COLOR, new Vector2(0f, -125f), new Vector2(340f, 56f));

            return (armRect, bobObj.GetComponent<Image>());
        }

        private static GameObject CreatePanel(string name, Transform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = color.a > 0.01f;
            return go;
        }

        private static GameObject CreateImage(string name, Transform parent, Sprite sprite, Color color, Vector2 pos, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            Image img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return go;
        }

        private static Text CreateText(
            string name,
            Transform parent,
            Font font,
            string content,
            int fontSize,
            Color color,
            Vector2 pos,
            Vector2 size,
            FontStyle style = FontStyle.Normal,
            TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            Text text = go.GetComponent<Text>();
            text.font = font;
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.fontStyle = style;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateButton(
            string name,
            Transform parent,
            Font font,
            Sprite sprite,
            string label,
            Color bgColor,
            Color textColor,
            Vector2 pos,
            Vector2 size)
        {
            GameObject go = CreateImage(name, parent, sprite, bgColor, pos, size);
            Button btn = go.AddComponent<Button>();
            CreateText("Label", go.transform, font, label, 26, textColor, Vector2.zero, size, FontStyle.Bold);
            return btn;
        }

        private static void UpdateBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(GAME_SCENE_PATH, true),
                new EditorBuildSettingsScene(STAGE_SCENE_PATH, true),
            };

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
