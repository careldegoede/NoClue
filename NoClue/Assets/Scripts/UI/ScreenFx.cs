using UnityEngine;
using UnityEngine.UI;

namespace NoClue.UI
{
    /// <summary>
    /// Full-screen atmosphere for the spooky mansion theme, built from generated textures (no art needed):
    /// drifting fog behind the UI and a candle-flicker vignette in front of it,
    /// plus <see cref="Flash"/> and <see cref="Shake"/> for dramatic moments.
    /// </summary>
    public sealed class ScreenFx : MonoBehaviour
    {
        public static ScreenFx Instance { get; private set; }

        private RawImage _fog;
        private Image _vignette, _flash;
        private RectTransform _shakeTarget;
        private float _flashStart = -1, _flashSeconds;
        private Color _flashColor;
        private float _shakeUntil = -1, _shakeStrength;

        /// <summary>Creates the layers: fog right after <paramref name="background"/>, the rest at the top.</summary>
        public static ScreenFx Create(Transform canvas, RectTransform background, RectTransform shakeTarget)
        {
            var fx = canvas.gameObject.AddComponent<ScreenFx>();
            Instance = fx;
            fx._shakeTarget = shakeTarget;

            fx._fog = Layer<RawImage>(canvas, "Fog");
            fx._fog.texture = FogTexture(256);
            // (alphas look much stronger than their numbers in the project's linear colour space)
            fx._fog.color = new Color(0.75f, 0.68f, 0.72f, 0.018f);
            fx._fog.uvRect = new Rect(0, 0, 1.6f, 0.9f);
            fx._fog.transform.SetSiblingIndex(background.GetSiblingIndex() + 1);

            fx._vignette = Layer<Image>(canvas, "Vignette");
            fx._vignette.sprite = VignetteSprite(256);
            fx._vignette.color = new Color(0.02f, 0.0f, 0.01f, 0.9f);

            fx._flash = Layer<Image>(canvas, "Flash");
            fx._flash.color = new Color(0, 0, 0, 0);
            return fx;
        }

        /// <summary>A quick full-screen colour pulse (e.g. blood red when someone is knocked out).</summary>
        public void Flash(Color color, float seconds = 0.6f)
        {
            _flashColor = color;
            _flashSeconds = seconds;
            _flashStart = Time.unscaledTime;
        }

        /// <summary>Shakes the screen content for a moment.</summary>
        public void Shake(float strength = 14f, float seconds = 0.35f)
        {
            _shakeStrength = strength;
            _shakeUntil = Time.unscaledTime + seconds;
        }

        private void Update()
        {
            float now = Time.unscaledTime;

            // candle flicker: the darkness at the edges breathes slightly, with the odd sharper dip
            float flicker = Mathf.PerlinNoise(now * 1.7f, 0.3f) * 0.12f + Mathf.PerlinNoise(now * 9f, 4.1f) * 0.05f;
            var v = _vignette.color;
            v.a = 0.8f + flicker;
            _vignette.color = v;

            // fog drifts slowly sideways
            var fr = _fog.uvRect;
            fr.x = now * 0.008f;
            fr.y = Mathf.Sin(now * 0.05f) * 0.05f;
            _fog.uvRect = fr;

            if (_flashStart >= 0)
            {
                float t = (now - _flashStart) / _flashSeconds;
                var c = _flashColor;
                c.a = t >= 1 ? 0 : _flashColor.a * (1 - t) * (1 - t);
                _flash.color = c;
                if (t >= 1) _flashStart = -1;
            }

            if (_shakeTarget != null)
            {
                if (now < _shakeUntil)
                {
                    float k = (_shakeUntil - now) / 0.35f;
                    _shakeTarget.anchoredPosition = Random.insideUnitCircle * _shakeStrength * Mathf.Clamp01(k);
                }
                else if (_shakeTarget.anchoredPosition != Vector2.zero)
                {
                    _shakeTarget.anchoredPosition = Vector2.zero;
                }
            }
        }

        // ------------------------------------------------------------------ generated textures

        private static T Layer<T>(Transform canvas, string name) where T : Graphic
        {
            var rt = Ui.Stretch(Ui.Rect(canvas, name));
            var g = rt.gameObject.AddComponent<T>();
            g.raycastTarget = false;
            return g;
        }

        private static Sprite VignetteSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f, dy = (y + 0.5f) / size - 0.5f;
                float d = Mathf.Sqrt(dx * dx * 1.0f + dy * dy * 1.25f) / 0.7071f;   // a bit taller than wide
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1.05f, d));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        /// <summary>Seamless soft noise (tiles in both directions) for drifting fog.</summary>
        private static Texture2D FogTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[size * size];
            const float period = 4f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float n = 0, amp = 1, total = 0;
                for (int o = 0; o < 4; o++)
                {
                    float f = period * (1 << o);
                    // blend 4 offset samples so the edges wrap without a seam
                    float a = Mathf.PerlinNoise(u * f, v * f);
                    float b = Mathf.PerlinNoise((u - 1) * f + 100, v * f);
                    float c = Mathf.PerlinNoise(u * f, (v - 1) * f + 100);
                    float d = Mathf.PerlinNoise((u - 1) * f + 100, (v - 1) * f + 100);
                    n += amp * (a * (1 - u) * (1 - v) + b * u * (1 - v) + c * (1 - u) * v + d * u * v);
                    total += amp;
                    amp *= 0.5f;
                }
                n /= total;
                byte alpha = (byte)(Mathf.Clamp01((n - 0.35f) * 2.2f) * 255);
                px[y * size + x] = new Color32(255, 255, 255, alpha);
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

    }
}
