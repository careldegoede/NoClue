using UnityEngine;
using UnityEngine.UI;

namespace NoClue.UI
{
    /// <summary>
    /// NoClue is laid out for a portrait phone: 1080 units wide, at least 1920 tall.
    /// On a phone (tall screen) the UI fills the width and grows taller, staying clear of the notch /
    /// status bar (safe area). On a wide screen (the PC editor, a tablet) it becomes a centred
    /// 1080x1920 phone-shaped column, so desktop testing looks like the phone.
    /// </summary>
    public sealed class PortraitFrame : MonoBehaviour
    {
        public const float RefWidth = 1080f;
        public const float RefHeight = 1920f;

        private CanvasScaler _scaler;
        private RectTransform[] _frames;
        private Vector2Int _lastScreen;
        private Rect _lastSafe;

        public static void Attach(CanvasScaler scaler, params RectTransform[] frames)
        {
            var f = scaler.gameObject.AddComponent<PortraitFrame>();
            f._scaler = scaler;
            f._frames = frames;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
            f.Apply();
        }

        private void Update()
        {
            if (Screen.width != _lastScreen.x || Screen.height != _lastScreen.y || Screen.safeArea != _lastSafe) Apply();
        }

        private void Apply()
        {
            _lastScreen = new Vector2Int(Screen.width, Screen.height);
            _lastSafe = Screen.safeArea;
            if (Screen.width <= 0 || Screen.height <= 0) return;

            // tall enough for a 9:16 column? then fit the width, else fit the height
            bool tall = Screen.height / (float)Screen.width >= RefHeight / RefWidth - 0.001f;
            _scaler.matchWidthOrHeight = tall ? 0f : 1f;
            float scale = tall ? Screen.width / RefWidth : Screen.height / RefHeight;

            // safe area (notch, status bar, gesture bar) in canvas units
            var safe = Screen.safeArea;
            float left = safe.xMin / scale;
            float right = (Screen.width - safe.xMax) / scale;
            float bottom = safe.yMin / scale;
            float top = (Screen.height - safe.yMax) / scale;

            float canvasWidth = Screen.width / scale;
            float width = tall ? canvasWidth - left - right : Mathf.Min(RefWidth, canvasWidth);
            float shift = tall ? (left - right) * 0.5f : 0f;

            foreach (var rt in _frames)
            {
                rt.anchorMin = new Vector2(0.5f, 0);
                rt.anchorMax = new Vector2(0.5f, 1);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = new Vector2(-width * 0.5f + shift, bottom);
                rt.offsetMax = new Vector2(width * 0.5f + shift, -top);
            }
        }
    }
}
