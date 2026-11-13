using UnityEngine;

namespace NoClue.UI
{
    /// <summary>Pop-ups grow in from slightly small and fade up, instead of just appearing.</summary>
    public sealed class PopIn : MonoBehaviour
    {
        private const float Seconds = 0.18f;
        private float _start;
        private CanvasGroup _group;

        private void OnEnable()
        {
            _start = Time.unscaledTime;
            if (!TryGetComponent(out _group)) _group = gameObject.AddComponent<CanvasGroup>();
            Apply(0);
        }

        private void Update()
        {
            float t = Mathf.Clamp01((Time.unscaledTime - _start) / Seconds);
            Apply(t);
            if (t >= 1) enabled = false;
        }

        private void Apply(float t)
        {
            float ease = 1 - Mathf.Pow(1 - t, 3);
            transform.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, ease);
            _group.alpha = ease;
        }
    }
}
