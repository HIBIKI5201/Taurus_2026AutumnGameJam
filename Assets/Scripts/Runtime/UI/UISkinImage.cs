using UnityEngine;
using UnityEngine.UI;

namespace Taurus.Runtime.UI
{
    /// <summary> この Image にどのUIスキンのスロットを表示するかを指定する。実際の差し替えは UISkinApplier が行う。 </summary>
    [RequireComponent(typeof(Image))]
    public class UISkinImage : MonoBehaviour
    {
        [SerializeField] private UISkinSlot _slot;

        // スキンにスプライトがあるとき、Image の色を白にして素材の色をそのまま出す（仮UIの塗り色を消すため）
        [SerializeField] private bool _isResetColorWhenApplied = true;

        /// <summary> 表示するスロット。 </summary>
        public UISkinSlot Slot => _slot;

        /// <summary> スロットを設定する（主にエディタ構築ツール用）。 </summary>
        public void SetSlot(UISkinSlot slot)
        {
            _slot = slot;
        }

        /// <summary> スキンからスロットのスプライトを取り出して Image に反映する。スキンに無ければ何もしない。 </summary>
        public void Apply(UISkin skin)
        {
            if (skin == null || _slot == UISkinSlot.None || !skin.TryGetSprite(_slot, out Sprite sprite))
            {
                return;
            }

            Image image = GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            if (_isResetColorWhenApplied)
            {
                image.color = Color.white;
            }
        }
    }
}
