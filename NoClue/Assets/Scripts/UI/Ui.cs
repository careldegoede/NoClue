using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace NoClue.UI
{
    /// <summary>Tiny helpers for building uGUI from code.</summary>
    public static class Ui
    {
        public static readonly Color Background = Hex("#16181E");
        public static readonly Color PanelColor = Hex("#232733");
        public static readonly Color PanelLight = Hex("#2E3342");
        public static readonly Color Accent = Hex("#E0B04A");
        public static readonly Color AccentDark = Hex("#8F6B22");
        public static readonly Color TextColor = Hex("#ECEDF2");
        public static readonly Color TextDim = Hex("#9AA0B0");
        public static readonly Color Danger = Hex("#C8504A");
        public static readonly Color Good = Hex("#4FA36B");
        public static readonly Color Blue = Hex("#4A7FC8");

        private static Font s_Font;
        public static Font Font => s_Font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

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
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
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
            var rt = Panel(parent, "Button " + label, color ?? Accent);
            var btn = rt.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(onClick);

            var text = Label(rt, label, fontSize, (color ?? Accent) == Accent ? Background : TextColor,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(text.rectTransform);
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
            var dim = Panel(parent, name, new Color(0, 0, 0, 0.72f));
            Stretch(dim);
            root = dim.gameObject;

            var box = Panel(dim, "Box", PanelColor);
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(width, height);
            return box;
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
