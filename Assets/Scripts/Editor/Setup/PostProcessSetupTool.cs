using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Taurus.Editor.Setup
{
    /// <summary>
    /// GameScene に Bloom・Tonemapping・Vignette 付きの Global Volume を配置し、
    /// Main Camera の Post Processing を有効化するエディタツール。
    /// </summary>
    public static class PostProcessSetupTool
    {
        private const string VOLUME_PROFILE_PATH = "Assets/Settings/GamePostProcessProfile.asset";
        private const string GAME_SCENE_PATH = "Assets/Scenes/GameScene.unity";
        private const string GLOBAL_VOLUME_NAME = "Global Volume";

        /// <summary> VolumeProfile を生成・更新し、GameScene へ Global Volume とカメラの Post Processing を設定する。 </summary>
        [MenuItem("Taurus/Setup Post Processing Volume")]
        public static void SetupPostProcessing()
        {
            VolumeProfile profile = EnsureVolumeProfile();

            Scene scene = EditorSceneManager.OpenScene(GAME_SCENE_PATH, OpenSceneMode.Single);

            // Main Camera の Post Processing を確実に有効化
            Camera mainCam = Object.FindFirstObjectByType<Camera>();
            if (mainCam != null)
            {
                UniversalAdditionalCameraData urpCamData = mainCam.GetComponent<UniversalAdditionalCameraData>();
                if (urpCamData == null)
                {
                    urpCamData = mainCam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                }

                urpCamData.renderPostProcessing = true;
                EditorUtility.SetDirty(urpCamData);
            }

            // 既存の Global Volume があれば流用し、無ければ新規作成
            Volume volume = Object.FindFirstObjectByType<Volume>();
            if (volume == null)
            {
                GameObject volumeObj = new GameObject(GLOBAL_VOLUME_NAME);
                volume = volumeObj.AddComponent<Volume>();
            }

            volume.isGlobal = true;
            volume.priority = 1f;
            volume.weight = 1f;
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            UnityEngine.Debug.Log($"[{nameof(PostProcessSetupTool)}] Global Volume（Bloom / Tonemapping / Vignette）と Main Camera の Post Processing を設定しました。");
        }

        /// <summary> VolumeProfile の Bloom / Tonemapping / Vignette をサブアセット込みで作り直して保存する（シーンは触らない）。 </summary>
        [MenuItem("Taurus/Repair Post Processing Profile")]
        public static void RepairVolumeProfile()
        {
            EnsureVolumeProfile();
            UnityEngine.Debug.Log($"[{nameof(PostProcessSetupTool)}] {VOLUME_PROFILE_PATH} の Bloom / Tonemapping / Vignette を保存し直しました。");
        }

        private static VolumeProfile EnsureVolumeProfile()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VOLUME_PROFILE_PATH);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VOLUME_PROFILE_PATH);
            }

            // 以前サブアセットとして保存されずに空参照（fileID: 0）になった要素を取り除く
            profile.components.RemoveAll(component => component == null);

            // Bloom（Emission HDR > 1.0 の発光玉を綺麗に光らせる）
            if (!profile.TryGet(out Bloom bloom))
            {
                bloom = profile.Add<Bloom>(true);
            }

            bloom.active = true;
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.85f);
            bloom.scatter.Override(0.68f);

            // Tonemapping（ACES で強い HDR 発光を自然に馴染ませる）
            if (!profile.TryGet(out Tonemapping tonemapping))
            {
                tonemapping = profile.Add<Tonemapping>(true);
            }

            tonemapping.active = true;
            tonemapping.mode.Override(TonemappingMode.ACES);

            // Vignette（画面端をわずかに締めて中央の振り子を引き立てる）
            if (!profile.TryGet(out Vignette vignette))
            {
                vignette = profile.Add<Vignette>(true);
            }

            vignette.active = true;
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.45f);

            // VolumeProfile.Add で作った要素はメモリ上にしか無いので、プロファイルのサブアセットとして埋め込まないと
            // 保存時に空参照になり、Bloom などが一切掛からなくなる
            EmbedAsSubAsset(bloom, profile);
            EmbedAsSubAsset(tonemapping, profile);
            EmbedAsSubAsset(vignette, profile);

            EditorUtility.SetDirty(bloom);
            EditorUtility.SetDirty(tonemapping);
            EditorUtility.SetDirty(vignette);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            return profile;
        }

        private static void EmbedAsSubAsset(VolumeComponent component, VolumeProfile profile)
        {
            if (AssetDatabase.Contains(component))
            {
                return;
            }

            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
        }
    }
}
