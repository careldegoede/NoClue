using System;
using System.Collections.Generic;
using NoClue.Net;
using NoClue.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace NoClue.App
{
    /// <summary>
    /// Owns the canvas, the music, the network session and which screen is showing.
    /// Created automatically when any scene loads, so no scene wiring is needed.
    /// </summary>
    public sealed class NoClueApp : MonoBehaviour
    {
        public interface IScreen
        {
            GameObject Root { get; }
            void Refresh(GameView view);
            void Tick();
            void Dispose();
        }

        private const string PrefName = "noclue.name";
        private const string PrefLastToken = "noclue.lastToken";
        private const string PrefTokenPrefix = "noclue.token.";

        private RectTransform _screenRoot;
        private RectTransform _overlayRoot;
        private IScreen _screen;
        private AudioSource _music;
        private AudioSource _sfx;
        private readonly Dictionary<string, AudioClip> _clips = new();
        private float _musicTarget = 0.6f;
        private readonly List<Envelope> _inbox = new();
        private (GameObject go, float until)? _toast;

        public ISession Session { get; private set; }
        public GameView View { get; private set; }
        public bool Connecting { get; private set; }

        public string PlayerName
        {
            get => PlayerPrefs.GetString(PrefName, Environment.UserName);
            set { PlayerPrefs.SetString(PrefName, value); PlayerPrefs.Save(); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<NoClueApp>() != null) return;
            var go = new GameObject("NoClueApp");
            DontDestroyOnLoad(go);
            go.AddComponent<NoClueApp>();
        }

        private void Awake()
        {
            Application.runInBackground = true;   // the host must keep serving while unfocused
            Application.targetFrameRate = 60;
            BuildCanvas();
            BuildMusic();
            ShowMenu();
        }

        private void BuildCanvas()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions(); // point/click/navigate
                DontDestroyOnLoad(es);
            }

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var bg = Ui.Panel(canvasGo.transform, "Background", Ui.Background);
            Ui.Stretch(bg);
            _screenRoot = Ui.Stretch(Ui.Rect(canvasGo.transform, "Screen"));
            _overlayRoot = Ui.Stretch(Ui.Rect(canvasGo.transform, "Overlay"));
        }

        private void BuildMusic()
        {
            _music = gameObject.AddComponent<AudioSource>();
            _music.clip = Resources.Load<AudioClip>("Audio/introbackground");
            _music.loop = true;
            _music.volume = 0;
            if (_music.clip != null) _music.Play();
            else Debug.LogWarning("[NoClue] Intro music not found at Resources/Audio/introbackground");

            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
        }

        /// <summary>Plays a sound effect from Resources/Audio/&lt;name&gt; (missing clips are ignored).</summary>
        public void PlaySfx(string name, float volume = 1f)
        {
            if (!_clips.TryGetValue(name, out var clip))
            {
                clip = Resources.Load<AudioClip>("Audio/" + name);
                if (clip == null) Debug.LogWarning($"[NoClue] Sound not found: Resources/Audio/{name}");
                _clips[name] = clip;
            }
            if (clip != null) _sfx.PlayOneShot(clip, volume);
        }

        // ------------------------------------------------------------------ navigation

        public void ShowMenu()
        {
            EndSession();
            _musicTarget = 0.6f;
            SetScreen(new MenuScreen(this, _screenRoot));
        }

        public void ShowBrowse()
        {
            EndSession();
            _musicTarget = 0.6f;
            SetScreen(new BrowseScreen(this, _screenRoot));
        }

        private void SetScreen(IScreen screen)
        {
            if (_screen != null)
            {
                _screen.Dispose();
                Destroy(_screen.Root);
            }
            _screen = screen;
        }

        public void HostGame()
        {
            EndSession();
            try
            {
                Session = new HostSession();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Toast("Couldn't start hosting: " + e.Message);
                return;
            }
            SetScreen(new WaitingScreen(this, _screenRoot, "Starting game..."));
            Session.Send(MsgType.JoinRequest, new JoinRequestMsg { name = PlayerName });
        }

        public async void JoinGame(string address, int port, string gameId)
        {
            if (Connecting) return;
            EndSession();
            Connecting = true;
            SetScreen(new WaitingScreen(this, _screenRoot, $"Connecting to {address}..."));
            try
            {
                var session = await RemoteSession.ConnectAsync(address, port);
                if (this == null) { session.Dispose(); return; }   // app closed meanwhile
                Session = session;

                string token = gameId != null
                    ? PlayerPrefs.GetString(PrefTokenPrefix + gameId, "")
                    : PlayerPrefs.GetString(PrefLastToken, "");
                if (!string.IsNullOrEmpty(token)) Session.Send(MsgType.Rejoin, new RejoinMsg { token = token });
                else Session.Send(MsgType.JoinRequest, new JoinRequestMsg { name = PlayerName });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NoClue] Connect failed: {e.Message}");
                Toast($"Couldn't connect to {address}: {e.Message}");
                ShowBrowse();
            }
            finally
            {
                Connecting = false;
            }
        }

        public bool HasTokenFor(string gameId) =>
            !string.IsNullOrEmpty(PlayerPrefs.GetString(PrefTokenPrefix + gameId, ""));

        public void Leave() => ShowMenu();

        public void Send(string type, object payload) => Session?.Send(type, payload);

        private void EndSession()
        {
            Session?.Dispose();
            Session = null;
            View = null;
        }

        // ------------------------------------------------------------------ frame loop

        private void Update()
        {
            if (_music != null) _music.volume = Mathf.MoveTowards(_music.volume, _musicTarget, Time.unscaledDeltaTime * 0.4f);

            if (Session != null)
            {
                _inbox.Clear();
                Session.Poll(_inbox);
                foreach (var env in _inbox)
                {
                    if (Session == null) break;   // a message ended the session (e.g. game full)
                    Ui.Safe(() => Handle(env));
                }

                if (Session != null && !Session.IsOpen)
                {
                    Toast("Lost connection to the host. Join again to rejoin your seat.");
                    ShowBrowse();
                }
            }

            _screen?.Tick();

            if (_toast.HasValue && Time.unscaledTime > _toast.Value.until)
            {
                Destroy(_toast.Value.go);
                _toast = null;
            }
        }

        private void Handle(Envelope env)
        {
            switch (env.type)
            {
                case MsgType.Welcome:
                {
                    var msg = FramedConnection.Read<WelcomeMsg>(env);
                    PlayerPrefs.SetString(PrefTokenPrefix + msg.gameId, msg.token);
                    PlayerPrefs.SetString(PrefLastToken, msg.token);
                    PlayerPrefs.Save();
                    break;
                }
                case MsgType.Rejected:
                {
                    var msg = FramedConnection.Read<RejectedMsg>(env);
                    if (msg.reason == "rejoin-unknown")
                    {
                        Session.Send(MsgType.JoinRequest, new JoinRequestMsg { name = PlayerName });
                        return;
                    }
                    Toast(msg.reason);
                    if (msg.fatal) ShowBrowse();
                    break;
                }
                case MsgType.Toast:
                    Toast(FramedConnection.Read<ToastMsg>(env).text);
                    break;
                case MsgType.View:
                    ApplyView(FramedConnection.Read<GameView>(env));
                    break;
            }
        }

        private void ApplyView(GameView view)
        {
            View = view;
            if (view.phase == Phase.Lobby)
            {
                _musicTarget = 0.6f;
                if (_screen is not LobbyScreen) SetScreen(new LobbyScreen(this, _screenRoot));
            }
            else
            {
                _musicTarget = 0.12f;
                if (_screen is not GameScreen) SetScreen(new GameScreen(this, _screenRoot));
            }
            _screen.Refresh(view);
        }

        // ------------------------------------------------------------------ toasts

        public void Toast(string text)
        {
            if (_toast.HasValue) Destroy(_toast.Value.go);

            var box = Ui.Panel(_overlayRoot, "Toast", new Color(0.08f, 0.08f, 0.1f, 0.94f));
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0);
            box.pivot = new Vector2(0.5f, 0);
            box.anchoredPosition = new Vector2(0, 60);
            box.sizeDelta = new Vector2(1100, 90);
            var label = Ui.Label(box, text, 28, Ui.TextColor, TextAnchor.MiddleCenter);
            Ui.Stretch(label.rectTransform, 20, 0, 20, 0);
            _toast = (box.gameObject, Time.unscaledTime + 4f);
        }

        private void OnApplicationQuit() => EndSession();
        private void OnDestroy() => EndSession();
    }
}
