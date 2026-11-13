using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
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
            column.sizeDelta = new Vector2(900, 800);
            Ui.VLayout(column, 26, 0, TextAnchor.MiddleCenter, expandWidth: false);

            var logo = Ui.ThemeSprite("logo");
            if (logo != null)
            {
                Ui.Size(Ui.Image(column, logo, "Logo").rectTransform, 900, 260);
            }
            else
            {
                Ui.Size(Ui.Logo(column, 150).rectTransform, 900, 190);
            }
            Ui.Size(Ui.Label(column, "Who did it, where, and with what?", 30, Ui.TextDim, TextAnchor.MiddleCenter,
                FontStyle.Italic).rectTransform, 700, 60);
            Ui.Size(Ui.Rect(column, "Gap"), 10, 30);

            Ui.Size(Ui.Label(column, "Your name", 26, Ui.TextDim, TextAnchor.LowerLeft).rectTransform, 520, 34);
            var name = Ui.Input(column, "Enter your name", app.PlayerName);
            name.characterLimit = 20;
            name.onEndEdit.AddListener(v => { if (!string.IsNullOrWhiteSpace(v)) app.PlayerName = v.Trim(); });

            Ui.Size(Ui.Rect(column, "Gap"), 10, 10);
            Ui.Button(column, "HOST", () => { Save(app, name); app.HostGame(); }, width: 520, height: 90, fontSize: 38);
            Ui.Button(column, "JOIN", () => { Save(app, name); app.ShowBrowse(); }, Ui.PanelLight, width: 520, height: 90, fontSize: 38);
#if !UNITY_EDITOR
            Ui.Button(column, "Quit", Application.Quit, Ui.PanelColor, width: 520, height: 70);
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
            var back = Ui.Button(root, "Cancel", app.ShowMenu, Ui.PanelLight);
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

            var column = Ui.Rect(root, "Column");
            column.anchorMin = new Vector2(0.5f, 0);
            column.anchorMax = new Vector2(0.5f, 1);
            column.sizeDelta = new Vector2(1100, -120);
            Ui.VLayout(column, 20);

            Ui.Size(Ui.Label(column, "Join a game", 64, Ui.Accent, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, -1, 90);
            Ui.Size(Ui.Label(column, "Games hosted on your network:", 28, Ui.TextDim).rectTransform, -1, 40);

            var listBox = Ui.Panel(column, "Games", Ui.PanelColor);
            Ui.Flex(listBox, 1, 1);
            _list = Ui.ScrollArea(listBox, "Scroll", out _);
            Ui.Stretch((RectTransform)_list.parent.parent, 16, 16, 16, 16);
            Ui.VLayout(_list, 12);
            Ui.FitHeight(_list);
            _empty = Ui.Label(listBox, "Looking for games...", 30, Ui.TextDim, TextAnchor.MiddleCenter);
            Ui.Stretch(_empty.rectTransform);

            var manual = Ui.Rect(column, "Manual");
            Ui.Size(manual, -1, 80);
            Ui.HLayout(manual, 16);
            Ui.Size(Ui.Label(manual, "Or enter the host's IP:", 28, Ui.TextDim).rectTransform, 300, 76);
            var ip = Ui.Input(manual, "192.168.1.20", "", 420);
            Ui.Button(manual, "Join", () =>
            {
                string text = ip.text.Trim();
                if (text.Length == 0) return;
                int port = TcpHostServer.DefaultPort;
                int colon = text.LastIndexOf(':');
                if (colon > 0 && int.TryParse(text.Substring(colon + 1), out int p)) { port = p; text = text.Substring(0, colon); }
                _app.JoinGame(text, port, null);
            }, width: 200, height: 76);

            Ui.Button(column, "Back", app.ShowMenu, Ui.PanelLight, width: 240, height: 70);

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
            Ui.Size(row, -1, 120);
            Ui.HLayout(row, 20, 12);

            var portrait = Ui.Image(row, CardCatalog.GetSprite(g.hostCharacterId), "Host");
            Ui.Size(portrait.rectTransform, 72, 96);

            string status = g.inProgress ? "in progress" : $"{g.playerCount}/{g.maxPlayers} players";
            var label = Ui.Label(row, $"<b>{g.hostName}'s game</b>\n<size=24><color=#9AA0B0>{status}  ·  {g.address}</color></size>", 32);
            label.supportRichText = true;
            Ui.Flex(label, 1);

            bool rejoin = _app.HasTokenFor(g.gameId);
            bool canJoin = rejoin || (!g.inProgress && g.playerCount < g.maxPlayers);
            var btn = Ui.Button(row, rejoin && g.inProgress ? "Rejoin" : "Join",
                () => _app.JoinGame(g.address, g.tcpPort, g.gameId), width: 200, height: 76);
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

        public GameObject Root { get; }

        public LobbyScreen(NoClueApp app, Transform parent)
        {
            _app = app;
            var root = Ui.Stretch(Ui.Rect(parent, "Lobby"));
            Root = root.gameObject;

            // left: character picker
            var left = Ui.Rect(root, "Picker");
            left.anchorMin = new Vector2(0, 0);
            left.anchorMax = new Vector2(1, 1);
            left.offsetMin = new Vector2(60, 60);
            left.offsetMax = new Vector2(-620, -40);
            Ui.VLayout(left, 16, 0, TextAnchor.UpperLeft);

            _title = Ui.Label(left, "Lobby", 56, Ui.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            Ui.Size(_title.rectTransform, -1, 80);
            _hint = Ui.Label(left, "Choose your character", 30, Ui.TextDim);
            Ui.Size(_hint.rectTransform, -1, 44);

            var grid = Ui.Rect(left, "Grid");
            Ui.Flex(grid, 1, 1);
            Ui.Grid(grid, new Vector2(240, 360), 20);
            foreach (var id in CardCatalog.Characters)
            {
                var cell = Ui.Rect(grid, id);
                var card = new CardWidget(cell, id, OnPick);
                card.Root.anchorMin = new Vector2(0, 0);
                card.Root.anchorMax = new Vector2(1, 1);
                card.Root.offsetMin = new Vector2(0, 40);
                card.Root.offsetMax = Vector2.zero;
                var owner = Ui.Label(cell, "", 24, Ui.TextDim, TextAnchor.MiddleCenter);
                owner.rectTransform.anchorMin = new Vector2(0, 0);
                owner.rectTransform.anchorMax = new Vector2(1, 0);
                owner.rectTransform.pivot = new Vector2(0.5f, 0);
                owner.rectTransform.sizeDelta = new Vector2(0, 36);
                _characters[id] = (card, owner);
            }

            // right: players + start
            var right = Ui.Panel(root, "Players", Ui.PanelColor);
            right.anchorMin = new Vector2(1, 0);
            right.anchorMax = new Vector2(1, 1);
            right.pivot = new Vector2(1, 0.5f);
            right.offsetMin = new Vector2(-560, 60);
            right.offsetMax = new Vector2(-60, -40);
            Ui.VLayout(right, 14, 28);

            Ui.Size(Ui.Label(right, "Players", 40, Ui.TextColor, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, -1, 56);
            _players = Ui.Rect(right, "List");
            Ui.Flex(_players, 1, 1);
            Ui.VLayout(_players, 10);

            if (app.Session is { IsHost: true })
            {
                var ips = LocalAddresses();
                if (ips.Count > 0)
                    Ui.Size(Ui.Label(right, "Your IP: " + string.Join(", ", ips), 22, Ui.TextDim).rectTransform, -1, 34);
            }

            _start = Ui.Button(right, "Start Game", () => _app.Send(MsgType.StartGame, new StartGameMsg()),
                width: -1, height: 90, fontSize: 36);
            _waiting = Ui.Label(right, "Waiting for the host to start...", 26, Ui.TextDim, TextAnchor.MiddleCenter);
            Ui.Size(_waiting.rectTransform, -1, 50);
            Ui.Button(right, "Leave", app.Leave, Ui.PanelLight, width: -1, height: 70);
        }

        private void OnPick(string characterId) =>
            _app.Send(MsgType.PickCharacter, new PickCharacterMsg { characterId = characterId });

        public void Refresh(GameView view)
        {
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
                owner.text = taker == null ? "" : mine ? "You" : taker.name;
                owner.color = mine ? Ui.Accent : Ui.TextDim;
            }

            Ui.Clear(_players);
            foreach (var p in view.players)
            {
                var row = Ui.Panel(_players, p.name, p.id == view.myPlayerId ? Ui.PanelLight : Ui.Background);
                Ui.Size(row, -1, 92);
                Ui.HLayout(row, 16, 8);
                var img = Ui.Image(row, CardCatalog.GetSprite(p.characterId), "Portrait");
                Ui.Size(img.rectTransform, 57, 76);
                string tag = p.isHost ? "  <color=#E0B04A>(host)</color>" : "";
                string choosing = string.IsNullOrEmpty(p.characterId) ? "\n<size=22><color=#9AA0B0>choosing...</color></size>"
                    : $"\n<size=22><color=#9AA0B0>{CardCatalog.DisplayName(p.characterId)}</color></size>";
                var label = Ui.Label(row, p.name + tag + choosing, 30);
                label.supportRichText = true;
                Ui.Flex(label, 1);
            }
            Ui.Size(Ui.Label(_players, $"{view.players.Length}/{CardCatalog.MaxPlayers} players", 24, Ui.TextDim).rectTransform, -1, 36);

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
            var result = new List<string>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork) result.Add(ua.Address.ToString());
                }
            }
            catch (NetworkInformationException) { }
            return result;
        }

        public void Tick() { }
        public void Dispose() { }
    }
}
