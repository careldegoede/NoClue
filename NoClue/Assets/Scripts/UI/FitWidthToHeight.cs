using UnityEngine;
using UnityEngine.UI;

namespace NoClue.UI
{
    /// <summary>
    /// Makes a layout child exactly as wide as N cards of the current height (capped by the space the
    /// parent leaves), so a card row looks the same on a wide PC screen and on a narrow phone.
    /// </summary>
    [RequireComponent(typeof(LayoutElement))]
    public sealed class FitWidthToHeight : MonoBehaviour
    {
        public float Aspect = 0.7f;        // card width / height
        public int Items = 3;
        public float ItemPadding = 12f;    // horizontal padding per item
        public float Reserved = 180f;      // parent width taken by siblings (arrows, spacing)

        private LayoutElement _le;
        private RectTransform _rt;

        private void Awake()
        {
            _le = GetComponent<LayoutElement>();
            _rt = (RectTransform)transform;
        }

        private void LateUpdate()
        {
            var parent = _rt.parent as RectTransform;
            if (parent == null) return;
            float want = Items * (_rt.rect.height * Aspect + ItemPadding);
            float max = Mathf.Max(100f, parent.rect.width - Reserved);
            float w = Mathf.Min(want, max);
            if (!Mathf.Approximately(_le.preferredWidth, w)) _le.preferredWidth = _le.minWidth = w;
        }
    }
}
