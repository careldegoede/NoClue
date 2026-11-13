using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using NoClue.Core;
using NoClue.Net;
using NoClue.UI;
using UnityEngine;
using UnityEngine.UI;

namespace NoClue.App
{
    /// <summary>Title screen: name, HOST, JOIN.</summary>
    public sealed class MenuScreen : NoClueApp.IScreen
    {
        public GameObject Root { get; }

        public MenuScreen(NoClueApp app, Transform parent)
        {
            var root = Ui.Stretch(Ui.Rect(parent, "Menu"));
            Root = root.gameObject;

            var column = Ui.Rect(root, "Column");
            column.anchorMin = column.anchorMax = new Vector2(0.5f, 0.5f);
            column.sizeDelta = new Vector2(940, 1500);
            Ui.VLayout(column, 30, 0, TextAnchor.MiddleCenter, expandWidth: false);

            var logo = Ui.ThemeSprite("logo");
            if (logo != null)
            {
                Ui.Size(Ui.Image(column, logo, "Logo").rectTransform, 940, 300);
            }
            else
            {
                Ui.Size(Ui.Logo(column, 170).rectTransform, 940, 230);
            }
            Ui.Size(Ui.Label(column, "Who did it, where, and with what?", 34, Ui.TextDim, TextAnchor.MiddleCenter,
                FontStyle.Italic).rectTransform, 900, 70);
            Ui.Size(Ui.Rect(column, "Gap"), 10, 80);

            Ui.Size(Ui.Label(column, "Your name", 30, Ui.TextDim, TextAnchor.LowerLeft).rectTransform, 760, 40);
            var name = Ui.Input(column, "Enter your name", app.PlayerName, 760, 100);
            name.characterLimit = 20;
            name.onEndEdit.AddListener(v => { if (!string.IsNullOrWhiteSpace(v)) app.PlayerName = v.Trim(); });

            Ui.Size(Ui.Rect(column, "Gap"), 10, 30);
            Ui.Button(column, "HOST", () => { Save(app, name); app.HostGame(); }, width: 760, height: 130, fontSize: 46);
            Ui.Button(column, "JOIN", () => { Save(app, name); app.ShowBrowse(); }, Ui.PanelLight, width: 760, height: 130, fontSize: 46);
#if !UNITY_EDITOR
            Ui.Button(column, "Quit", Application.Quit, Ui.PanelColor, width: 760, height: 100);
#endif
        }

        private static void Save(NoClueApp app, InputField field)
        {
            if (!string.IsNullOrWhiteSpace(field.text)) app.PlayerName = field.text.Trim();
        }

        public void Refresh(GameView view) { }
        public void Tick() { }
        public void Dispose() { }
    }

    /// <summary>A centered message while connecting.</summary>
    public sealed class WaitingScreen : NoClueApp.IScreen
    {
        public GameObject Root { get; }

        public WaitingScreen(NoClueApp app, Transform parent, string message)
        {
            var root = Ui.Stretch(Ui.Rect(parent, "Waiting"));
            Root = root.gameObject;
            Ui.Stretch(Ui.Label(root, message, 40, Ui.TextDim, TextAnchor.MiddleCenter).rectTransform);
            var back = Ui.Button(root, "Cancel", app.ShowMenu, Ui.PanelLight, width: 600, height: 110);
            var rt = (RectTransform)back.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.3f);
        }

        public void Refresh(GameView view) { }
        public void Tick() { }
        public void Dispose() { }
    }

    /// <summary>Games found on the LAN, plus join-by-IP.</summary>
    public sealed class BrowseScreen : NoClueApp.IScreen
    {
        private readonly NoClueApp _app;
        private readonly LanListener _listener;
        private readonly RectTransform _list;
        private readonly Text _empty;
        private string _signature = "";
        private float _nextRefresh;

        public GameObject Root { get; }

        public BrowseScreen(NoClueApp app, Transform parent)
        {
            _app = app;
            var root = Ui.Stretch(Ui.Rect(parent, "Browse"));
            Root = root.gameObject;

            var column = Ui.Stretch(Ui.Rect(root, "Column"), 40, 60, 40, 50);
            Ui.VLayout(column, 22);

            Ui.Size(Ui.Label(column, "Join a game", 70, Ui.Accent, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, -1, 100);
            Ui.Size(Ui.Label(column, "Games hosted on your network:", 30, Ui.TextDim).rectTransform, -1, 44);

            var listBox = Ui.Panel(column, "Games", Ui.PanelColor);
            Ui.Flex(listBox, 1, 1);
            _list = Ui.ScrollArea(listBox, "Scroll", out _);
            Ui.Stretch((RectTransform)_list.parent.parent, 16, 16, 16, 16);
            Ui.VLayout(_list, 12);
            Ui.FitHeight(_list);
            _empty = Ui.Label(listBox, "Looking for games...", 30, Ui.TextDim, TextAnchor.MiddleCenter);
            Ui.Stretch(_empty.rectTransform);

            Ui.Size(Ui.Label(column, "Or enter the host's IP:", 30, Ui.TextDim).rectTransform, -1, 44);
            var manual = Ui.Rect(column, "Manual");
            Ui.Size(manual, -1, 100);
            Ui.HLayout(manual, 16);
            var ip = Ui.Input(manual, "192.168.1.20", PlayerPrefs.GetString("noclue.lastIp", ""), 400, 100);
            Ui.Flex(ip, 1);
            Ui.Button(manual, "Join", () =>
            {
                string text = ip.text.Trim();
                if (text.Length == 0) return;
                PlayerPrefs.SetString("noclue.lastIp", text);   // remembered for next time
                PlayerPrefs.Save();
                int port = TcpHostServer.DefaultPort;
                int colon = text.LastIndexOf(':');
                if (colon > 0 && int.TryParse(text.Substring(colon + 1), out int p)) { port = p; text = text.Substring(0, colon); }
                _app.JoinGame(text, port, null);
            }, width: 240, height: 100);

            Ui.Button(column, "Back", app.ShowMenu, Ui.PanelLight, width: -1, height: 110);

            try
            {
                _listener = new LanListener();
            }
            catch (SocketException e)
            {
                _empty.text = "Can't listen for games on this network (" + e.Message + ").\nUse the host's IP below.";
            }
        }

        public void Tick()
        {
            if (_listener == null || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.5f;

            var games = _listener.CurrentGames();
            string sig = string.Join("|", games.Select(g => $"{g.gameId}{g.playerCount}{g.inProgress}{g.hostCharacterId}"));
            if (sig == _signature) return;
            _signature = sig;

            Ui.Clear(_list);
            _empty.gameObject.SetActive(games.Count == 0);
            foreach (var g in games) AddRow(g);
        }

        private void AddRow(GameAdvert g)
        {
            var row = Ui.Panel(_list, "Game " + g.gameId, Ui.PanelLight);
            Ui.Size(row, -1, 150);
            Ui.HLayout(row, 20, 14);

            var portrait = Ui.Image(row, CardCatalog.GetSprite(g.hostCharacterId), "Host");
            Ui.Size(portrait.rectTransform, 90, 120);
            if (portrait.sprite == null) portrait.color = Ui.PanelColor;   // host still choosing: no white box

            string status = g.inProgress ? "in progress" : $"{g.playerCount}/{g.maxPlayers} players";
            var label = Ui.Label(row, $"<b>{g.hostName}'s game</b>\n<size=24><color=#9AA0B0>{status}  ·  {g.address}</color></size>", 32);
            label.supportRichText = true;
            Ui.Flex(label, 1);

            bool rejoin = _app.HasTokenFor(g.gameId);
            bool canJoin = rejoin || (!g.inProgress && g.playerCount < g.maxPlayers);
            var btn = Ui.Button(row, rejoin && g.inProgress ? "Rejoin" : "Join",
                () => _app.JoinGame(g.address, g.tcpPort, g.gameId), width: 220, height: 100);
            btn.interactable = canJoin;
        }

        public void Refresh(GameView view) { }

        public void Dispose() => _listener?.Dispose();
    }

    /// <summary>Character picker + who's here + Start (host only).</summary>
    public sealed class LobbyScreen : NoClueApp.IScreen
    {
        private readonly NoClueApp _app;
        private readonly Dictionary<string, (CardWidget card, Text owner)> _characters = new();
        private readonly RectTransform _players;
        private readonly Text _title;
        private readonly Text _hint;
        private readonly Button _start;
        private readonly Text _waiting;
        private readonly Text _count;
        private readonly Carousel _carousel;
        private readonly Text _pageLabel;
        private readonly Button _choose;
        private GameView _view;
        private bool _positioned;

        public GameObject Root { get; }

        public LobbyScreen(NoClueApp app, Transform parent)
        {
            _app = app;
            var root = Ui.Stretch(Ui.Rect(parent, "Lobby"));
            Root = root.gameObject;

            // one portrait column: title, the big character card, then who's here and Start
            var left = Ui.Stretch(Ui.Rect(root, "Column"), 36, 36, 36, 36);
            Ui.VLayout(left, 14, 0, TextAnchor.UpperCenter);

            _title = Ui.Label(left, "Lobby", 60, Ui.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Size(_title.rectTransform, -1, 84);
            _hint = Ui.Label(left, "Choose your character", 30, Ui.TextDim, TextAnchor.MiddleCenter);
            Ui.Size(_hint.rectTransform, -1, 80);

            // one big character card at a time: swipe / arrows / mouse wheel (phone friendly)
            var stage = Ui.Rect(left, "Stage");
            Ui.Flex(stage, 1, 1);
            _carousel = Carousel.Create(stage, "Characters");
            Ui.Stretch((RectTransform)_carousel.transform);
            foreach (var id in CardCatalog.Characters)
            {
                var page = _carousel.AddPage(id);
                var card = new CardWidget(page, id, OnPick);
                var rt = card.Root;
                rt.anchorMin = new Vector2(0.5f, 0);
                rt.anchorMax = new Vector2(0.5f, 1);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = new Vector2(0, 46);
                rt.offsetMax = new Vector2(0, -6);
                var fit = rt.gameObject.AddComponent<AspectRatioFitter>();   // card shape, as tall as fits
                fit.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
                fit.aspectRatio = 896f / 1200f;
                var owner = Ui.Label(page, "", 26, Ui.TextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
                owner.rectTransform.anchorMin = new Vector2(0, 0);
                owner.rectTransform.anchorMax = new Vector2(1, 0);
                owner.rectTransform.pivot = new Vector2(0.5f, 0);
                owner.rectTransform.sizeDelta = new Vector2(0, 40);
                _characters[id] = (card, owner);
            }

            // big thumb-sized arrows on both edges
            ArrowButton(stage, "«", 0f, _carousel.Previous);
            ArrowButton(stage, "»", 1f, _carousel.Next);

            // "3 / 8" + the pick button
            var nav = Ui.Rect(left, "Nav");
            Ui.Size(nav, -1, 110);
            Ui.HLayout(nav, 20, 0, TextAnchor.MiddleCenter);
            _pageLabel = Ui.Label(nav, "", 30, Ui.TextDim, TextAnchor.MiddleCenter);
            Ui.Size(_pageLabel.rectTransform, 110, 100);
            _choose = Ui.Button(nav, "Choose", () => OnPick(CardCatalog.Characters[_carousel.Page]),
                width: 760, height: 110, fontSize: 36);
            _carousel.PageChanged += _ => UpdatePicker();

            // players: two per row so all 8 fit under the card
            var right = Ui.Panel(left, "Players", Ui.PanelColor);
            Ui.VLayout(right, 10, 20);
            Ui.FitHeight(right);
            var header = Ui.Rect(right, "Header");
            Ui.Size(header, -1, 50);
            Ui.HLayout(header, 10);
            Ui.Flex(Ui.Label(header, "Players", 34, Ui.TextColor, TextAnchor.MiddleLeft, FontStyle.Bold), 1);
            _count = Ui.Label(header, "", 28, Ui.TextDim, TextAnchor.MiddleRight);
            Ui.Size(_count.rectTransform, 220, 50);

            _players = Ui.Rect(right, "List");
            var grid = Ui.Grid(_players, new Vector2(479, 96), 10);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            Ui.FitHeight(_players);

            if (app.Session is { IsHost: true })
            {
                var ips = LocalAddresses();
                if (ips.Count > 0)
                    Ui.Size(Ui.Label(right, "Your IP: " + string.Join(", ", ips), 24, Ui.TextDim).rectTransform, -1, 36);
            }

            _start = Ui.Button(left, "Start Game", () => _app.Send(MsgType.StartGame, new StartGameMsg()),
                width: -1, height: 120, fontSize: 42);
            _waiting = Ui.Label(left, "Waiting for the host to start...", 28, Ui.TextDim, TextAnchor.MiddleCenter);
            Ui.Size(_waiting.rectTransform, -1, 44);
            Ui.Button(left, "Leave", app.Leave, Ui.PanelLight, width: -1, height: 96);
        }

        private void OnPick(string characterId) =>
            _app.Send(MsgType.PickCharacter, new PickCharacterMsg { characterId = characterId });

        private static void ArrowButton(RectTransform stage, string glyph, float side, UnityEngine.Events.UnityAction onClick)
        {
            var b = Ui.Button(stage, glyph, onClick, new Color(0, 0, 0, 0.55f), 56, 96, 150);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(side, 0.55f);
            rt.pivot = new Vector2(side, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }

        /// <summary>Page counter and the Choose button for the card currently in view.</summary>
        private void UpdatePicker()
        {
            _pageLabel.text = $"{_carousel.Page + 1} / {_carousel.PageCount}";
            if (_view == null) return;
            string id = CardCatalog.Characters[_carousel.Page];
            var taker = _view.players.FirstOrDefault(p => p.characterId == id);
            bool mine = taker != null && taker.id == _view.myPlayerId;
            string name = CardCatalog.DisplayName(id);
            Ui.SetButtonLabel(_choose, taker == null ? $"Play as {name}" : mine ? $"You are {name}" : $"Taken by {taker.name}");
            _choose.interactable = taker == null;
        }

        public void Refresh(GameView view)
        {
            _view = view;
            var me = view.players.FirstOrDefault(p => p.id == view.myPlayerId);
            var host = view.players.FirstOrDefault(p => p.isHost);
            _title.text = host != null ? $"{host.name}'s game" : "Lobby";
            _hint.text = string.IsNullOrEmpty(me?.characterId)
                ? "Choose your character"
                : $"You are {CardCatalog.DisplayName(me.characterId)}. You can still switch to a free character.";

            foreach (var (id, (card, owner)) in _characters)
            {
                var taker = view.players.FirstOrDefault(p => p.characterId == id);
                bool mine = taker != null && taker.id == view.myPlayerId;
                bool takenByOther = taker != null && !mine;
                card.SetSelected(mine);
                card.SetDimmed(takenByOther);
                card.SetInteractable(!takenByOther);
                owner.text = taker == null ? "Available" : mine ? "You" : "Taken by " + taker.name;
                owner.color = mine ? Ui.Accent : taker == null ? Ui.Good : Ui.TextDim;
            }

            // first time in: open on my character, or the first free one
            if (!_positioned)
            {
                _positioned = true;
                string start = !string.IsNullOrEmpty(me?.characterId)
                    ? me.characterId
                    : CardCatalog.Characters.FirstOrDefault(c => view.players.All(p => p.characterId != c));
                int idx = System.Array.IndexOf(CardCatalog.Characters, start);
                _carousel.GoTo(Mathf.Max(0, idx), instant: true);
            }
            UpdatePicker();

            Ui.Clear(_players);
            foreach (var p in view.players)
            {
                var row = Ui.Panel(_players, p.name, p.id == view.myPlayerId ? Ui.PanelLight : Ui.Background);
                Ui.HLayout(row, 14, 8);
                var img = Ui.Image(row, CardCatalog.GetSprite(p.characterId), "Portrait");
                Ui.Size(img.rectTransform, 60, 80);
                if (img.sprite == null)
                {
                    // still choosing: a dark placeholder card with a "?" instead of a blank white box
                    img.color = Ui.PanelLight;
                    Ui.Stretch(Ui.Label(img.transform, "?", 34, Ui.TextDim, TextAnchor.MiddleCenter).rectTransform);
                }
                string tag = p.isHost ? "  <color=#C9A04A>(host)</color>" : "";
                string choosing = string.IsNullOrEmpty(p.characterId) ? "\n<size=22><color=#9AA0B0>choosing...</color></size>"
                    : $"\n<size=22><color=#9AA0B0>{CardCatalog.DisplayName(p.characterId)}</color></size>";
                var label = Ui.Label(row, p.name + tag + choosing, 28);
                label.supportRichText = true;
                label.resizeTextForBestFit = true;   // long names shrink to fit the half-width chip
                label.resizeTextMinSize = 16;
                label.resizeTextMaxSize = label.fontSize;
                label.verticalOverflow = VerticalWrapMode.Truncate;
                Ui.Flex(label, 1, 1);
            }
            _count.text = $"{view.players.Length}/{CardCatalog.MaxPlayers}";

            bool isHost = me is { isHost: true };
            bool canStart = view.players.Length >= 2 && view.players.All(p => !string.IsNullOrEmpty(p.characterId));
            _start.gameObject.SetActive(isHost);
            _start.interactable = canStart;
            _waiting.gameObject.SetActive(!isHost || !canStart);
            _waiting.text = isHost
                ? view.players.Length < 2 ? "Waiting for players to join..." : "Waiting for everyone to pick a character..."
                : "Waiting for the host to start...";
        }

        private static List<string> LocalAddresses()
        {
            // only home-network addresses (192.168.x.x, 10.x.x.x, 172.16-31.x.x): not mobile data or VPNs
            return LanDiscovery.LocalSubnets()
                .Select(s => s.ip.GetAddressBytes())
                .Where(b => b[0] == 192 && b[1] == 168 || b[0] == 10 || b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                .Select(b => string.Join(".", b))
                .Distinct()
                .ToList();
        }

        public void Tick() { }
        public void Dispose() { }
    }
}
