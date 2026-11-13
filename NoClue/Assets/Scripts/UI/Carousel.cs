using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NoClue.UI
{
    /// <summary>
    /// One page at a time, phone-style: swipe/drag left and right and it snaps to the nearest page;
    /// a quick flick moves one page; the mouse wheel steps pages on desktop.
    /// Build with <see cref="Create"/>, then <see cref="AddPage"/> for each page.
    /// </summary>
    public sealed class Carousel : MonoBehaviour, IBeginDragHandler, IEndDragHandler, IScrollHandler
    {
        private const float FlickPixels = 60f;
        private ScrollRect _scroll;
        private RectTransform _content;
        private readonly List<LayoutElement> _pages = new();
        private bool _dragging;
        private float _lastWheel;

        public int Page { get; private set; }
        public int PageCount => _pages.Count;
        public event Action<int> PageChanged;

        public static Carousel Create(Transform parent, string name)
        {
            var root = Ui.Rect(parent, name);
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.inertia = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 0;   // the wheel steps whole pages instead (OnScroll)

            var viewport = Ui.Stretch(Ui.Rect(root, "Viewport"));
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);   // catches drags

            var content = Ui.Rect(viewport, "Content");
            content.anchorMin = new Vector2(0, 0);
            content.anchorMax = new Vector2(0, 1);
            content.pivot = new Vector2(0, 0.5f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var row = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;

            var c = root.gameObject.AddComponent<Carousel>();
            c._scroll = scroll;
            c._content = content;
            return c;
        }

        /// <summary>Adds a page (as wide as the carousel) and returns it to fill.</summary>
        public RectTransform AddPage(string name)
        {
            var page = Ui.Rect(_content, name);
            _pages.Add(page.gameObject.AddComponent<LayoutElement>());
            return page;
        }

        public void Next() => GoTo(Page + 1);
        public void Previous() => GoTo(Page - 1);

        public void GoTo(int page, bool instant = false)
        {
            page = Mathf.Clamp(page, 0, Mathf.Max(0, _pages.Count - 1));
            if (page != Page)
            {
                Page = page;
                PageChanged?.Invoke(page);
            }
            if (instant)
            {
                SizePages();
                Canvas.ForceUpdateCanvases();
                _scroll.horizontalNormalizedPosition = Target;
            }
        }

        private float Target => _pages.Count <= 1 ? 0 : (float)Page / (_pages.Count - 1);

        private void SizePages()
        {
            float w = _scroll.viewport.rect.width;
            foreach (var le in _pages)
                if (!Mathf.Approximately(le.preferredWidth, w)) le.minWidth = le.preferredWidth = w;
        }

        private void LateUpdate()
        {
            SizePages();   // pages always match the visible width (rotation / resize safe)
            if (_dragging || _pages.Count <= 1) return;
            float pos = _scroll.horizontalNormalizedPosition;
            float k = 1 - Mathf.Exp(-14f * Time.unscaledDeltaTime);   // smooth glide to the page
            _scroll.horizontalNormalizedPosition = Mathf.Abs(pos - Target) < 0.0005f ? Target : Mathf.Lerp(pos, Target, k);
        }

        public void OnBeginDrag(PointerEventData e) => _dragging = true;

        public void OnEndDrag(PointerEventData e)
        {
            _dragging = false;
            float dx = e.position.x - e.pressPosition.x;
            if (Mathf.Abs(dx) > FlickPixels) GoTo(Page + (dx < 0 ? 1 : -1));   // flick: one page
            else if (_pages.Count > 1)
                GoTo(Mathf.RoundToInt(_scroll.horizontalNormalizedPosition * (_pages.Count - 1)));
        }

        public void OnScroll(PointerEventData e)
        {
            if (Time.unscaledTime - _lastWheel < 0.2f) return;
            float d = Mathf.Abs(e.scrollDelta.x) > Mathf.Abs(e.scrollDelta.y) ? e.scrollDelta.x : -e.scrollDelta.y;
            if (Mathf.Abs(d) < 0.01f) return;
            _lastWheel = Time.unscaledTime;
            GoTo(Page + (d > 0 ? 1 : -1));
        }
    }
}
