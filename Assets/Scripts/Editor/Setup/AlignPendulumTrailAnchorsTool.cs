using Taurus.Runtime.Pendulum;
using UnityEditor;
using UnityEngine;

namespace Taurus.Editor.Setup
{
    /// <summary> PendulumModel_1〜3 の各玉メッシュ（SkinnedMeshRenderer）の実頂点中心を計算し、Trail（発光アンカー）の位置を正確に揃える。 </summary>
    public static class AlignPendulumTrailAnchorsTool
    {
        private static readonly string[] PREFAB_PATHS =
        {
            "Assets/Level/Prefabs/PendulumModel_1.prefab",
            "Assets/Level/Prefabs/PendulumModel_2.prefab",
            "Assets/Level/Prefabs/PendulumModel_3.prefab"
        };

        [MenuItem("Tools/Taurus/Align Pendulum Trail Anchors")]
        public static void AlignAll()
        {
            for (int i = 0; i < PREFAB_PATHS.Length; i++)
            {
                AlignPrefab(PREFAB_PATHS[i]);
            }

            AssetDatabase.SaveAssets();
        }

        private static void AlignPrefab(string prefabPath)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                Debug.LogError($"[{nameof(AlignPendulumTrailAnchorsTool)}] Prefab を読み込めません: {prefabPath}");
                return;
            }

            try
            {
                PendulumVisualFeedback feedback = root.GetComponent<PendulumVisualFeedback>();
                if (feedback == null)
                {
                    Debug.LogWarning($"[{nameof(AlignPendulumTrailAnchorsTool)}] PendulumVisualFeedback がありません: {prefabPath}");
                    return;
                }

                SerializedObject so = new SerializedObject(feedback);
                bool modified = false;
                modified |= AlignTier(prefabPath, "Parent", so, "_parentBobMeshRenderer", "_parentGlowAnchor");
                modified |= AlignTier(prefabPath, "Child", so, "_childBobMeshRenderer", "_childGlowAnchor");
                modified |= AlignTier(prefabPath, "Grandchild", so, "_grandchildBobMeshRenderer", "_grandchildGlowAnchor");

                if (modified)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool AlignTier(
            string prefabPath,
            string tierName,
            SerializedObject so,
            string rendererPropName,
            string anchorPropName)
        {
            Renderer renderer = so.FindProperty(rendererPropName)?.objectReferenceValue as Renderer;
            Transform anchor = so.FindProperty(anchorPropName)?.objectReferenceValue as Transform;
            if (renderer == null || anchor == null)
            {
                return false;
            }

            if (!TryComputeExactWorldCenter(renderer, out Vector3 worldCenter, out Vector3 worldSize, out Transform dominantBone))
            {
                Debug.LogWarning($"[{nameof(AlignPendulumTrailAnchorsTool)}] {prefabPath} ({tierName}) の中心を計算できませんでした。");
                return false;
            }

            string beforeParentName = anchor.parent != null ? anchor.parent.name : "null";
            Vector3 beforeWorld = anchor.position;
            Vector3 beforeLocal = anchor.localPosition;

            // 子アームや孫アーム配下に誤って置かれていると別段の回転で円運動してしまうため、
            // 該当玉メッシュを実際に駆動している最大ウェイトのボーン配下へ付け替える
            if (dominantBone != null && anchor.parent != dominantBone)
            {
                anchor.SetParent(dominantBone, true);
            }

            anchor.position = worldCenter;
            Vector3 afterLocal = anchor.localPosition;
            float movedDist = Vector3.Distance(beforeWorld, worldCenter);

            Debug.Log(
                $"[{nameof(AlignPendulumTrailAnchorsTool)}] {prefabPath} [{tierName}] " +
                $"anchor='{anchor.name}' parent='{beforeParentName}' -> '{(anchor.parent != null ? anchor.parent.name : "null")}' " +
                $"moved={movedDist:F4}m size=({worldSize.x:F3},{worldSize.y:F3},{worldSize.z:F3}) " +
                $"local: ({beforeLocal.x:F5},{beforeLocal.y:F5},{beforeLocal.z:F5}) -> ({afterLocal.x:F5},{afterLocal.y:F5},{afterLocal.z:F5})");
            return true;
        }

        // SkinnedMeshRenderer の全頂点をボーン行列×バインドポーズでワールド空間へスキニングし、
        // 厳密な幾何中心 (min+max)/2 と、その玉を最も強く駆動しているボーンを求める
        private static bool TryComputeExactWorldCenter(
            Renderer renderer,
            out Vector3 worldCenter,
            out Vector3 worldSize,
            out Transform dominantBone)
        {
            worldCenter = Vector3.zero;
            worldSize = Vector3.zero;
            dominantBone = null;

            if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                Mesh mesh = smr.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                BoneWeight[] weights = mesh.boneWeights;
                Matrix4x4[] bindposes = mesh.bindposes;
                Transform[] bones = smr.bones;

                if (vertices == null || vertices.Length == 0)
                {
                    return false;
                }

                bool canSkin = weights != null && weights.Length == vertices.Length
                    && bindposes != null && bones != null && bones.Length == bindposes.Length && bones.Length > 0;

                Matrix4x4[] skinMatrices = null;
                float[] boneWeightSums = null;
                if (canSkin)
                {
                    skinMatrices = new Matrix4x4[bones.Length];
                    boneWeightSums = new float[bones.Length];
                    for (int b = 0; b < bones.Length; b++)
                    {
                        skinMatrices[b] = bones[b] != null
                            ? bones[b].localToWorldMatrix * bindposes[b]
                            : smr.transform.localToWorldMatrix;
                    }
                }

                Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 v = vertices[i];
                    Vector3 wPos;
                    if (canSkin)
                    {
                        BoneWeight bw = weights[i];
                        wPos = Vector3.zero;
                        if (bw.weight0 > 0f && bw.boneIndex0 < skinMatrices.Length) { wPos += skinMatrices[bw.boneIndex0].MultiplyPoint3x4(v) * bw.weight0; boneWeightSums[bw.boneIndex0] += bw.weight0; }
                        if (bw.weight1 > 0f && bw.boneIndex1 < skinMatrices.Length) { wPos += skinMatrices[bw.boneIndex1].MultiplyPoint3x4(v) * bw.weight1; boneWeightSums[bw.boneIndex1] += bw.weight1; }
                        if (bw.weight2 > 0f && bw.boneIndex2 < skinMatrices.Length) { wPos += skinMatrices[bw.boneIndex2].MultiplyPoint3x4(v) * bw.weight2; boneWeightSums[bw.boneIndex2] += bw.weight2; }
                        if (bw.weight3 > 0f && bw.boneIndex3 < skinMatrices.Length) { wPos += skinMatrices[bw.boneIndex3].MultiplyPoint3x4(v) * bw.weight3; boneWeightSums[bw.boneIndex3] += bw.weight3; }
                    }
                    else
                    {
                        wPos = smr.transform.localToWorldMatrix.MultiplyPoint3x4(v);
                    }

                    min = Vector3.Min(min, wPos);
                    max = Vector3.Max(max, wPos);
                }

                if (canSkin)
                {
                    int bestIndex = -1;
                    float bestSum = 0f;
                    for (int b = 0; b < boneWeightSums.Length; b++)
                    {
                        if (bones[b] != null && boneWeightSums[b] > bestSum)
                        {
                            bestSum = boneWeightSums[b];
                            bestIndex = b;
                        }
                    }

                    if (bestIndex >= 0)
                    {
                        dominantBone = bones[bestIndex];
                    }
                }

                worldCenter = (min + max) * 0.5f;
                worldSize = max - min;
                return true;
            }

            Bounds bounds = renderer.bounds;
            worldCenter = bounds.center;
            worldSize = bounds.size;
            return true;
        }
    }
}
