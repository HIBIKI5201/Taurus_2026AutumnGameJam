using System.Collections.Generic;
using System.IO;
using Taurus.Develop.Debug;
using Taurus.Runtime.Camera;
using Taurus.Runtime.Game;
using Taurus.Runtime.Pendulum;
using Taurus.Runtime.Stage;
using Taurus.Runtime.UI;
using Unity.Cinemachine;
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
    /// 初期プロトタイプ構築用に、3Dグレーボックスの仮建物ステージ（Stage_Default）・3D仮振り子Prefab・
    /// Cinemachine 3 カメラリグ付き GameScene・StageDefinition・Build Settings を一括生成するエディタツール。
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
        private const string MATERIAL_DIR = "Assets/Level/GrayboxMaterials";
        private const string PREFAB_DIR = "Assets/Level/Prefabs";
        private const string SINGLE_PREFAB_PATH = PREFAB_DIR + "/PendulumSingle.prefab";
        private const string DOUBLE_PREFAB_PATH = PREFAB_DIR + "/PendulumDouble.prefab";
        private const string GAME_SCENE_PATH = "Assets/Scenes/GameScene.unity";
        private const string STAGE_SCENE_PATH = "Assets/Scenes/Stage_Default.unity";

        // 振り子支点の基準高さ（作業机天板 y=-1.25、天井 y=7.4 に対して二重振り子全長3.68が干渉しない位置）
        private const float PIVOT_HEIGHT_Y = 2.65f;
        private const float DUAL_PIVOT_OFFSET_X = 2.75f;

        private static readonly Color BG_COLOR = new Color(0.090f, 0.169f, 0.161f, 1f);         // #172b29
        private static readonly Color PANEL_OVERLAY_COLOR = new Color(0.090f, 0.169f, 0.161f, 0.68f); // 3D空間が透ける半透明暗緑
        private static readonly Color TEXT_COLOR = new Color(0.949f, 0.933f, 0.890f, 1f);       // #f2eee3
        private static readonly Color MUTED_COLOR = new Color(0.66f, 0.72f, 0.69f, 1f);
        private static readonly Color LIME_COLOR = new Color(0.835f, 1.0f, 0.345f, 1f);         // #d5ff58
        private static readonly Color CORAL_COLOR = new Color(1.0f, 0.451f, 0.357f, 1f);        // #ff735b
        private static readonly Color DARK_BTN_COLOR = new Color(0.20f, 0.28f, 0.26f, 1f);

        /// <summary> 必要なアセット・3D仮Prefab・3D仮ステージ・Cinemachine付きGameScene・Build Settingsをすべて生成する。 </summary>
        [MenuItem("Taurus/Setup Project Scenes & Prefabs")]
        public static void SetupAll()
        {
            EnsureDirectory("Assets/Settings");
            EnsureDirectory(SPRITE_DIR);
            EnsureDirectory(MATERIAL_DIR);
            EnsureDirectory(PREFAB_DIR);
            EnsureDirectory("Assets/Scenes");

            Sprite circleSprite = EnsureCircleSprite();
            Sprite squareSprite = EnsureSquareSprite();
            StageDefinition stageDef = EnsureStageDefinition();

            // 3Dグレーボックス用のURP Litマテリアル群を生成
            Material matWall = EnsureMaterial("Mat_Wall", new Color(0.14f, 0.21f, 0.20f, 1f), 0.15f, 0.0f);
            Material matFloor = EnsureMaterial("Mat_Floor", new Color(0.20f, 0.16f, 0.13f, 1f), 0.25f, 0.0f);
            Material matWood = EnsureMaterial("Mat_Wood", new Color(0.35f, 0.25f, 0.18f, 1f), 0.30f, 0.0f);
            Material matPropDark = EnsureMaterial("Mat_PropDark", new Color(0.22f, 0.20f, 0.19f, 1f), 0.35f, 0.15f);
            Material matLeather = EnsureMaterial("Mat_Leather", new Color(0.34f, 0.18f, 0.14f, 1f), 0.40f, 0.0f);
            Material matStand = EnsureMaterial("Mat_Stand", new Color(0.28f, 0.27f, 0.24f, 1f), 0.55f, 0.50f);
            Material matParentRod = EnsureMaterial("Mat_ParentRod", new Color(0.76f, 0.81f, 0.78f, 1f), 0.65f, 0.70f);
            Material matChildRod = EnsureMaterial("Mat_ChildRod", new Color(0.95f, 0.60f, 0.52f, 1f), 0.65f, 0.60f);
            Material matParentBob = EnsureMaterial("Mat_ParentBob", new Color(0.22f, 0.34f, 0.31f, 1f), 0.55f, 0.30f, enableEmission: true);
            Material matChildBob = EnsureMaterial("Mat_ChildBob", new Color(0.36f, 0.27f, 0.24f, 1f), 0.55f, 0.30f, enableEmission: true);
            Material matLime = EnsureMaterial("Mat_LimeAccent", LIME_COLOR, 0.5f, 0.2f, enableEmission: true);

            PendulumMotion singlePrefab = CreatePendulum3DPrefab(
                SINGLE_PREFAB_PATH,
                false,
                matStand,
                matParentRod,
                matChildRod,
                matParentBob,
                matChildBob,
                matLime);

            PendulumMotion doublePrefab = CreatePendulum3DPrefab(
                DOUBLE_PREFAB_PATH,
                true,
                matStand,
                matParentRod,
                matChildRod,
                matParentBob,
                matChildBob,
                matLime);

            CreateStageDefault3DScene(matWall, matFloor, matWood, matPropDark, matLeather, matStand);
            CreateGameScene(stageDef, singlePrefab, doublePrefab, circleSprite, squareSprite);
            UpdateBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            UnityEngine.Debug.Log($"[{nameof(ProjectSceneSetupTool)}] 3Dグレーボックスステージ・3D仮振り子Prefab・Cinemachineカメラ付きGameSceneの構築が完了しました。");
        }

        private static void EnsureDirectory(string assetFolder)
        {
            if (!Directory.Exists(assetFolder))
            {
                Directory.CreateDirectory(assetFolder);
            }
        }

        /// <summary> URP Lit シェーダーでグレーボックス用マテリアルを作成または更新する。 </summary>
        private static Material EnsureMaterial(string matName, Color baseColor, float smoothness, float metallic, bool enableEmission = false)
        {
            string path = $"{MATERIAL_DIR}/{matName}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            mat.SetColor("_BaseColor", baseColor);
            mat.SetColor("_Color", baseColor);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);

            if (enableEmission)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
            }

            EditorUtility.SetDirty(mat);
            return mat;
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

        /// <summary>
        /// 3Dプリミティブで構成したグレーボックス仮振り子Prefabを作成する。
        /// 支点原点(0,0,0)に対し、背面(Z=+0.35)に台座フレームを逃がすことで、振り子が360度回転しても支柱に干渉しない構造にしている。
        /// </summary>
        private static PendulumMotion CreatePendulum3DPrefab(
            string path,
            bool isDouble,
            Material matStand,
            Material matParentRod,
            Material matChildRod,
            Material matParentBob,
            Material matChildBob,
            Material matLime)
        {
            GameObject root = new GameObject(isDouble ? "PendulumDouble" : "PendulumSingle");
            PendulumMotion motion = root.AddComponent<PendulumMotion>();
            PendulumVisualFeedback feedback = root.AddComponent<PendulumVisualFeedback>();

            // 台座（支点から作業机天板 y=-3.90 ローカル位置まで伸びる背面支柱＋ベース）
            GameObject standRoot = new GameObject("Stand_Graybox");
            standRoot.transform.SetParent(root.transform, false);
            CreatePrimitiveChild("StandBase", PrimitiveType.Cube, standRoot.transform, matStand, new Vector3(0f, -3.80f, 0.35f), new Vector3(1.6f, 0.22f, 1.1f));
            CreatePrimitiveChild("StandPillar", PrimitiveType.Cylinder, standRoot.transform, matStand, new Vector3(0f, -1.90f, 0.45f), new Vector3(0.16f, 1.95f, 0.16f));
            CreatePrimitiveChild("PivotShaft", PrimitiveType.Cylinder, standRoot.transform, matStand, new Vector3(0f, 0f, 0.22f), new Vector3(0.14f, 0.24f, 0.14f), Quaternion.Euler(90f, 0f, 0f));

            // 支点キャップ（ライム色アクセント）
            CreatePrimitiveChild("PivotCap", PrimitiveType.Sphere, root.transform, matLime, new Vector3(0f, 0f, -0.06f), new Vector3(0.24f, 0.24f, 0.14f));

            // 親アーム（θ=0で真下 -Y 方向へ伸びる）
            const float parentArmLength = 2.0f;
            GameObject parentArm = new GameObject("ParentArm");
            parentArm.transform.SetParent(root.transform, false);

            CreatePrimitiveChild("ParentRod", PrimitiveType.Cylinder, parentArm.transform, matParentRod, new Vector3(0f, -parentArmLength * 0.5f, 0f), new Vector3(0.08f, parentArmLength * 0.5f, 0.08f));
            GameObject parentBob = CreatePrimitiveChild("ParentBob", PrimitiveType.Sphere, parentArm.transform, matParentBob, new Vector3(0f, -parentArmLength, 0f), new Vector3(0.56f, 0.56f, 0.56f));
            Renderer parentBobRenderer = parentBob.GetComponent<Renderer>();

            Transform childArmTransform = null;
            Transform childBobTransform = null;
            Renderer childBobRenderer = null;

            if (isDouble)
            {
                // 子アームは親玉の中心(y = -parentArmLength)をピボットにし、手前側(Z = -0.18)へ少しオフセットして視認性を高める
                const float childArmLength = 1.45f;
                GameObject childArm = new GameObject("ChildArm");
                childArm.transform.SetParent(parentArm.transform, false);
                childArm.transform.localPosition = new Vector3(0f, -parentArmLength, -0.18f);
                childArmTransform = childArm.transform;

                CreatePrimitiveChild("ChildRod", PrimitiveType.Cylinder, childArm.transform, matChildRod, new Vector3(0f, -childArmLength * 0.5f, 0f), new Vector3(0.065f, childArmLength * 0.5f, 0.065f));
                GameObject childBob = CreatePrimitiveChild("ChildBob", PrimitiveType.Sphere, childArm.transform, matChildBob, new Vector3(0f, -childArmLength, 0f), new Vector3(0.46f, 0.46f, 0.46f));
                childBobTransform = childBob.transform;
                childBobRenderer = childBob.GetComponent<Renderer>();
            }

            motion.ConfigureArms(parentArm.transform, childArmTransform, isDouble);
            feedback.ConfigureMeshBobs(parentBob.transform, parentBobRenderer, childBobTransform, childBobRenderer);

            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return savedPrefab.GetComponent<PendulumMotion>();
        }

        /// <summary> 3Dプリミティブをコライダー無しで子生成するヘルパー。 </summary>
        private static GameObject CreatePrimitiveChild(
            string name,
            PrimitiveType primitiveType,
            Transform parent,
            Material material,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion? localRotation = null)
        {
            GameObject go = GameObject.CreatePrimitive(primitiveType);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            if (localRotation.HasValue)
            {
                go.transform.localRotation = localRotation.Value;
            }

            Collider col = go.GetComponent<Collider>();
            if (col != null)
            {
                Object.DestroyImmediate(col);
            }

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
            }

            return go;
        }

        /// <summary>
        /// 3Dグレーボックスの仮建物（リサイクルショップ空間＋作業机＋仮プロップ3種＋ライティング）を Stage_Default.unity へ構築する。
        /// </summary>
        private static void CreateStageDefault3DScene(
            Material matWall,
            Material matFloor,
            Material matWood,
            Material matPropDark,
            Material matLeather,
            Material matStand)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 環境光（暗い深緑寄りの落ち着いた室内アンビエント）
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.24f, 0.30f, 0.28f, 1f);

            GameObject stageRoot = new GameObject("StageRoot");
            StageAnchor anchor = stageRoot.AddComponent<StageAnchor>();
            stageRoot.AddComponent<StageSceneBootstrapper>();

            // 振り子支点アンカー（作業机の上）
            GameObject pendulumRoot = new GameObject("PendulumRoot");
            pendulumRoot.transform.SetParent(stageRoot.transform, false);

            GameObject singlePivot = new GameObject("Pivot_Single");
            singlePivot.transform.SetParent(stageRoot.transform, false);
            singlePivot.transform.position = new Vector3(0f, PIVOT_HEIGHT_Y, 0f);

            GameObject leftPivot = new GameObject("Pivot_Dual_Left");
            leftPivot.transform.SetParent(stageRoot.transform, false);
            leftPivot.transform.position = new Vector3(-DUAL_PIVOT_OFFSET_X, PIVOT_HEIGHT_Y, 0f);

            GameObject rightPivot = new GameObject("Pivot_Dual_Right");
            rightPivot.transform.SetParent(stageRoot.transform, false);
            rightPivot.transform.position = new Vector3(DUAL_PIVOT_OFFSET_X, PIVOT_HEIGHT_Y, 0f);

            anchor.SetPivots(
                pendulumRoot.transform,
                singlePivot.transform,
                new[] { leftPivot.transform, rightPivot.transform });

            // 1. 建物グレーボックス（リサイクルショップ部屋空間：幅18m × 高さ10m × 奥行16m）
            GameObject buildingRoot = new GameObject("Building_Graybox");
            buildingRoot.transform.SetParent(stageRoot.transform, false);

            CreatePrimitiveChild("Floor", PrimitiveType.Cube, buildingRoot.transform, matFloor, new Vector3(0f, -3.2f, 0f), new Vector3(20f, 0.4f, 20f));
            CreatePrimitiveChild("Ceiling", PrimitiveType.Cube, buildingRoot.transform, matWall, new Vector3(0f, 7.6f, 0f), new Vector3(20f, 0.4f, 20f));
            CreatePrimitiveChild("BackWall", PrimitiveType.Cube, buildingRoot.transform, matWall, new Vector3(0f, 2.2f, 5.5f), new Vector3(20f, 11f, 0.4f));
            CreatePrimitiveChild("LeftWall", PrimitiveType.Cube, buildingRoot.transform, matWall, new Vector3(-9.5f, 2.2f, 0f), new Vector3(0.4f, 11f, 20f));
            CreatePrimitiveChild("RightWall", PrimitiveType.Cube, buildingRoot.transform, matWall, new Vector3(9.5f, 2.2f, 0f), new Vector3(0.4f, 11f, 20f));

            // 天井の梁と柱（空間の奥行きとスケール感を出す）
            CreatePrimitiveChild("Beam_Front", PrimitiveType.Cube, buildingRoot.transform, matWood, new Vector3(0f, 7.1f, -1.5f), new Vector3(19f, 0.45f, 0.45f));
            CreatePrimitiveChild("Beam_Back", PrimitiveType.Cube, buildingRoot.transform, matWood, new Vector3(0f, 7.1f, 3.0f), new Vector3(19f, 0.45f, 0.45f));
            CreatePrimitiveChild("Pillar_BackLeft", PrimitiveType.Cube, buildingRoot.transform, matWood, new Vector3(-7.8f, 2.2f, 5.1f), new Vector3(0.5f, 10.6f, 0.5f));
            CreatePrimitiveChild("Pillar_BackRight", PrimitiveType.Cube, buildingRoot.transform, matWood, new Vector3(7.8f, 2.2f, 5.1f), new Vector3(0.5f, 10.6f, 0.5f));

            // 奥壁の仮窓枠
            GameObject windowFrame = new GameObject("WindowFrame_Graybox");
            windowFrame.transform.SetParent(buildingRoot.transform, false);
            windowFrame.transform.localPosition = new Vector3(0f, 4.2f, 5.25f);
            CreatePrimitiveChild("FrameTop", PrimitiveType.Cube, windowFrame.transform, matWood, new Vector3(0f, 1.4f, 0f), new Vector3(5.6f, 0.25f, 0.25f));
            CreatePrimitiveChild("FrameBottom", PrimitiveType.Cube, windowFrame.transform, matWood, new Vector3(0f, -1.4f, 0f), new Vector3(5.6f, 0.25f, 0.25f));
            CreatePrimitiveChild("FrameLeft", PrimitiveType.Cube, windowFrame.transform, matWood, new Vector3(-2.7f, 0f, 0f), new Vector3(0.25f, 2.9f, 0.25f));
            CreatePrimitiveChild("FrameRight", PrimitiveType.Cube, windowFrame.transform, matWood, new Vector3(2.7f, 0f, 0f), new Vector3(0.25f, 2.9f, 0.25f));
            CreatePrimitiveChild("FrameMullion", PrimitiveType.Cube, windowFrame.transform, matWood, Vector3.zero, new Vector3(0.15f, 2.7f, 0.15f));

            // 2. 中央の作業机（振り子台座が乗るメインテーブル：天板上面 y = -1.25）
            GameObject workbench = new GameObject("Workbench_Graybox");
            workbench.transform.SetParent(stageRoot.transform, false);
            CreatePrimitiveChild("TableTop", PrimitiveType.Cube, workbench.transform, matWood, new Vector3(0f, -1.40f, 0.35f), new Vector3(9.4f, 0.30f, 2.4f));
            CreatePrimitiveChild("Leg_FL", PrimitiveType.Cube, workbench.transform, matPropDark, new Vector3(-4.2f, -2.30f, -0.55f), new Vector3(0.28f, 1.6f, 0.28f));
            CreatePrimitiveChild("Leg_FR", PrimitiveType.Cube, workbench.transform, matPropDark, new Vector3(4.2f, -2.30f, -0.55f), new Vector3(0.28f, 1.6f, 0.28f));
            CreatePrimitiveChild("Leg_BL", PrimitiveType.Cube, workbench.transform, matPropDark, new Vector3(-4.2f, -2.30f, 1.25f), new Vector3(0.28f, 1.6f, 0.28f));
            CreatePrimitiveChild("Leg_BR", PrimitiveType.Cube, workbench.transform, matPropDark, new Vector3(4.2f, -2.30f, 1.25f), new Vector3(0.28f, 1.6f, 0.28f));

            // 3. 背景プロップ仮グレーボックス（発注書3種に対応した位置決め）
            GameObject propsRoot = new GameObject("Props_Graybox");
            propsRoot.transform.SetParent(stageRoot.transform, false);

            // プロップA：左奥「古道具の木製箪笥・陳列棚」
            GameObject shelf = new GameObject("AntiqueShelf_Graybox");
            shelf.transform.SetParent(propsRoot.transform, false);
            shelf.transform.localPosition = new Vector3(-5.8f, -3.0f, 4.2f);
            CreatePrimitiveChild("ShelfBody", PrimitiveType.Cube, shelf.transform, matWood, new Vector3(0f, 2.2f, 0f), new Vector3(2.8f, 4.4f, 1.1f));
            CreatePrimitiveChild("ShelfItem1", PrimitiveType.Cube, shelf.transform, matStand, new Vector3(-0.6f, 4.7f, 0f), new Vector3(0.7f, 0.6f, 0.6f));
            CreatePrimitiveChild("ShelfItem2", PrimitiveType.Cylinder, shelf.transform, matPropDark, new Vector3(0.6f, 4.8f, 0f), new Vector3(0.45f, 0.4f, 0.45f));

            // プロップB：右奥「ヴィンテージ革張りソファ＆木箱セット」
            GameObject sofa = new GameObject("VintageSofaAndCrate_Graybox");
            sofa.transform.SetParent(propsRoot.transform, false);
            sofa.transform.localPosition = new Vector3(5.6f, -3.0f, 3.8f);
            sofa.transform.localRotation = Quaternion.Euler(0f, -18f, 0f);
            CreatePrimitiveChild("SofaSeat", PrimitiveType.Cube, sofa.transform, matLeather, new Vector3(0f, 0.65f, 0f), new Vector3(3.2f, 0.7f, 1.4f));
            CreatePrimitiveChild("SofaBack", PrimitiveType.Cube, sofa.transform, matLeather, new Vector3(0f, 1.45f, 0.52f), new Vector3(3.2f, 1.3f, 0.38f));
            CreatePrimitiveChild("WoodCrate", PrimitiveType.Cube, sofa.transform, matWood, new Vector3(-2.2f, 0.55f, 0.3f), new Vector3(1.1f, 1.1f, 1.1f));

            // プロップC：左手前寄り「アンティーク足踏みミシン」
            GameObject sewing = new GameObject("SewingMachine_Graybox");
            sewing.transform.SetParent(propsRoot.transform, false);
            sewing.transform.localPosition = new Vector3(-6.8f, -3.0f, 0.8f);
            sewing.transform.localRotation = Quaternion.Euler(0f, 24f, 0f);
            CreatePrimitiveChild("SewingLegs", PrimitiveType.Cube, sewing.transform, matPropDark, new Vector3(0f, 0.9f, 0f), new Vector3(1.5f, 1.8f, 0.8f));
            CreatePrimitiveChild("SewingDesk", PrimitiveType.Cube, sewing.transform, matWood, new Vector3(0f, 1.9f, 0f), new Vector3(1.9f, 0.18f, 1.0f));
            CreatePrimitiveChild("SewingHead", PrimitiveType.Cube, sewing.transform, matPropDark, new Vector3(0.15f, 2.35f, 0f), new Vector3(0.95f, 0.7f, 0.45f));

            // 4. ライティング
            GameObject lightsRoot = new GameObject("Lights");
            lightsRoot.transform.SetParent(stageRoot.transform, false);

            GameObject dirLightObj = new GameObject("KeyDirectionalLight");
            dirLightObj.transform.SetParent(lightsRoot.transform, false);
            dirLightObj.transform.rotation = Quaternion.Euler(32f, -22f, 0f);
            Light dirLight = dirLightObj.AddComponent<Light>();
            dirLight.type = LightType.Directional;
            dirLight.color = new Color(1.0f, 0.95f, 0.86f, 1f);
            dirLight.intensity = 1.25f;
            dirLight.shadows = LightShadows.Soft;

            CreatePointLight("ArenaFillLight_Front", lightsRoot.transform, new Vector3(0f, 4.6f, -4.5f), new Color(0.90f, 1.0f, 0.95f, 1f), 18f, 16f);
            CreatePointLight("WarmRoomLight_Back", lightsRoot.transform, new Vector3(0f, 5.2f, 3.2f), new Color(1.0f, 0.82f, 0.58f, 1f), 14f, 14f);

            EditorSceneManager.SaveScene(scene, STAGE_SCENE_PATH);
        }

        private static void CreatePointLight(string name, Transform parent, Vector3 position, Color color, float intensity, float range)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.transform.position = position;
            Light light = obj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
        }

        private static void CreateGameScene(
            StageDefinition stageDef,
            PendulumMotion singlePrefab,
            PendulumMotion doublePrefab,
            Sprite circleSprite,
            Sprite squareSprite)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Main Camera（3D Perspective + CinemachineBrain）
            GameObject camObj = new GameObject("Main Camera");
            camObj.tag = "MainCamera";
            UnityEngine.Camera cam = camObj.AddComponent<UnityEngine.Camera>();
            cam.orthographic = false;
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 80f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BG_COLOR;
            cam.transform.position = new Vector3(0f, PIVOT_HEIGHT_Y, -10.5f);
            camObj.AddComponent<AudioListener>();

            CinemachineBrain brain = camObj.AddComponent<CinemachineBrain>();
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.65f);

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

            // Cinemachine Camera Rig（状態別バーチャルカメラ5台）
            GameObject rigObj = new GameObject("CameraRig");
            CameraRigController rigController = rigObj.AddComponent<CameraRigController>();

            CinemachineCamera titleCam = CreateCinemachineCamera("CM_TitleCamera", rigObj.transform, new Vector3(-2.6f, 3.6f, -12.0f), new Vector3(0f, 1.8f, 0f), 46f, 20);
            CinemachineCamera rulesCam = CreateCinemachineCamera("CM_RulesCamera", rigObj.transform, new Vector3(2.2f, 3.2f, -10.8f), new Vector3(0f, 2.2f, 0f), 45f, 0);
            CinemachineCamera gameSingleCam = CreateCinemachineCamera("CM_GameSingleCamera", rigObj.transform, new Vector3(0f, PIVOT_HEIGHT_Y, -10.6f), new Vector3(0f, PIVOT_HEIGHT_Y, 0f), 46f, 0);
            CinemachineCamera gameDualCam = CreateCinemachineCamera("CM_GameDualCamera", rigObj.transform, new Vector3(0f, PIVOT_HEIGHT_Y, -13.0f), new Vector3(0f, PIVOT_HEIGHT_Y, 0f), 48f, 0);
            CinemachineCamera resultCam = CreateCinemachineCamera("CM_ResultCamera", rigObj.transform, new Vector3(2.8f, 2.0f, -11.6f), new Vector3(0f, 2.4f, 0f), 45f, 0);

            rigController.ConfigureCameras(gameController, titleCam, rulesCam, gameSingleCam, gameDualCam, resultCam);

            // Canvas（Scale With Screen Size 1920x1080）
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

            // Title Panel（3Dの仮建物が奥に透けて見える半透明オーバーレイ）
            GameObject titlePanel = CreatePanel("TitlePanel", canvasObj.transform, PANEL_OVERLAY_COLOR);
            CreateText("Eyebrow", titlePanel.transform, uiFont, "PENDULUM CHALLENGE", 24, LIME_COLOR, new Vector2(0f, 260f), new Vector2(800f, 50f));
            CreateText("TitleLogo", titlePanel.transform, uiFont, "10カウント", 96, TEXT_COLOR, new Vector2(0f, 165f), new Vector2(900f, 120f), FontStyle.Bold);
            CreateText("SubText", titlePanel.transform, uiFont, "揺れる振り子の真上通過を数えろ。\nSPACE を押してルールを見る", 28, MUTED_COLOR, new Vector2(0f, 45f), new Vector2(900f, 90f));

            bool showTitleMeter = GameController.TITLE_REQUIRED_PRESSES > 1;
            GameObject meterBg = CreateImage("StartMeterBg", titlePanel.transform, squareSprite, DARK_BTN_COLOR, new Vector2(0f, -45f), new Vector2(520f, 22f));
            GameObject meterFillObj = CreateImage("StartMeterFill", meterBg.transform, squareSprite, LIME_COLOR, Vector2.zero, new Vector2(520f, 22f));
            Image meterFill = meterFillObj.GetComponent<Image>();
            meterFill.type = Image.Type.Filled;
            meterFill.fillMethod = Image.FillMethod.Horizontal;
            meterFill.fillAmount = 0f;
            meterBg.SetActive(showTitleMeter);

            Text startCountText = CreateText("StartCountText", titlePanel.transform, uiFont, $"0 / {GameController.TITLE_REQUIRED_PRESSES}", 34, LIME_COLOR, new Vector2(0f, -100f), new Vector2(400f, 50f), FontStyle.Bold);
            startCountText.gameObject.SetActive(showTitleMeter);
            Button titleStartBtn = CreateButton("TitleStartButton", titlePanel.transform, uiFont, squareSprite, "スタート (SPACE)", LIME_COLOR, BG_COLOR, new Vector2(0f, -195f), new Vector2(440f, 76f));

            // Rules Panel
            GameObject rulesPanel = CreatePanel("RulesPanel", canvasObj.transform, PANEL_OVERLAY_COLOR);
            CreateText("RulesTitle", rulesPanel.transform, uiFont, "真上を見逃すな", 56, TEXT_COLOR, new Vector2(0f, 350f), new Vector2(900f, 80f), FontStyle.Bold);
            CreateText("RulesLead", rulesPanel.transform, uiFont, "一周の終わりではなく、頂点を通過した瞬間にカウント（光ったら +1）", 26, MUTED_COLOR, new Vector2(0f, 285f), new Vector2(1200f, 50f));

            RuleDemoAnimator demoAnimator = rulesPanel.AddComponent<RuleDemoAnimator>();
            (RectTransform spinArm, Image spinBob) = CreateRuleDemoCard("DemoSpin", rulesPanel.transform, uiFont, circleSprite, squareSprite, new Vector2(-420f, 55f), "真上を通ったら数える", "頂点を通過した瞬間に+1");
            (RectTransform feintArm, Image _) = CreateRuleDemoCard("DemoFeint", rulesPanel.transform, uiFont, circleSprite, squareSprite, new Vector2(0f, 55f), "0回でも油断しない", "頂点の手前で反転するフェイント");
            (RectTransform reverseArm, Image reverseBob) = CreateRuleDemoCard("DemoReverse", rulesPanel.transform, uiFont, circleSprite, squareSprite, new Vector2(420f, 55f), "区間ごとに方向が変わる", "回数を終えたら真下を通って反転");
            demoAnimator.ConfigureDemos(spinArm, spinBob, feintArm, reverseArm, reverseBob);

            CreateText("RulesControls", rulesPanel.transform, uiFont, "[SPACE] +1    /    [CTRL] -1    /    各ウェーブ 10秒", 28, LIME_COLOR, new Vector2(0f, -200f), new Vector2(1000f, 50f), FontStyle.Bold);
            Button rulesStartBtn = CreateButton("RulesStartButton", rulesPanel.transform, uiFont, squareSprite, "ゲームスタート (SPACE)", LIME_COLOR, BG_COLOR, new Vector2(0f, -295f), new Vector2(440f, 76f));

            // Game Panel（中央の3D振り子アリーナを遮らない透明パネル）
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
            GameObject resultPanel = CreatePanel("ResultPanel", canvasObj.transform, PANEL_OVERLAY_COLOR);
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

        /// <summary> Cinemachine 3 のバーチャルカメラを生成し、指定座標から注視点を向くように配置する。 </summary>
        private static CinemachineCamera CreateCinemachineCamera(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 lookAtPoint,
            float fieldOfView,
            int initialPriority)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.LookAt(lookAtPoint);

            CinemachineCamera cmCam = go.AddComponent<CinemachineCamera>();
            cmCam.Lens.FieldOfView = fieldOfView;
            cmCam.Lens.NearClipPlane = 0.2f;
            cmCam.Lens.FarClipPlane = 80f;
            cmCam.Priority = new PrioritySettings
            {
                Enabled = true,
                Value = initialPriority,
            };

            return cmCam;
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
            GameObject card = CreateImage(name, parent, squareSprite, new Color(0.106f, 0.196f, 0.180f, 0.94f), anchoredPosition, new Vector2(380f, 340f));

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
