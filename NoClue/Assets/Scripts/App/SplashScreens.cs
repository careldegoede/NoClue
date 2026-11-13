using NoClue.Net;
using NoClue.UI;
using UnityEngine;
using UnityEngine.UI;

namespace NoClue.App
{
    /// <summary>
    /// Opening splash: the mansion fills the screen with a slow zoom, the title fades in, lightning
    /// flickers across, then it fades to the menu (or straight away on a click).
    /// </summary>
    public sealed class SplashScreen : NoClueApp.IScreen
    {
        private const float AutoContinueSeconds = 6.5f;
        private const float FadeSeconds = 0.8f;

        private readonly NoClueApp _app;
        private readonly RectTransform _art;
        private readonly CanvasGroup _group;
        private readonly CanvasGroup _titleGroup;
        private readonly float _start;
        private float _leaveAt = -1;
        private readonly float[] _lightning = { 1.1f, 1.35f, 3.9f };
        private int _nextBolt;

        public GameObject Root { get; }

        public SplashScreen(NoClueApp app, Transform parent)
        {
            _app = app;
            _start = Time.unscaledTime;

            var root = Ui.Stretch(Ui.Panel(parent, "Splash", Color.black));
            Root = root.gameObject;
            _group = Root.AddComponent<CanvasGroup>();

            _art = FullScreenArt(root, "mansion");

            // title + hint
            var titleBox = Ui.Rect(root, "Title");
            titleBox.anchorMin = new Vector2(0, 0);
            titleBox.anchorMax = new Vector2(1, 0.32f);
            titleBox.offsetMin = titleBox.offsetMax = Vector2.zero;
            _titleGroup = titleBox.gameObject.AddComponent<CanvasGroup>();
            _titleGroup.alpha = 0;
            Ui.VLayout(titleBox, 4, 0, TextAnchor.MiddleCenter);

            var logo = Ui.ThemeSprite("logo");
            if (logo != null)
            {
                Ui.Size(Ui.Image(titleBox, logo, "Logo").rectTransform, -1, 200);
            }
            else
            {
                Ui.Size(Ui.Logo(titleBox, 170).rectTransform, -1, 200);
            }
            Ui.Size(Ui.Label(titleBox, "Tap anywhere to enter", 26, Ui.TextDim, TextAnchor.MiddleCenter,
                FontStyle.Italic).rectTransform, -1, 40);

            // the whole splash is one big "continue" button
            var click = root.gameObject.AddComponent<Button>();
            click.transition = Selectable.Transition.None;
            click.onClick.AddListener(Continue);
        }

        /// <summary>A full-screen image that covers the screen (cropping as needed), or null if missing.</summary>
        public static RectTransform FullScreenArt(Transform parent, string themeSprite)
        {
            var sprite = Ui.ThemeSprite(themeSprite);
            if (sprite == null) return null;
            var img = Ui.Image(parent, sprite, "Art");
            img.preserveAspect = false;
            var fitter = img.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            return img.rectTransform;
        }

        private void Continue()
        {
            if (_leaveAt < 0) _leaveAt = Time.unscaledTime;
        }

        public void Tick()
        {
            float t = Time.unscaledTime - _start;

            // slow cinematic push-in on the mansion
            if (_art != null) _art.localScale = Vector3.one * (1.04f + t * 0.012f);

            _titleGroup.alpha = Mathf.Clamp01((t - 1.6f) / 1.2f);

            if (_nextBolt < _lightning.Length && t >= _lightning[_nextBolt])
            {
                ScreenFx.Instance?.Flash(new Color(0.8f, 0.85f, 1f, _nextBolt == 1 ? 0.35f : 0.5f), 0.35f);
                if (_nextBolt == 0) _app.PlaySfx("thunder11");   // one rumble for the opening strike
                _nextBolt++;
            }

            if (_leaveAt < 0 && t >= AutoContinueSeconds) _leaveAt = Time.unscaledTime;
            if (_leaveAt < 0) return;

            float f = (Time.unscaledTime - _leaveAt) / FadeSeconds;
            _group.alpha = 1 - Mathf.Clamp01(f);
            if (f >= 1) _app.ShowMenu();
        }

        public void Refresh(GameView view) { }
        public void Dispose() { }
    }

    /// <summary>
    /// A few seconds of the mansion over everything when a game starts, then it fades away.
    /// Lives on the overlay layer so the game screen builds underneath it.
    /// </summary>
    public sealed class GameStartCurtain : MonoBehaviour
    {
        private const float HoldSeconds = 2.4f;
        private const float FadeSeconds = 0.9f;
        private float _start;
        private CanvasGroup _group;
        private RectTransform _art;

        public static void Show(Transform overlay, System.Action<string> playSfx = null)
        {
            playSfx?.Invoke("thunder11");
            var root = Ui.Stretch(Ui.Panel(overlay, "GameStartCurtain", Color.black));
            root.SetAsFirstSibling();   // under toasts
            var curtain = root.gameObject.AddComponent<GameStartCurtain>();
            curtain._group = root.gameObject.AddComponent<CanvasGroup>();
            curtain._group.blocksRaycasts = true;   // no stray clicks while it's up
            curtain._art = SplashScreen.FullScreenArt(root, "mansion");

            var text = Ui.Label(root, "The doors close behind you...", 54, Ui.TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            text.rectTransform.anchorMin = new Vector2(0, 0.06f);
            text.rectTransform.anchorMax = new Vector2(1, 0.22f);
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;

            ScreenFx.Instance?.Flash(new Color(0.8f, 0.85f, 1f, 0.45f), 0.4f);
        }

        private void Start() => _start = Time.unscaledTime;

        private void Update()
        {
            float t = Time.unscaledTime - _start;
            if (_art != null) _art.localScale = Vector3.one * (1.06f + t * 0.02f);
            float f = (t - HoldSeconds) / FadeSeconds;
            _group.alpha = 1 - Mathf.Clamp01(f);
            if (f > 0.3f) _group.blocksRaycasts = false;
            if (f >= 1) Destroy(gameObject);
        }
    }
}
