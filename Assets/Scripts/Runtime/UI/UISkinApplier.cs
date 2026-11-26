using UnityEngine;

namespace Taurus.Runtime.UI
{
    /// <summary>
    ///     子階層の UISkinImage に、設定したUIスキンのスプライトを反映する。
    ///     Canvas のルートに付け、本番素材へ入れ替えるときは _skin を差し替える。
    /// </summary>
    public class UISkinApplier : MonoBehaviour
    {
        [SerializeField] private UISkin _skin;

        /// <summary> 現在のスキン。 </summary>
        public UISkin Skin => _skin;

        /// <summary> スキンを設定して即座に反映する。 </summary>
        public void SetSkin(UISkin skin)
        {
            _skin = skin;
            ApplyAll();
        }

        /// <summary> 子階層（非アクティブを含む）のすべての UISkinImage に反映する。 </summary>
        public void ApplyAll()
        {
            if (_skin == null)
            {
                Debug.LogWarning($"[{nameof(UISkinApplier)}] UISkin が未設定です。", this);
                return;
            }

            foreach (UISkinImage image in GetComponentsInChildren<UISkinImage>(true))
            {
                image.Apply(_skin);
            }
        }

        private void Awake()
        {
            ApplyAll();
        }
    }
}
