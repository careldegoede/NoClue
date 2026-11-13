using NoClue.Core;
using NoClue.Net;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace NoClue.UI
{
    /// <summary>A card: art, name, an optional tag (Owned / shown-by portrait / Out) and selection state.</summary>
    public sealed class CardWidget
    {
        public readonly string CardId;
        public readonly RectTransform Root;
        public readonly Button Button;

        private readonly Image _frame;
        private readonly Image _dim;
        private readonly RectTransform _tag;
        private readonly Image _tagBg;
        private readonly Image _tagPortrait;
        private readonly Text _tagText;

        // private "suspect" mark (optional)
        private readonly Button _suspectButton;
        private readonly Image _suspectButtonBg;
        private readonly GameObject _suspectBadge;
        private bool _selected, _suspect;

        /// <param name="onToggleSuspect">If given, the card gets a small "?" corner button to mark it as a suspect.</param>
        public CardWidget(Transform parent, string cardId, UnityAction<string> onClick = null,
            UnityAction<string> onToggleSuspect = null)
        {
            CardId = cardId;

            Root = Ui.Panel(parent, "Card " + cardId, Ui.PanelLight);
            _frame = Root.GetComponent<Image>();
            if (onClick != null)
            {
                Button = Root.gameObject.AddComponent<Button>();
                Ui.AddClick(Button);
                Button.onClick.AddListener(() => onClick(cardId));
            }

            var art = Ui.Image(Root, CardCatalog.GetSprite(cardId), "Art");
            Ui.Stretch(art.rectTransform, 5, 5, 5, 5);

            // No name overlay: the card art already has the name printed on it.

            // tag in the top-left corner
            _tag = Ui.Panel(Root, "Tag", Ui.Accent);
            _tagBg = _tag.GetComponent<Image>();
            _tag.anchorMin = _tag.anchorMax = new Vector2(0, 1);
            _tag.pivot = new Vector2(0, 1);
            _tag.anchoredPosition = new Vector2(2, -2);
            _tag.sizeDelta = new Vector2(96, 34);
            _tagText = Ui.Label(_tag, "", 18, Ui.Background, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(_tagText.rectTransform);
            _tagPortrait = Ui.Image(_tag, null, "Portrait");
            _tagPortrait.preserveAspect = false;
            Ui.Stretch(_tagPortrait.rectTransform, 3, 3, 3, 3);
            _tag.gameObject.SetActive(false);

            _dim = Ui.Panel(Root, "Dim", new Color(0, 0, 0, 0.6f)).GetComponent<Image>();
            Ui.Stretch(_dim.rectTransform);
            _dim.raycastTarget = false;
            _dim.gameObject.SetActive(false);

            if (onToggleSuspect == null) return;

            // "SUSPECT" badge along the bottom
            var badge = Ui.Panel(Root, "Suspect", Ui.Danger);
            badge.anchorMin = new Vector2(0, 0);
            badge.anchorMax = new Vector2(1, 0);
            badge.pivot = new Vector2(0.5f, 0);
            badge.offsetMin = new Vector2(5, 5);
            badge.offsetMax = new Vector2(-5, 35);
            badge.GetComponent<Image>().raycastTarget = false;
            var badgeText = Ui.Label(badge, "SUSPECT", 18, Ui.TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(badgeText.rectTransform);
            _suspectBadge = badge.gameObject;
            _suspectBadge.SetActive(false);

            // "?" toggle in the bottom-right corner (its own button, so it never selects the card)
            var toggle = Ui.Panel(Root, "SuspectToggle", new Color(0, 0, 0, 0.7f));
            toggle.anchorMin = toggle.anchorMax = new Vector2(1, 0);
            toggle.pivot = new Vector2(1, 0);
            toggle.anchoredPosition = new Vector2(-4, 40);
            toggle.sizeDelta = new Vector2(44, 44);
            _suspectButtonBg = toggle.GetComponent<Image>();
            _suspectButton = toggle.gameObject.AddComponent<Button>();
            Ui.AddClick(_suspectButton);
            _suspectButton.onClick.AddListener(() => onToggleSuspect(cardId));
            var q = Ui.Label(toggle, "?", 30, Ui.TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(q.rectTransform);
        }

        /// <summary>Shows/hides the suspect toggle (hidden for cards that can't be the answer).</summary>
        public void SetSuspectAvailable(bool available)
        {
            if (_suspectButton == null) return;
            _suspectButton.gameObject.SetActive(available);
            if (!available) SetSuspect(false);
        }

        public void SetSuspect(bool suspect)
        {
            _suspect = suspect && _suspectButton != null;
            if (_suspectBadge != null) _suspectBadge.SetActive(_suspect);
            if (_suspectButtonBg != null) _suspectButtonBg.color = _suspect ? Ui.Danger : new Color(0, 0, 0, 0.7f);
            UpdateFrame();
        }

        private void UpdateFrame() =>
            _frame.color = _selected ? Ui.Accent : _suspect ? Ui.Danger : Ui.PanelLight;

        /// <summary>Owned → "Owned"; ShownBy → the shower's profile image; Out → "Out" plus their portrait.</summary>
        public void SetNote(CardNote note, PlayerView by)
        {
            if (note == null || note.kind == NoteKind.None)
            {
                _tag.gameObject.SetActive(false);
                return;
            }
            _tag.gameObject.SetActive(true);

            switch (note.kind)
            {
                case NoteKind.Owned:
                    _tagBg.color = Ui.Accent;
                    _tag.sizeDelta = new Vector2(96, 34);
                    _tagText.text = "Owned";
                    _tagText.color = Ui.Background;
                    _tagPortrait.gameObject.SetActive(false);
                    break;

                case NoteKind.ShownBy:
                    _tagBg.color = Ui.Danger;                 // red frame so revealed cards stand out
                    _tag.sizeDelta = new Vector2(60, 78);
                    _tagPortrait.rectTransform.offsetMin = new Vector2(5, 5);
                    _tagPortrait.rectTransform.offsetMax = new Vector2(-5, -5);
                    _tagText.text = "";
                    _tagPortrait.sprite = by != null ? CardCatalog.GetSprite(by.characterId) : null;
                    _tagPortrait.gameObject.SetActive(true);
                    break;

                case NoteKind.Out:
                    _tagBg.color = Ui.Danger;
                    _tag.sizeDelta = new Vector2(96, 34);
                    _tagText.text = by != null ? "Out: " + by.name : "Out";
                    _tagText.color = Ui.TextColor;
                    _tagText.resizeTextForBestFit = true;
                    _tagText.resizeTextMinSize = 10;
                    _tagText.resizeTextMaxSize = 18;
                    _tagPortrait.gameObject.SetActive(false);
                    break;
            }
        }

        public void SetSelected(bool selected)
        {
            _selected = selected;
            UpdateFrame();
        }

        public void SetDimmed(bool dimmed) => _dim.gameObject.SetActive(dimmed);

        public void SetInteractable(bool on)
        {
            if (Button != null) Button.interactable = on;
        }
    }
}
