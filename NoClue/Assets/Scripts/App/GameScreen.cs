using System.Collections.Generic;
using System.Linq;
using NoClue.Core;
using NoClue.Net;
using NoClue.UI;
using UnityEngine;
using UnityEngine.UI;

namespace NoClue.App
{
    /// <summary>
    /// The table, laid out phone-first: player strip, banner, three swipeable card rows
    /// (characters / rooms / weapons, 3 cards in view) and an action bar along the bottom.
    /// Pop-ups handle questions, reveals, the die, the log, host decisions and game over.
    /// Everything is redrawn from the latest <see cref="GameView"/>.
    /// </summary>
    public sealed class GameScreen : NoClueApp.IScreen
    {
        private const float CardAspect = 0.7f;   // card images range from 2:3 to 3:4

        private readonly NoClueApp _app;
        private readonly Transform _root;
        private readonly RectTransform _playerStrip;
        private readonly RectTransform _banner;
        private readonly Dictionary<string, CardWidget> _cards = new();
        private readonly Dictionary<CardType, Carousel> _rows = new();

        // bottom action bar
        private readonly Text _picksLabel;
        private readonly Button _spin;
        private readonly Button _accuse;

        // log pop-up (opened from the action bar)
        private GameObject _logModal;
        private Text _logText;
        private ScrollRect _logScroll;

        // host: "X disconnected - wait or eject?" pop-up
        private GameObject _hostModal;
        private int _hostModalFor = -1;

        private GameView _view;
        private readonly Dictionary<CardType, string> _picked = new();

        // atmosphere: pulsing "your turn" text, and flashes/shakes for big moments in the log
        private Text _pulseText;
        private string[] _prevLog;

        // private notebook: cards I've marked as suspects (saved on this device per game + seat)
        private readonly HashSet<string> _suspects = new();
        private string _suspectPrefKey;
        private GameObject _accuseModal;
        private readonly HashSet<int> _waitingOn = new();   // disconnected players the host chose to wait for

        private GameObject _answerModal;
        private string _answerKey;
        private GameObject _shownModal;
        private GameObject _nobodyModal;
        private GameObject _gameOverModal;
        private bool _gameOverDismissed;
        private Text _gameOverTitle;
        private string _gameOverWinnerText;
        private RectTransform _gameOverContent;
        private float _gameOverRevealAt = -1;

        // the die: everyone watches each throw before any pop-up that follows from it
        private DieView _die;
        private GameObject _rollModal;
        private Text _rollResult;
        private bool _rolling;
        private float _rollCloseAt = -1;
        private int _seenRollSeq = -1;

        private GameObject _revealChoiceModal;
        private string _revealChoiceKey;
        private GameObject _publicRevealModal;
        private int _seenPublicRevealSeq = -1;

        public GameObject Root { get; }

        public GameScreen(NoClueApp app, Transform parent)
        {
            _app = app;
            var root = Ui.Stretch(Ui.Rect(parent, "Game"));
            Root = root.gameObject;
            _root = root;

            // ---- top: players
            _playerStrip = Ui.Panel(root, "Players", Ui.PanelColor);
            _playerStrip.anchorMin = new Vector2(0, 1);
            _playerStrip.anchorMax = new Vector2(1, 1);
            _playerStrip.pivot = new Vector2(0.5f, 1);
            _playerStrip.offsetMin = new Vector2(0, -190);
            _playerStrip.offsetMax = Vector2.zero;
            Ui.HLayout(_playerStrip, 14, 12, TextAnchor.MiddleCenter);

            // ---- banner
            _banner = Ui.Panel(root, "Banner", Ui.Background);
            _banner.anchorMin = new Vector2(0, 1);
            _banner.anchorMax = new Vector2(1, 1);
            _banner.pivot = new Vector2(0.5f, 1);
            _banner.offsetMin = new Vector2(20, -290);
            _banner.offsetMax = new Vector2(-20, -196);
            Ui.HLayout(_banner, 14, 6, TextAnchor.MiddleCenter);

            // ---- bottom: action bar
            var bar = Ui.Panel(root, "ActionBar", Ui.PanelColor);
            bar.anchorMin = new Vector2(0, 0);
            bar.anchorMax = new Vector2(1, 0);
            bar.pivot = new Vector2(0.5f, 0);
            bar.offsetMin = new Vector2(0, 0);
            bar.offsetMax = new Vector2(0, 124);
            Ui.HLayout(bar, 16, 16, TextAnchor.MiddleCenter);
            _picksLabel = Ui.Label(bar, "", 26, Ui.TextColor, TextAnchor.MiddleLeft);
            _picksLabel.supportRichText = true;
            Ui.Flex(_picksLabel, 1);
            _spin = Ui.Button(bar, "Spin the die!", () => Send(MsgType.Roll), width: 330, height: 92, fontSize: 32);
            _accuse = Ui.Button(bar, "Accuse", ShowAccuseConfirm, Ui.Danger, width: 250, height: 92, fontSize: 32);
            Ui.Button(bar, "Log", ToggleLog, Ui.PanelLight, width: 150, height: 92, fontSize: 28);
            Ui.Button(bar, "Leave", app.Leave, Ui.PanelLight, width: 170, height: 92, fontSize: 28);

            // ---- middle: three swipeable card rows, 3 cards in view each
            var rows = Ui.Rect(root, "Rows");
            rows.anchorMin = Vector2.zero;
            rows.anchorMax = Vector2.one;
            rows.offsetMin = new Vector2(12, 132);
            rows.offsetMax = new Vector2(-12, -296);
            var v = Ui.VLayout(rows, 6);
            v.childForceExpandHeight = true;
            BuildRow(rows, "Characters", CardType.Character);
            BuildRow(rows, "Rooms", CardType.Room);
            BuildRow(rows, "Weapons", CardType.Weapon);
        }

        private void BuildRow(RectTransform parent, string title, CardType type)
        {
            var row = Ui.Rect(parent, title);
            Ui.Flex(row, 1, 1);
            Ui.VLayout(row, 0, 0, TextAnchor.UpperCenter);
            Ui.Size(Ui.Label(row, title, 24, Ui.Accent, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform, -1, 30);

            var strip = Ui.Rect(row, "Strip");
            Ui.Flex(strip, 1, 1);
            Ui.HLayout(strip, 10, 0, TextAnchor.MiddleCenter);

            var holder = Ui.Rect(strip, "Cards");
            var holderLe = holder.gameObject.AddComponent<LayoutElement>();
            holderLe.flexibleHeight = 1;   // fill the row's height
            var fit = holder.gameObject.AddComponent<FitWidthToHeight>();
            fit.Aspect = CardAspect;
            fit.Items = 3;
            fit.Reserved = 2 * 76 + 2 * 10;

            var carousel = Carousel.Create(holder, title, itemsPerView: 3);
            Ui.Stretch((RectTransform)carousel.transform);
            _rows[type] = carousel;

            // arrows either side (siblings in the strip; created after so they sit left/right)
            var left = Ui.Button(strip, "«", carousel.Previous, new Color(0, 0, 0, 0.55f), 40, 76, 110);
            left.transform.SetAsFirstSibling();
            Ui.Button(strip, "»", carousel.Next, new Color(0, 0, 0, 0.55f), 40, 76, 110);

            foreach (var id in CardCatalog.OfType(type))
            {
                var page = carousel.AddPage(id);
                var inner = Ui.Stretch(Ui.Rect(page, "Slot"), 6, 4, 6, 4);
                var card = new CardWidget(inner, id, OnCardClicked, ToggleSuspect);
                var aspect = card.Root.gameObject.AddComponent<AspectRatioFitter>();
                aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                aspect.aspectRatio = CardAspect;
                _cards[id] = card;
            }
        }

        // ------------------------------------------------------------------ refresh

        public void Refresh(GameView view)
        {
            _view = view;
            if (!IsMyTurnAndFree())
            {
                _picked.Clear();
                if (_accuseModal != null) Object.Destroy(_accuseModal);
            }
            _waitingOn.RemoveWhere(id => Player(id) is { connected: true });
            LoadSuspects();
            ReactToNewLogLines();

            // first view (or after a rejoin): don't replay old throws and reveals
            if (_seenRollSeq < 0) _seenRollSeq = view.rollSeq;
            if (_seenPublicRevealSeq < 0) _seenPublicRevealSeq = view.publicRevealSeq;
            if (view.rollSeq > _seenRollSeq)
            {
                _seenRollSeq = view.rollSeq;
                StartRollAnimation();
            }

            RefreshPlayers();
            RefreshBanner();
            RefreshCards();
            RefreshActionBar();
            RefreshLog();

            if (_rolling) return;   // pop-ups wait until the die has landed
            RefreshHostPrompt();
            RefreshAnswerModal();
            RefreshRevealChoiceModal();
            RefreshShownModal();
            RefreshNobodyModal();
            RefreshPublicReveal();
            RefreshGameOver();
        }

        private void RefreshPlayers()
        {
            Ui.Clear(_playerStrip);
            string arrow = _view.direction >= 0 ? "»" : "«";   // play direction between the chips
            bool first = true;
            foreach (var p in _view.players)
            {
                if (!first)
                {
                    var a = Ui.Label(_playerStrip, arrow, 44, _view.direction >= 0 ? Ui.TextDim : Ui.Danger,
                        TextAnchor.MiddleCenter, FontStyle.Bold);
                    Ui.Size(a.rectTransform, 28, 166);
                }
                first = false;

                bool active = p.id == _view.activePlayerId;
                bool asked = _view.ask.active && p.id == _view.ask.responderId;
                var chip = Ui.Panel(_playerStrip, p.name, active ? Ui.Accent : asked ? Ui.Blue : Ui.PanelLight);
                Ui.Size(chip, 170, 166);

                var img = Ui.Image(chip, CardCatalog.GetSprite(p.characterId), "Portrait");
                var irt = img.rectTransform;
                irt.anchorMin = new Vector2(0.5f, 1);
                irt.anchorMax = new Vector2(0.5f, 1);
                irt.pivot = new Vector2(0.5f, 1);
                irt.anchoredPosition = new Vector2(0, -6);
                irt.sizeDelta = new Vector2(66, 88);
                if (!p.connected || p.eliminated || p.ejected) img.color = new Color(0.4f, 0.4f, 0.4f);

                // host: tap a dropped player's chip to bring back the wait / eject choice
                if (Me is { isHost: true } && !p.connected && !p.ejected && !p.eliminated && _view.phase == Phase.Playing)
                {
                    int dropId = p.id;
                    var tap = chip.gameObject.AddComponent<Button>();
                    Ui.AddClick(tap);
                    tap.onClick.AddListener(() => { _waitingOn.Remove(dropId); RefreshHostPrompt(); });
                }

                string status = p.ejected ? "removed" : p.eliminated ? "OUT" : !p.connected ? "offline" :
                    active ? "turn" : asked ? "answering" : $"{p.handCount} cards";
                string me = p.id == _view.myPlayerId ? " (you)" : "";
                var label = Ui.Label(chip, $"{p.name}{me}\n<size=17>{status}</size>", 19,
                    active ? Ui.Background : Ui.TextColor, TextAnchor.UpperCenter, FontStyle.Bold);
                label.supportRichText = true;
                label.resizeTextForBestFit = true;   // long names shrink instead of spilling over the portrait
                label.resizeTextMinSize = 12;
                label.resizeTextMaxSize = 19;
                label.verticalOverflow = VerticalWrapMode.Truncate;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                Ui.Stretch(label.rectTransform, 6, 98, 6, 4);
            }
        }

        /// <summary>Red flash + shake when someone is knocked out, gold flash on a win, etc.</summary>
        private void ReactToNewLogLines()
        {
            var log = _view.log;
            var prev = _prevLog;
            _prevLog = log;
            if (prev == null || prev.Length == 0 || ScreenFx.Instance == null) return;   // first view: no replay

            int from = System.Array.LastIndexOf(log, prev[prev.Length - 1]) + 1;
            if (from <= 0) return;   // can't line the logs up (e.g. after a rejoin): skip rather than replay
            var fx = ScreenFx.Instance;
            for (int i = from; i < log.Length; i++)
            {
                string line = log[i];
                if (line.Contains("is out!"))   // wrong accusation
                {
                    fx.Flash(new Color(0.7f, 0.02f, 0.05f, 0.55f), 0.9f);
                    fx.Shake(18f);
                    _app.PlaySfx("loser-noooh");
                }
                else if (line.Contains("CORRECT")) fx.Flash(new Color(1f, 0.8f, 0.3f, 0.45f), 1.2f);
                else if (line.Contains("reversed")) { fx.Flash(new Color(0.7f, 0.02f, 0.05f, 0.3f), 0.5f); fx.Shake(8f); }
                else if (line.Contains("Revealed to everyone") || line.Contains("Nobody else holds"))
                    fx.Flash(new Color(0.4f, 0.55f, 0.9f, 0.3f), 0.6f);
            }
        }

        private void RefreshBanner()
        {
            Ui.Clear(_banner);
            _pulseText = null;
            var me = Me;
            var ask = _view.ask;

            if (_view.phase == Phase.GameOver)
            {
                var winner = Player(_view.winnerId);
                BannerText(winner != null ? $"Game over. {winner.name} solved the mystery!" : "Game over. Nobody solved it.", Ui.Accent);
                return;
            }

            if (ask.active)
            {
                var asker = Player(ask.askerId);
                var responder = Player(ask.responderId);
                BannerPortrait(asker);
                BannerText($"{Name(asker)} asked", Ui.TextColor);
                BannerPortrait(responder);
                string waiting = responder is { connected: false } ? "  (offline, waiting...)" : "";
                BannerText($"{Name(responder)}:  {CardCatalog.DisplayName(ask.characterId)}, in the " +
                           $"{CardCatalog.DisplayName(ask.roomId)}, with the {CardCatalog.DisplayName(ask.weaponId)}{waiting}",
                    Ui.TextColor);
                return;
            }

            if (_view.revealChoice.active)
            {
                var roller = Player(_view.revealChoice.askerId);
                if (roller != null && roller.id == _view.myPlayerId)
                    BannerText("SHOW ALL! Pick one of your 3 cards to reveal to everyone.", Ui.Blue);
                else
                {
                    BannerPortrait(roller);
                    BannerText($"{Name(roller)} rolled SHOW ALL and is picking a card to reveal...", Ui.Blue);
                }
                return;
            }

            if (_view.awaitingAckFrom >= 0)
            {
                var looker = Player(_view.awaitingAckFrom);
                bool mine = looker != null && looker.id == _view.myPlayerId;
                string offline = looker is { connected: false } ? " (offline, waiting...)" : "";
                string text = _view.ackKind switch
                {
                    AckKind.PublicReveal => mine ? "Everyone has seen the reveal. Click OK to continue."
                        : $"Waiting for {Name(looker)} to continue...{offline}",
                    AckKind.NobodyShowed => mine ? "Nobody could show you a card! Think about it, then click OK."
                        : $"Nobody could show {Name(looker)} a card. {Name(looker)} is thinking...{offline}",
                    _ => mine ? "Look at the card you were shown, then click OK."
                        : $"{Name(looker)} is looking at the card they were shown...{offline}",
                };
                if (!mine) BannerPortrait(looker);
                BannerText(text, mine ? Ui.Accent : Ui.TextDim);
                return;
            }

            var active = Player(_view.activePlayerId);
            string prefix = me != null && (me.eliminated || me.ejected) ? "You're out, watching.  " : "";
            if (active != null && active.id == _view.myPlayerId)
                _pulseText = BannerText("Your turn! Pick 3 cards, then spin the die or make an accusation.", Ui.Accent);
            else if (active != null)
            {
                BannerPortrait(active);
                string offline = active.connected ? "" : " (offline, waiting...)";
                BannerText($"{prefix}Waiting for {active.name}{offline}", Ui.TextDim);
            }
        }

        private void BannerPortrait(PlayerView p)
        {
            var img = Ui.Image(_banner, CardCatalog.GetSprite(p?.characterId), "Portrait");
            Ui.Size(img.rectTransform, 60, 80);
        }

        private Text BannerText(string text, Color color)
        {
            var t = Ui.Label(_banner, text, 30, color, TextAnchor.MiddleLeft, FontStyle.Bold);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var fitter = t.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var shadow = t.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.8f);
            shadow.effectDistance = new Vector2(2, -2);
            return t;
        }

        private void RefreshCards()
        {
            var notes = _view.notes.ToDictionary(n => n.cardId);
            foreach (var (id, card) in _cards)
            {
                notes.TryGetValue(id, out var note);
                card.SetNote(note, note != null ? Player(note.byPlayerId) : null);

                bool selecting = IsMyTurnAndFree();
                card.SetInteractable(selecting);
                card.SetSelected(selecting && _picked.TryGetValue(CardCatalog.TypeOf(id), out var sel) && sel == id);
                card.SetDimmed(false);

                // only unknown cards can be suspects; a known card drops its mark
                bool unknown = note == null || note.kind == NoteKind.None;
                card.SetSuspectAvailable(unknown && _view.phase == Phase.Playing);
                card.SetSuspect(unknown && _suspects.Contains(id));
            }
        }

        // ------------------------------------------------------------------ suspects (private notebook)

        private void LoadSuspects()
        {
            string key = $"noclue.suspects.{_view.gameId}.{_view.myPlayerId}";
            if (key == _suspectPrefKey) return;
            _suspectPrefKey = key;
            _suspects.Clear();
            foreach (var id in PlayerPrefs.GetString(key, "").Split(','))
                if (CardCatalog.Exists(id)) _suspects.Add(id);
        }

        private void ToggleSuspect(string id)
        {
            if (!_suspects.Remove(id)) _suspects.Add(id);
            if (_suspectPrefKey != null)
            {
                PlayerPrefs.SetString(_suspectPrefKey, string.Join(",", _suspects));
                PlayerPrefs.Save();
            }
            RefreshCards();
        }

        // ------------------------------------------------------------------ bottom action bar

        private void RefreshActionBar()
        {
            var me = Me;
            bool myTurn = IsMyTurnAndFree();
            bool ready = myTurn && _picked.Count == 3;
            SetEnabled(_spin, ready);
            SetEnabled(_accuse, ready);

            string text;
            if (_view.phase != Phase.Playing || me == null) text = "";
            else if (me.eliminated || me.ejected) text = "<color=#9D917C>You're out. Keep watching!</color>";
            else if (myTurn)
            {
                string Pick(CardType t) => _picked.TryGetValue(t, out var id)
                    ? $"<color=#C9A04A>{CardCatalog.DisplayName(id)}</color>"
                    : $"<color=#9D917C>{t.ToString().ToLower()}?</color>";
                text = $"{Pick(CardType.Character)}  ·  {Pick(CardType.Room)}  ·  {Pick(CardType.Weapon)}";
            }
            else if (_view.ask.active && _view.ask.responderId == me.id) text = "You're being asked: answer in the pop-up.";
            else text = "<color=#9D917C>Swipe the rows to look at your cards. Tap ? to mark a suspect.</color>";
            _picksLabel.text = text;
        }

        // ------------------------------------------------------------------ log pop-up

        private void ToggleLog()
        {
            if (_logModal != null)
            {
                Object.Destroy(_logModal);
                _logModal = null;
                return;
            }

            var box = Ui.Modal(_root, "Log", 1000, 820, out _logModal);
            Ui.VLayout(box, 12, 28, TextAnchor.UpperCenter);
            Ui.Size(Ui.Label(box, "What happened", 40, Ui.Accent, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform, -1, 56);
            var area = Ui.Panel(box, "Area", Ui.Background);
            Ui.Flex(area, 1, 1);
            var content = Ui.ScrollArea(area, "Scroll", out _logScroll);
            Ui.Stretch((RectTransform)content.parent.parent, 16, 12, 16, 12);
            Ui.VLayout(content, 0);
            Ui.FitHeight(content);
            _logText = Ui.Label(content, "", 24, Ui.TextColor, TextAnchor.UpperLeft);
            Ui.Button(box, "Close", ToggleLog, Ui.PanelLight, width: 260, height: 76);
            RefreshLog();
        }

        private void RefreshLog()
        {
            if (_logModal == null || _logText == null) return;
            _logText.text = string.Join("\n", _view.log);
            Canvas.ForceUpdateCanvases();
            _logScroll.verticalNormalizedPosition = 0;   // newest at the bottom, in view
        }

        // ------------------------------------------------------------------ host: disconnected players

        /// <summary>
        /// Host only: when someone drops, ask "wait or eject?". "Wait" hides it; tapping their greyed-out
        /// chip in the player strip asks again.
        /// </summary>
        private void RefreshHostPrompt()
        {
            bool host = Me is { isHost: true } && _view.phase == Phase.Playing;
            var dropped = host
                ? _view.players.FirstOrDefault(p => !p.connected && !p.ejected && !p.eliminated && !_waitingOn.Contains(p.id))
                : null;

            if (dropped == null || dropped.id != _hostModalFor)
            {
                if (_hostModal != null) Object.Destroy(_hostModal);
                _hostModal = null;
                _hostModalFor = -1;
            }
            if (dropped == null || _hostModal != null) return;

            _hostModalFor = dropped.id;
            int id = dropped.id;
            var box = Ui.Modal(_root, "Disconnected", 860, 560, out _hostModal);
            Ui.VLayout(box, 16, 30, TextAnchor.UpperCenter);
            Ui.Size(Ui.Label(box, $"{dropped.name} disconnected", 44, Ui.Danger, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform, -1, 60);
            var portrait = Ui.Image(box, CardCatalog.GetSprite(dropped.characterId), "Portrait");
            portrait.color = new Color(0.5f, 0.5f, 0.5f);
            Ui.Size(portrait.rectTransform, -1, 190);
            Ui.Size(Ui.Label(box, "Wait for them to rejoin, or remove them. If you remove them, their cards are shown to everyone.",
                24, Ui.TextDim, TextAnchor.MiddleCenter).rectTransform, -1, 70);
            var buttons = Ui.Rect(box, "Buttons");
            Ui.Size(buttons, -1, 84);
            Ui.HLayout(buttons, 20, 0, TextAnchor.MiddleCenter);
            Ui.Button(buttons, "Wait", () => { _waitingOn.Add(id); RefreshHostPrompt(); }, Ui.PanelLight, width: 300, height: 80);
            Ui.Button(buttons, "Eject", () => _app.Send(MsgType.EjectPlayer, new EjectPlayerMsg { playerId = id }),
                Ui.Danger, width: 300, height: 80);
        }

        // ------------------------------------------------------------------ pop-ups

        private void RefreshAnswerModal()
        {
            var me = Me;
            var ask = _view.ask;
            bool mustAnswer = _view.phase == Phase.Playing && ask.active && me != null && ask.responderId == me.id;
            string key = mustAnswer ? $"{ask.askerId}-{ask.characterId}-{ask.roomId}-{ask.weaponId}" : null;
            if (key == _answerKey) return;

            _answerKey = key;
            if (_answerModal != null) Object.Destroy(_answerModal);
            _answerModal = null;
            if (!mustAnswer) return;

            var asker = Player(ask.askerId);
            var box = Ui.Modal(_root, "Answer", 900, 640, out _answerModal);
            Ui.VLayout(box, 18, 30, TextAnchor.UpperCenter);

            var held = new[] { ask.characterId, ask.roomId, ask.weaponId }.Where(_view.hand.Contains).ToList();
            Ui.Size(Ui.Label(box, $"{Name(asker)} asks you:", 40, Ui.Accent, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform, -1, 56);
            Ui.Size(Ui.Label(box, held.Count > 0 ? "Choose ONE card to show (only they will see it)." : "You don't have any of these.",
                26, Ui.TextDim, TextAnchor.MiddleCenter).rectTransform, -1, 40);

            var row = Ui.Rect(box, "Cards");
            Ui.Size(row, -1, 300);
            Ui.HLayout(row, 24, 0, TextAnchor.MiddleCenter);
            foreach (var id in new[] { ask.characterId, ask.roomId, ask.weaponId })
            {
                bool canShow = held.Contains(id);
                var card = new CardWidget(row, id, canShow ? ShowCard : null);
                Ui.Size(card.Root, 210, 280);
                card.SetDimmed(!canShow);
                card.SetSelected(canShow);
            }

            if (held.Count == 0)
                Ui.Button(box, "I have none", () => ShowCard(""), Ui.PanelLight, width: 400, height: 80);
        }

        private void ShowCard(string cardId)
        {
            _app.Send(MsgType.ShowCard, new ShowCardMsg { cardId = cardId });
            if (_answerModal != null) Object.Destroy(_answerModal);
            _answerModal = null;
        }

        private void RefreshShownModal()
        {
            // The host holds the turn until we click OK, so show the pop-up whenever it's waiting on us
            // (this also brings it back after a rejoin).
            bool waitingOnMe = _view.phase == Phase.Playing && _view.awaitingAckFrom == _view.myPlayerId &&
                               _view.ackKind == AckKind.ShownToMe && !string.IsNullOrEmpty(_view.lastShownCardId);
            if (!waitingOnMe)
            {
                if (_shownModal != null) Object.Destroy(_shownModal);
                _shownModal = null;
                return;
            }
            if (_shownModal != null) return;

            var by = Player(_view.lastShownById);
            var box = Ui.Modal(_root, "Shown", 640, 640, out _shownModal);
            Ui.VLayout(box, 18, 30, TextAnchor.UpperCenter, expandWidth: false);
            Ui.Size(Ui.Label(box, $"{Name(by)} showed you:", 38, Ui.Accent, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform, 560, 56);
            var card = new CardWidget(box, _view.lastShownCardId);
            Ui.Size(card.Root, 270, 360);
            Ui.Button(box, "OK", () =>
            {
                _app.Send(MsgType.AckShown, new AckShownMsg());
                if (_shownModal != null) Object.Destroy(_shownModal);
                _shownModal = null;
            }, width: 260, height: 70);
        }

        /// <summary>My question went all the way round and nobody could show a card. The turn waits for my OK.</summary>
        private void RefreshNobodyModal()
        {
            var q = _view.unanswered;
            bool waitingOnMe = _view.phase == Phase.Playing && _view.awaitingAckFrom == _view.myPlayerId &&
                               _view.ackKind == AckKind.NobodyShowed && q.active;
            if (!waitingOnMe)
            {
                if (_nobodyModal != null) Object.Destroy(_nobodyModal);
                _nobodyModal = null;
                return;
            }
            if (_nobodyModal != null) return;

            var box = Ui.Modal(_root, "Nobody", 900, 680, out _nobodyModal);
            Ui.VLayout(box, 16, 30, TextAnchor.UpperCenter);
            Ui.Size(Ui.Label(box, "Nobody could show you a card!", 44, Ui.Accent, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform, -1, 60);
            Ui.Size(Ui.Label(box, "No other player has any of these. Each one is in the envelope... or in your own hand.",
                26, Ui.TextDim, TextAnchor.MiddleCenter).rectTransform, -1, 70);

            var row = Ui.Rect(box, "Cards");
            Ui.Size(row, -1, 300);
            Ui.HLayout(row, 24, 0, TextAnchor.MiddleCenter);
            foreach (var id in new[] { q.characterId, q.roomId, q.weaponId })
            {
                var card = new CardWidget(row, id);
                Ui.Size(card.Root, 210, 280);
                bool mineCard = _view.hand.Contains(id);
                card.SetDimmed(mineCard);          // my own cards are no mystery
                card.SetSelected(!mineCard);       // the rest could be the answer
            }

            Ui.Button(box, "OK", () =>
            {
                _app.Send(MsgType.AckShown, new AckShownMsg());
                if (_nobodyModal != null) Object.Destroy(_nobodyModal);
                _nobodyModal = null;
            }, width: 260, height: 70);
        }

        // ------------------------------------------------------------------ the die and "Show all"

        private void StartRollAnimation()
        {
            if (_die == null) _die = DieView.Create();
            if (_rollModal != null) Object.Destroy(_rollModal);

            var roller = Player(_view.rollerId);
            var box = Ui.Modal(_root, "Roll", 640, 700, out _rollModal);
            Ui.VLayout(box, 10, 26, TextAnchor.UpperCenter, expandWidth: false);
            string who = roller != null && roller.id == _view.myPlayerId ? "You roll" : $"{Name(roller)} rolls";
            Ui.Size(Ui.Label(box, $"{who} the die...", 38, Ui.TextColor, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform, 580, 56);

            var image = Ui.Rect(box, "Die").gameObject.AddComponent<RawImage>();
            image.texture = _die.Texture;
            image.raycastTarget = false;
            Ui.Size(image.rectTransform, 460, 460);

            _rollResult = Ui.Label(box, "", 52, Ui.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Size(_rollResult.rectTransform, 580, 80);

            _rolling = true;
            _rollCloseAt = -1;
            _app.PlaySfx("dice-gambling");
            int side = _view.rollSide;

            // who the question goes to (already decided by the host): "Asking Mom!" reads better than "ASK NEXT!"
            var asked = _view.ask.active ? Player(_view.ask.responderId) : null;
            string askedName = asked == null ? null : asked.id == _view.myPlayerId ? "you" : asked.name;

            _die.Roll(side, () =>
            {
                var face = Die.FaceOf(side);
                if (_rollResult != null)
                {
                    _rollResult.text = face switch
                    {
                        DieFace.AskNext when askedName != null => $"Asking {askedName}!",
                        DieFace.Reverse when askedName != null => $"Reverse! Asking {askedName}",
                        _ => Die.Label(face) + "!",
                    };
                    _rollResult.color = DieView.FaceColor(face);
                }
                _rollCloseAt = Time.unscaledTime + 1.4f;   // let everyone read the result
            });
        }

        /// <summary>I rolled "Show all": pick which of my 3 cards everyone gets to see.</summary>
        private void RefreshRevealChoiceModal()
        {
            var rc = _view.revealChoice;
            bool mine = _view.phase == Phase.Playing && rc.active && rc.askerId == _view.myPlayerId;
            string key = mine ? $"{rc.characterId}-{rc.roomId}-{rc.weaponId}-{_view.rollSeq}" : null;
            if (key == _revealChoiceKey) return;

            _revealChoiceKey = key;
            if (_revealChoiceModal != null) Object.Destroy(_revealChoiceModal);
            _revealChoiceModal = null;
            if (!mine) return;

            var box = Ui.Modal(_root, "RevealChoice", 900, 620, out _revealChoiceModal);
            Ui.VLayout(box, 18, 30, TextAnchor.UpperCenter);
            Ui.Size(Ui.Label(box, "SHOW ALL!", 48, Ui.Blue, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform, -1, 64);
            Ui.Size(Ui.Label(box, "Pick one card. If another player has it, everyone gets to see it.",
                26, Ui.TextDim, TextAnchor.MiddleCenter).rectTransform, -1, 40);

            var row = Ui.Rect(box, "Cards");
            Ui.Size(row, -1, 300);
            Ui.HLayout(row, 24, 0, TextAnchor.MiddleCenter);
            foreach (var id in new[] { rc.characterId, rc.roomId, rc.weaponId })
            {
                var card = new CardWidget(row, id, ChooseReveal);
                Ui.Size(card.Root, 210, 280);
                card.SetSelected(true);
            }
        }

        private void ChooseReveal(string cardId)
        {
            _app.Send(MsgType.RevealChoice, new RevealChoiceMsg { cardId = cardId });
            if (_revealChoiceModal != null) Object.Destroy(_revealChoiceModal);
            _revealChoiceModal = null;
        }

        /// <summary>
        /// Everyone sees a "Show all" result. The roller's OK lets the game continue;
        /// for everyone else OK just closes it.
        /// </summary>
        private void RefreshPublicReveal()
        {
            bool iMustAck = _view.phase == Phase.Playing && _view.awaitingAckFrom == _view.myPlayerId &&
                            _view.ackKind == AckKind.PublicReveal;
            bool isNew = _view.publicRevealSeq > _seenPublicRevealSeq;

            if (!iMustAck && !isNew) return;                       // nothing new, nothing owed
            if (_publicRevealModal != null && !isNew) return;      // already showing it

            _seenPublicRevealSeq = _view.publicRevealSeq;
            if (_publicRevealModal != null) Object.Destroy(_publicRevealModal);
            if (string.IsNullOrEmpty(_view.publicRevealCardId)) return;

            var asker = Player(_view.publicRevealAskerId);
            var holder = Player(_view.publicRevealHolderId);
            string card = CardCatalog.DisplayName(_view.publicRevealCardId);
            string title = holder == null
                ? $"Nobody else has {card}!"
                : holder.id == _view.myPlayerId ? $"Your {card} card is revealed!" : $"{holder.name} has {card}!";

            var box = Ui.Modal(_root, "PublicReveal", 760, 720, out _publicRevealModal);
            Ui.VLayout(box, 16, 28, TextAnchor.UpperCenter, expandWidth: false);
            Ui.Size(Ui.Label(box, $"{Name(asker)} rolled SHOW ALL", 26, Ui.Blue, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform, 700, 36);
            Ui.Size(Ui.Label(box, title, 40, Ui.Accent, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform, 700, 56);

            var row = Ui.Rect(box, "Row");
            Ui.Size(row, 700, 380);
            Ui.HLayout(row, 24, 0, TextAnchor.MiddleCenter);
            Ui.Size(new CardWidget(row, _view.publicRevealCardId).Root, 270, 360);
            if (holder != null)
            {
                var portrait = Ui.Image(row, CardCatalog.GetSprite(holder.characterId), "Holder");
                Ui.Size(portrait.rectTransform, 150, 200);
            }
            else
            {
                var hint = Ui.Label(row, "It's either in the envelope,\nor in " +
                                         (asker != null && asker.id == _view.myPlayerId ? "your" : $"{Name(asker)}'s") + " own hand.",
                    24, Ui.TextDim, TextAnchor.MiddleCenter);
                Ui.Size(hint.rectTransform, 300, 200);
            }

            var modal = _publicRevealModal;
            Ui.Button(box, "OK", () =>
            {
                if (iMustAck) _app.Send(MsgType.AckShown, new AckShownMsg());
                Object.Destroy(modal);
            }, width: 260, height: 70);
        }

        private void RefreshGameOver()
        {
            if (_view.phase != Phase.GameOver || _gameOverModal != null || _gameOverDismissed) return;

            // Drumroll: "And the winner is..." plays while the title teases, then Tick() reveals the rest.
            var winner = Player(_view.winnerId);
            var box = Ui.Modal(_root, "GameOver", 1100, 860, out _gameOverModal);
            Ui.VLayout(box, 16, 30, TextAnchor.UpperCenter);
            _gameOverTitle = Ui.Label(box, "And the winner is...", 56, Ui.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Size(_gameOverTitle.rectTransform, -1, 80);
            _gameOverWinnerText = winner == null ? "Nobody solved it!"
                : winner.id == _view.myPlayerId ? $"YOU, {winner.name}!" : $"{winner.name}!";

            _gameOverContent = Ui.Rect(box, "Reveal");
            Ui.Flex(_gameOverContent, 1, 1);
            Ui.VLayout(_gameOverContent, 14, 0, TextAnchor.UpperCenter);

            if (winner != null)
            {
                var portrait = Ui.Image(_gameOverContent, CardCatalog.GetSprite(winner.characterId), "Winner");
                Ui.Size(portrait.rectTransform, -1, 190);
            }
            Ui.Size(Ui.Label(_gameOverContent, "It was...", 28, Ui.TextDim, TextAnchor.MiddleCenter).rectTransform, -1, 40);

            var row = Ui.Rect(_gameOverContent, "Solution");
            Ui.Size(row, -1, 290);
            Ui.HLayout(row, 24, 0, TextAnchor.MiddleCenter);
            foreach (var id in _view.solution)
                Ui.Size(new CardWidget(row, id).Root, 210, 280);

            var buttons = Ui.Rect(_gameOverContent, "Buttons");
            Ui.Size(buttons, -1, 80);
            Ui.HLayout(buttons, 20, 0, TextAnchor.MiddleCenter);
            Ui.Button(buttons, "Look at the table", () =>
            {
                _gameOverDismissed = true;
                Object.Destroy(_gameOverModal);
            }, Ui.PanelLight, width: 340);
            Ui.Button(buttons, "Back to menu", _app.Leave, width: 340);

            _gameOverContent.gameObject.SetActive(false);
            _app.PlaySfx("and-the-winner-is-deep-voice");
            _gameOverRevealAt = Time.unscaledTime + 2.1f;   // the voice line is 2.0 s
        }

        // ------------------------------------------------------------------ input

        private void OnCardClicked(string id)
        {
            if (!IsMyTurnAndFree()) return;
            var type = CardCatalog.TypeOf(id);
            if (_picked.TryGetValue(type, out var current) && current == id) _picked.Remove(type);
            else _picked[type] = id;
            Redraw();
        }

        /// <summary>Sends Ask or Accuse; does nothing unless one card of each type is picked.</summary>
        private void Send(string type)
        {
            if (_picked.Count != 3 || !IsMyTurnAndFree()) return;
            _app.Send(type, new SuggestionMsg
            {
                characterId = _picked[CardType.Character],
                roomId = _picked[CardType.Room],
                weaponId = _picked[CardType.Weapon],
            });
            _picked.Clear();
            if (_accuseModal != null) Object.Destroy(_accuseModal);
        }

        private void ShowAccuseConfirm()
        {
            if (_picked.Count != 3) return;
            if (_accuseModal != null) Object.Destroy(_accuseModal);

            var box = Ui.Modal(_root, "Accuse", 1000, 640, out _accuseModal);
            Ui.VLayout(box, 18, 30, TextAnchor.UpperCenter);
            Ui.Size(Ui.Label(box, "Make this accusation?", 44, Ui.Danger, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform, -1, 60);
            Ui.Size(Ui.Label(box, "If you're wrong, you're out of the game and everyone sees your cards.",
                26, Ui.TextDim, TextAnchor.MiddleCenter).rectTransform, -1, 40);

            var row = Ui.Rect(box, "Cards");
            Ui.Size(row, -1, 300);
            Ui.HLayout(row, 24, 0, TextAnchor.MiddleCenter);
            foreach (var t in new[] { CardType.Character, CardType.Room, CardType.Weapon })
                Ui.Size(new CardWidget(row, _picked[t]).Root, 210, 280);

            var buttons = Ui.Rect(box, "Buttons");
            Ui.Size(buttons, -1, 80);
            Ui.HLayout(buttons, 20, 0, TextAnchor.MiddleCenter);
            var modal = _accuseModal;
            Ui.Button(buttons, "Cancel", () => Object.Destroy(modal), Ui.PanelLight, width: 300);
            Ui.Button(buttons, "Accuse!", () => Send(MsgType.Accuse), Ui.Danger, width: 300);
        }

        /// <summary>Disabled buttons fade right out so it's obvious they can't be pressed yet.</summary>
        private static void SetEnabled(Button b, bool on)
        {
            b.interactable = on;
            // TryGetComponent, not "??": Unity's fake-null for missing components breaks the ?? operator.
            if (!b.TryGetComponent<CanvasGroup>(out var group)) group = b.gameObject.AddComponent<CanvasGroup>();
            group.alpha = on ? 1f : 0.35f;
        }

        private void Redraw()
        {
            RefreshCards();
            RefreshActionBar();
        }

        // ------------------------------------------------------------------ helpers

        private PlayerView Me => Player(_view.myPlayerId);
        private PlayerView Player(int id) => _view.players.FirstOrDefault(p => p.id == id);
        private static string Name(PlayerView p) => p?.name ?? "?";

        private bool IsMyTurnAndFree() =>
            _view.phase == Phase.Playing && _view.activePlayerId == _view.myPlayerId && !_view.ask.active &&
            !_view.revealChoice.active && _view.awaitingAckFrom < 0 && Me is { eliminated: false, ejected: false };

        public void Tick()
        {
            // "Your turn!" glows between old gold and blood red, like a candle catching
            if (_pulseText != null)
            {
                float k = (Mathf.Sin(Time.unscaledTime * 3f) + 1f) * 0.5f;
                _pulseText.color = Color.Lerp(Ui.Accent, Color.Lerp(Ui.Danger, Color.white, 0.25f), k);
            }

            // game over drumroll finished: reveal the winner and the solution
            if (_gameOverRevealAt > 0 && Time.unscaledTime >= _gameOverRevealAt)
            {
                _gameOverRevealAt = -1;
                if (_gameOverTitle != null) _gameOverTitle.text = _gameOverWinnerText;
                if (_gameOverContent != null) _gameOverContent.gameObject.SetActive(true);
            }

            // die has landed and the result has been on screen long enough: close it and show what follows
            if (!_rolling || _rollCloseAt < 0 || Time.unscaledTime < _rollCloseAt) return;
            _rolling = false;
            _rollCloseAt = -1;
            if (_rollModal != null) Object.Destroy(_rollModal);
            _rollModal = null;
            if (_view != null) Refresh(_view);
        }

        public void Dispose()
        {
            if (_die != null) Object.Destroy(_die.gameObject);
        }
    }
}
