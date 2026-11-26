using System.IO;
using UnityEditor;
using UnityEngine;

namespace Taurus.Editor.UI
{
    /// <summary>
    ///     納品されたUI素材のスプライト範囲を、実際に描かれている（不透明な）領域だけに切り詰める。
    ///     素材は 1920x1080 の全画面に部品が置かれた形で納品されるため、余白ごと配置すると位置が決まらない。
    ///     本番素材に差し替えたら、もう一度このメニューを実行する。
    /// </summary>
    public static class UISpriteTrimTool
    {
        private const string UI_ASSET_FOLDER = "Assets/AssetStoreTools/UI";

        // これより薄い画素は余白として扱う（影のごく薄い部分を除くため）
        private const byte ALPHA_THRESHOLD = 16;

        /// <summary> UI素材フォルダの全テクスチャを切り詰める。 </summary>
        [MenuItem("Taurus/UI/UI素材のスプライト範囲を描画部分に切り詰める")]
        public static void TrimAll()
        {
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { UI_ASSET_FOLDER }))
            {
                if (TrimSprite(AssetDatabase.GUIDToAssetPath(guid)))
                {
                    count++;
                }
            }

            AssetDatabase.Refresh();
            Debug.Log($"[{nameof(UISpriteTrimTool)}] {count} 件のスプライト範囲を更新しました。");
        }

        /// <summary> 1 枚のテクスチャを、描画部分だけを範囲に持つ 1 つのスプライトにする。変更したら true。 </summary>
        public static bool TrimSprite(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return false;
            }

            // インポート後のテクスチャは縮小・圧縮されていることがあるため、元の PNG を直接読む
            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!source.LoadImage(File.ReadAllBytes(assetPath)))
                {
                    Debug.LogWarning($"[{nameof(UISpriteTrimTool)}] 画像を読めませんでした: {assetPath}");
                    return false;
                }

                if (!TryFindOpaqueRect(source, out RectInt rect))
                {
                    Debug.LogWarning($"[{nameof(UISpriteTrimTool)}] 不透明な画素がありません: {assetPath}");
                    return false;
                }

                string spriteName = Path.GetFileNameWithoutExtension(assetPath);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
#pragma warning disable CS0618 // spritesheet は非推奨だが、2D Sprite パッケージなしで範囲を設定できる唯一の手段
                importer.spritesheet = new[]
                {
                    new SpriteMetaData
                    {
                        name = spriteName,
                        rect = new Rect(rect.x, rect.y, rect.width, rect.height),
                        alignment = (int)SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                    },
                };
#pragma warning restore CS0618
                importer.SaveAndReimport();
                return true;
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        /// <summary> アルファが閾値を超える画素を囲む最小の矩形（左下原点）を求める。 </summary>
        private static bool TryFindOpaqueRect(Texture2D texture, out RectInt rect)
        {
            Color32[] pixels = texture.GetPixels32();
            int width = texture.width;
            int height = texture.height;
            int minX = width, minY = height, maxX = -1, maxY = -1;

            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (pixels[row + x].a <= ALPHA_THRESHOLD)
                    {
                        continue;
                    }

                    if (x < minX) { minX = x; }
                    if (x > maxX) { maxX = x; }
                    if (y < minY) { minY = y; }
                    if (y > maxY) { maxY = y; }
                }
            }

            if (maxX < 0)
            {
                rect = default;
                return false;
            }

            // GetPixels32 は左下原点なので、そのまま Sprite の rect に使える
            rect = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return true;
        }
    }
}
