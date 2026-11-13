using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace NoClue.UI
{
    /// <summary>Tiny helpers for building uGUI from code, with the "spooky mansion" theme baked in.</summary>
    public static class Ui
    {
        // Spooky mansion palette: candle-lit near-black, aged parchment, old gold, blood red.
        public static readonly Color Background = Hex("#0C0A0E");
        public static readonly Color PanelColor = Hex("#17121A");
        public static readonly Color PanelLight = Hex("#261D27");
        public static readonly Color Accent = Hex("#C9A04A");
        public static readonly Color AccentDark = Hex("#7A5A22");
        public static readonly Color TextColor = Hex("#EADFC8");
        public static readonly Color TextDim = Hex("#9D917C");
        public static readonly Color Danger = Hex("#B3242B");
        public static readonly Color Good = Hex("#4F8A5B");
        public static readonly Color Blue = Hex("#4E6E9E");

        // Fonts (Resources/Fonts, SIL Open Font License). Big text uses the engraved title face.
        private const int TitleFontFrom = 34;
        private static Font s_Body, s_BodyBold, s_Title, s_Builtin;
        private static Font Builtin => s_Builtin ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        public static Font Font => s_Body ??= Resources.Load<Font>("Fonts/CrimsonText-Regular") ?? Builtin;
        public static Font BoldFont => s_BodyBold ??= Resources.Load<Font>("Fonts/CrimsonText-Bold") ?? Font;
        public static Font TitleFont => s_Title ??= Resources.Load<Font>("Fonts/CinzelDecorative-Bold") ?? BoldFont;
        private static Font s_Logo;
        public static Font LogoFont => s_Logo ??= Resources.Load<Font>("Fonts/UnifrakturMaguntia-Book") ?? TitleFont;

        /// <summary>The "No~Clue" wordmark in gothic blackletter: gold letters, blood-red tilde, red halo.</summary>
        public static Text Logo(Transform parent, int size)
        {
            var t = Label(parent, "No<color=#B3242B>~</color>Clue", size, Accent, TextAnchor.MiddleCenter);
            t.font = LogoFont;
            t.supportRichText = true;
            t.resizeTextMaxSize = size;
            t.fontSize = size;
            var glow = t.gameObject.AddComponent<Outline>();
            glow.effectColor = new Color(Danger.r, Danger.g, Danger.b, 0.55f);
            glow.effectDistance = new Vector2(3, 3);
            return t;
        }

        /// <summary>Played on every button / card tap (set by the app to its click sound).</summary>
        public static Action ClickSound;

        /// <summary>Adds the click sound to a button.</summary>
        public static void AddClick(Button b) => b.onClick.AddListener(() => ClickSound?.Invoke());

        /// <summary>Optional theme art from Resources/Theme (null until the art exists).</summary>
        public static Sprite ThemeSprite(string name) => Resources.Load<Sprite>("Theme/" + name);

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        // ------------------------------------------------------------ basic objects

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Panel(Transform parent, string name, Color color)
        {
            var rt = Rect(parent, name);
            rt.gameObject.AddComponent<Image>().color = color;
            return rt;
        }

        public static Text Label(Transform parent, string text, int size = 28, Color? color = null,
            TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(parent, "Label");
            var t = rt.gameObject.AddComponent<Text>();
            bool title = size >= TitleFontFrom;
            if (title)
            {
                // engraved display face; shrinks to fit so long headlines never spill
                t.font = TitleFont;
                t.fontStyle = FontStyle.Normal;
                t.fontSize = Mathf.RoundToInt(size * 0.9f);
                t.resizeTextForBestFit = true;
                t.resizeTextMaxSize = t.fontSize;
                t.resizeTextMinSize = Mathf.Max(14, Mathf.RoundToInt(size * 0.5f));
                var shadow = t.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0, 0, 0, 0.85f);
                shadow.effectDistance = new Vector2(2, -3);
            }
            else
            {
                // book serif is a little smaller than the old sans, so nudge it up
                bool bold = (style & FontStyle.Bold) != 0;
                t.font = bold ? BoldFont : Font;
                t.fontStyle = bold ? style & ~FontStyle.Bold : style;
                t.fontSize = Mathf.RoundToInt(size * 1.1f);
            }
            t.text = text;
            t.color = color ?? TextColor;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Image Image(Transform parent, Sprite sprite, string name = "Image")
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        public static Button Button(Transform parent, string label, UnityAction onClick, Color? color = null,
            int fontSize = 30, float width = 320, float height = 80)
        {
            var fill = color ?? Accent;
            var rt = Panel(parent, "Button " + label, fill);
            var img = rt.GetComponent<Image>();
            var art = ThemeSprite("button");
            if (art != null)
            {
                img.sprite = art;
                img.type = UnityEngine.UI.Image.Type.Sliced;
                img.color = fill == Accent ? Color.white : Color.Lerp(fill, Color.white, 0.25f);
            }
            var outline = rt.gameObject.AddComponent<Outline>();          // thin old-gold rim
            outline.effectColor = new Color(Accent.r, Accent.g, Accent.b, fill == Accent ? 0.0f : 0.55f);
            outline.effectDistance = new Vector2(2, 2);
            var drop = rt.gameObject.AddComponent<Shadow>();
            drop.effectColor = new Color(0, 0, 0, 0.6f);
            drop.effectDistance = new Vector2(3, -4);

            var btn = rt.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.15f, 1.1f, 1.05f);
            colors.pressedColor = new Color(0.75f, 0.7f, 0.7f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            btn.colors = colors;
            AddClick(btn);
            if (onClick != null) btn.onClick.AddListener(onClick);

            // Buttons use the engraved title face, sized to the button.
            var text = Label(rt, label, Mathf.Max(fontSize, TitleFontFrom), fill == Accent ? Background : TextColor,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            text.resizeTextMaxSize = Mathf.RoundToInt(fontSize * 0.95f);
            text.resizeTextMinSize = 12;
            if (fill == Accent && text.TryGetComponent<Shadow>(out var s)) s.effectColor = new Color(1, 1, 1, 0.25f);
            Stretch(text.rectTransform, 10, 4, 10, 4);
            Size(rt, width, height);
            return btn;
        }

        public static InputField Input(Transform parent, string placeholder, string value = "", float width = 520,
            float height = 76)
        {
            var rt = Panel(parent, "Input", PanelLight);
            Size(rt, width, height);
            var field = rt.gameObject.AddComponent<InputField>();

            var ph = Label(rt, placeholder, 30, TextDim, TextAnchor.MiddleLeft, FontStyle.Italic);
            Stretch(ph.rectTransform, 20, 0, 20, 0);
            var txt = Label(rt, "", 30, TextColor, TextAnchor.MiddleLeft);
            txt.raycastTarget = true;
            txt.supportRichText = false;
            Stretch(txt.rectTransform, 20, 0, 20, 0);

            field.textComponent = txt;
            field.placeholder = ph;
            field.text = value;
            return field;
        }

        // ------------------------------------------------------------ layout

        public static RectTransform Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static RectTransform Size(RectTransform rt, float width, float height)
        {
            var le = GetOrAdd<LayoutElement>(rt);
            if (width > 0) { le.preferredWidth = width; le.minWidth = width; }
            if (height > 0) { le.preferredHeight = height; le.minHeight = height; }
            rt.sizeDelta = new Vector2(width > 0 ? width : rt.sizeDelta.x, height > 0 ? height : rt.sizeDelta.y);
            return rt;
        }

        public static LayoutElement Flex(Component c, float flexW = 1, float flexH = 0)
        {
            var le = GetOrAdd<LayoutElement>(c);
            le.flexibleWidth = flexW;
            le.flexibleHeight = flexH;
            return le;
        }

        public static VerticalLayoutGroup VLayout(Component c, float spacing = 16, int padding = 0,
            TextAnchor align = TextAnchor.UpperCenter, bool expandWidth = true)
        {
            var g = c.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(padding, padding, padding, padding);
            g.childAlignment = align;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = expandWidth;
            g.childForceExpandHeight = false;
            return g;
        }

        public static HorizontalLayoutGroup HLayout(Component c, float spacing = 16, int padding = 0,
            TextAnchor align = TextAnchor.MiddleLeft)
        {
            var g = c.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(padding, padding, padding, padding);
            g.childAlignment = align;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = false;
            return g;
        }

        public static GridLayoutGroup Grid(Component c, Vector2 cell, float spacing = 14,
            TextAnchor align = TextAnchor.UpperLeft)
        {
            var g = c.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = cell;
            g.spacing = new Vector2(spacing, spacing);
            g.childAlignment = align;
            return g;
        }

        public static void FitHeight(Component c)
        {
            var f = c.gameObject.AddComponent<ContentSizeFitter>();
            f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        /// <summary>A vertical scroll area; returns the content rect to fill (it grows with its children).</summary>
        public static RectTransform ScrollArea(Transform parent, string name, out ScrollRect scroll)
        {
            var root = Rect(parent, name);
            scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 40;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = Rect(root, "Viewport");
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0); // catches drag/scroll

            var content = Rect(viewport, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;

            scroll.viewport = viewport;
            scroll.content = content;
            return content;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        /// <summary>Full-screen dimmer with a centered panel; returns the panel.</summary>
        public static RectTransform Modal(Transform parent, string name, float width, float height, out GameObject root)
        {
            var dim = Panel(parent, name, new Color(0.04f, 0, 0.01f, 0.8f));   // blood-black dimmer
            Stretch(dim);
            root = dim.gameObject;

            var box = Panel(dim, "Box", PanelColor);
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(width, height);
            StyleFrame(box);
            box.gameObject.AddComponent<PopIn>();
            return box;
        }

        /// <summary>Old-gold rim + deep shadow, or the painted frame art when it exists.</summary>
        public static void StyleFrame(RectTransform panel)
        {
            var img = panel.GetComponent<Image>();
            var art = ThemeSprite("panel_frame");
            if (art != null)
            {
                img.sprite = art;
                img.type = UnityEngine.UI.Image.Type.Sliced;
                img.color = Color.white;
            }
            else
            {
                var rim = panel.gameObject.AddComponent<Outline>();
                rim.effectColor = new Color(Accent.r, Accent.g, Accent.b, 0.8f);
                rim.effectDistance = new Vector2(3, 3);
            }
            var shadow = panel.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.75f);
            shadow.effectDistance = new Vector2(10, -14);
        }

        /// <summary>Never use "GetComponent() ?? AddComponent()": Unity's fake-null objects defeat "??".</summary>
        public static T GetOrAdd<T>(Component c) where T : Component =>
            c.TryGetComponent<T>(out var existing) ? existing : c.gameObject.AddComponent<T>();

        public static void SetButtonLabel(Button b, string text) => b.GetComponentInChildren<Text>().text = text;

        public static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

        public static void Safe(Action a)
        {
            try { a(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
