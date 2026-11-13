using System;
using NoClue.Net;
using UnityEngine;
using UnityEngine.UI;

namespace NoClue.UI
{
    /// <summary>
    /// A 3D six-sided die, rendered by its own camera into a texture that the UI shows.
    /// Each face is a small world-space canvas, so faces can carry text without extra art.
    /// <see cref="Roll"/> tumbles it and lands it, face-up toward the viewer, on the side the host rolled.
    /// </summary>
    public sealed class DieView : MonoBehaviour
    {
        private const int Layer = 31;                       // only the die camera sees this layer
        private const float RollSeconds = 1.9f;
        private static readonly Vector3 Stage = new(0, -1000, 0);

        // Outward normal and "up" of each side in the die's local space.
        private static readonly (Vector3 normal, Vector3 up)[] Sides =
        {
            (Vector3.back, Vector3.up),
            (Vector3.right, Vector3.up),
            (Vector3.forward, Vector3.up),
            (Vector3.left, Vector3.up),
            (Vector3.up, Vector3.forward),
            (Vector3.down, Vector3.back),
        };

        private Transform _die;
        private Camera _camera;
        private RenderTexture _texture;
        private readonly Image[] _faces = new Image[Sides.Length];
        private readonly Color[] _faceColors = new Color[Sides.Length];

        private Quaternion _target = Quaternion.identity;
        private Vector3 _spinAxis = Vector3.one.normalized;
        private float _spinDegrees;
        private float _rollStart = -1;
        private Action _onLanded;

        public Texture Texture => _texture;

        public static DieView Create(int textureSize = 512)
        {
            var go = new GameObject("Die") { layer = Layer };
            go.transform.position = Stage;
            var view = go.AddComponent<DieView>();
            view.Build(textureSize);
            return view;
        }

        private void Build(int size)
        {
            _texture = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };

            var camGo = new GameObject("Die Camera") { layer = Layer };
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0, 0, -4.2f);
            _camera = camGo.AddComponent<Camera>();
            _camera.cullingMask = 1 << Layer;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Ui.PanelColor;   // matches the pop-up, so it looks right with or without alpha
            _camera.fieldOfView = 30;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 20;
            _camera.allowHDR = false;
            _camera.allowMSAA = true;
            _camera.targetTexture = _texture;

            // keep the die out of every other camera
            foreach (var cam in Camera.allCameras)
                if (cam != _camera) cam.cullingMask &= ~(1 << Layer);

            _die = new GameObject("Cube") { layer = Layer }.transform;
            _die.SetParent(transform, false);

            for (int i = 0; i < Sides.Length; i++) BuildFace(i);
            ShowSide(0);
        }

        private void BuildFace(int side)
        {
            var (normal, up) = Sides[side];
            var face = Die.FaceOf(side);

            var canvasGo = new GameObject("Side " + side, typeof(RectTransform), typeof(Canvas)) { layer = Layer };
            var rt = (RectTransform)canvasGo.transform;
            rt.SetParent(_die, false);
            rt.sizeDelta = new Vector2(200, 200);
            rt.localScale = Vector3.one * 0.005f;                    // 200 px -> 1 unit
            rt.localPosition = normal * 0.5f;
            rt.localRotation = Quaternion.LookRotation(-normal, up);  // a canvas is seen from behind its +Z
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

            var border = Ui.Panel(rt, "Border", Ui.Background);
            Ui.Stretch(border);

            // Painted face art (Resources/Dice/die_*.png) when available; plain colour + label otherwise.
            var art = FaceSprite(face);
            if (art != null)
            {
                var img = Ui.Image(border, art, "Face");
                img.preserveAspect = false;
                Ui.Stretch(img.rectTransform, 4, 4, 4, 4);   // thin dark edge reads as the die's bevel
                _faces[side] = img;
                _faceColors[side] = Color.white;             // Shade() darkens the art itself
                foreach (Transform t in canvasGo.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
                return;
            }

            var fill = Ui.Panel(border, "Face", FaceColor(face));
            Ui.Stretch(fill, 10, 10, 10, 10);
            _faces[side] = fill.GetComponent<Image>();
            _faceColors[side] = FaceColor(face);

            string text = face switch
            {
                DieFace.ShowAll => "SHOW\nALL",
                DieFace.Reverse => "« »\nREVERSE",
                _ => "ASK\nNEXT",
            };
            var label = Ui.Label(fill, text, 40, face == DieFace.AskNext ? Ui.Background : Ui.TextColor,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            label.lineSpacing = 0.9f;
            label.resizeTextForBestFit = true;   // "REVERSE" must stay on one line
            label.resizeTextMinSize = 16;
            label.resizeTextMaxSize = 40;
            Ui.Stretch(label.rectTransform, 8, 8, 8, 8);

            foreach (Transform t in canvasGo.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        }

        private static Sprite FaceSprite(DieFace face) => Resources.Load<Sprite>(face switch
        {
            DieFace.ShowAll => "Dice/die_show_all",
            DieFace.Reverse => "Dice/die_reverse",
            _ => "Dice/die_ask_next",
        });

        public static Color FaceColor(DieFace face) => face switch
        {
            DieFace.ShowAll => Ui.Blue,
            DieFace.Reverse => Ui.Danger,
            _ => Ui.Accent,
        };

        /// <summary>Rotation that puts <paramref name="side"/> facing the camera, upright.</summary>
        private static Quaternion FacingCamera(int side)
        {
            var (normal, up) = Sides[side];
            return Quaternion.Inverse(Quaternion.LookRotation(-normal, up));
        }

        public void ShowSide(int side)
        {
            _rollStart = -1;
            _target = FacingCamera(side);
            _die.localRotation = _target;
            _die.localPosition = Vector3.zero;
            Shade();
        }

        /// <summary>Tumbles the die and lands it on <paramref name="side"/>; <paramref name="onLanded"/> fires when it stops.</summary>
        public void Roll(int side, Action onLanded)
        {
            _target = FacingCamera(side);
            _spinAxis = UnityEngine.Random.onUnitSphere;
            _spinDegrees = 900 + UnityEngine.Random.Range(0, 360);
            _rollStart = Time.unscaledTime;
            _onLanded = onLanded;
        }

        private void Update()
        {
            if (_rollStart < 0) return;

            float t = Mathf.Clamp01((Time.unscaledTime - _rollStart) / RollSeconds);
            float ease = 1 - Mathf.Pow(1 - t, 3);                                       // fast, then settles
            _die.localRotation = _target * Quaternion.AngleAxis(_spinDegrees * (1 - ease), _spinAxis);
            float bounce = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3f)) * (1 - t) * 0.7f;    // three shrinking hops
            _die.localPosition = new Vector3(0, bounce, 0);
            Shade();

            if (t >= 1)
            {
                _rollStart = -1;
                var done = _onLanded;
                _onLanded = null;
                done?.Invoke();
            }
        }

        /// <summary>Faces turned away from the camera get darker, so the cube reads as 3D without lights.</summary>
        private void Shade()
        {
            var toCamera = (_camera.transform.position - _die.position).normalized;
            for (int i = 0; i < Sides.Length; i++)
            {
                var worldNormal = _die.rotation * Sides[i].normal;
                float light = Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(Vector3.Dot(worldNormal, toCamera)));
                var c = _faceColors[i];
                _faces[i].color = new Color(c.r * light, c.g * light, c.b * light, 1);
            }
        }

        private void OnDestroy()
        {
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
        }
    }
}
