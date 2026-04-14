// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Fancy Button
//  Interactive button with hover glow, click bounce,
//  optional shine sweep, and disabled dim.
//  Attach to any GameObject with a Unity Button.
// ═══════════════════════════════════════════════════════
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace DualCraft.UI.Visual
{
    [RequireComponent(typeof(Button))]
    public class FancyButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        // ── Inspector ────────────────────────────────────────
        [Header("References")]
        [SerializeField] private Image background;
        [SerializeField] private Image glowRing;
        [SerializeField] private Image innerGlow;
        [SerializeField] private Image borderLine;
        [SerializeField] private TMP_Text label;
        [SerializeField] private Image icon;
        [SerializeField] private RectTransform shineRect;

        [Header("Colors")]
        [SerializeField] private Color normalColor    = new Color(0.12f, 0.10f, 0.18f, 0.95f);
        [SerializeField] private Color hoverColor     = new Color(0.18f, 0.14f, 0.28f, 1f);
        [SerializeField] private Color pressColor     = new Color(0.08f, 0.06f, 0.12f, 1f);
        [SerializeField] private Color disabledColor  = new Color(0.10f, 0.10f, 0.10f, 0.60f);
        [SerializeField] private Color glowColor      = new Color(0.78f, 0.66f, 0.42f, 0.55f);
        [SerializeField] private Color borderNormal   = new Color(0.30f, 0.26f, 0.50f, 0.25f);
        [SerializeField] private Color borderHover    = new Color(0.78f, 0.66f, 0.42f, 0.50f);

        [Header("Behaviour")]
        [SerializeField] private float hoverScale     = 1.06f;
        [SerializeField] private float clickScale     = 0.92f;
        [SerializeField] private bool enableShineSweep = true;
        [SerializeField] private float shinePeriod    = 4f;
        [SerializeField] private bool enableIdleBreath = true;
        [SerializeField] private float breathSpeed    = 1.2f;

        // ── Internals ────────────────────────────────────────
        private Button _button;
        private Coroutine _hoverCo;
        private Coroutine _shineCo;
        private Coroutine _breathCo;
        private bool _isHovered;

        private void Awake()
        {
            _button = GetComponent<Button>();
            if (glowRing != null)
            {
                Color c = glowColor;
                c.a = 0f;
                glowRing.color = c;
            }
            if (background != null)
                background.color = normalColor;
            if (borderLine != null)
                borderLine.color = borderNormal;
            if (innerGlow != null)
            {
                Color c = glowColor;
                c.a = 0f;
                innerGlow.color = c;
            }
        }

        private void OnEnable()
        {
            if (enableShineSweep && shineRect != null)
                _shineCo = StartCoroutine(ShineSweepLoop());
            if (enableIdleBreath && innerGlow != null)
                _breathCo = StartCoroutine(IdleBreathLoop());
        }

        private void OnDisable()
        {
            if (_shineCo != null) StopCoroutine(_shineCo);
            if (_hoverCo != null) StopCoroutine(_hoverCo);
            if (_breathCo != null) StopCoroutine(_breathCo);
            transform.localScale = Vector3.one;
        }

        // ══════════════════════════════════════════════════════
        //  POINTER EVENTS
        // ══════════════════════════════════════════════════════

        public void OnPointerEnter(PointerEventData _)
        {
            if (!_button.interactable) return;
            _isHovered = true;
            if (background != null) background.color = hoverColor;
            if (borderLine != null) borderLine.color = borderHover;
            if (_hoverCo != null) StopCoroutine(_hoverCo);
            _hoverCo = StartCoroutine(UIAnimUtils.HoverGrow(transform, hoverScale));
            ShowGlow(true);
            // Pause idle breath during hover — glow ring takes over
            if (_breathCo != null) { StopCoroutine(_breathCo); _breathCo = null; }
        }

        public void OnPointerExit(PointerEventData _)
        {
            _isHovered = false;
            if (background != null)
                background.color = _button.interactable ? normalColor : disabledColor;
            if (borderLine != null)
                borderLine.color = borderNormal;
            if (_hoverCo != null) StopCoroutine(_hoverCo);
            _hoverCo = StartCoroutine(UIAnimUtils.HoverShrink(transform));
            ShowGlow(false);
            // Restart idle breath
            if (enableIdleBreath && innerGlow != null && _breathCo == null)
                _breathCo = StartCoroutine(IdleBreathLoop());
        }

        public void OnPointerDown(PointerEventData _)
        {
            if (!_button.interactable) return;
            if (background != null) background.color = pressColor;
            if (_hoverCo != null) StopCoroutine(_hoverCo);
            _hoverCo = StartCoroutine(ScaleTo(clickScale, 0.06f));
        }

        public void OnPointerUp(PointerEventData _)
        {
            if (!_button.interactable) return;
            if (_hoverCo != null) StopCoroutine(_hoverCo);
            _hoverCo = StartCoroutine(UIAnimUtils.ClickBounce(transform, clickScale, 0.18f));
            if (background != null)
                background.color = _isHovered ? hoverColor : normalColor;
            // Quick inner flash on click
            if (innerGlow != null)
                StartCoroutine(ClickFlash());
        }

        // ══════════════════════════════════════════════════════
        //  PUBLIC API
        // ══════════════════════════════════════════════════════

        /// <summary>Set the button label text.</summary>
        public void SetLabel(string text)
        {
            if (label != null) label.text = text;
        }

        /// <summary>Set the icon sprite.</summary>
        public void SetIcon(Sprite sprite)
        {
            if (icon != null)
            {
                icon.sprite = sprite;
                icon.gameObject.SetActive(sprite != null);
            }
        }

        /// <summary>Programmatic enable/disable styling.</summary>
        public void SetInteractable(bool interactable)
        {
            _button.interactable = interactable;
            if (background != null)
                background.color = interactable ? normalColor : disabledColor;
            if (borderLine != null)
                borderLine.color = interactable ? borderNormal : new Color(borderNormal.r, borderNormal.g, borderNormal.b, 0.08f);
            if (label != null)
                label.alpha = interactable ? 1f : 0.4f;
            ShowGlow(false);
            // Stop breathing when disabled
            if (!interactable && _breathCo != null) { StopCoroutine(_breathCo); _breathCo = null; }
            if (interactable && enableIdleBreath && innerGlow != null && _breathCo == null)
                _breathCo = StartCoroutine(IdleBreathLoop());
        }

        public void SetGlowColor(Color c)
        {
            glowColor = c;
        }

        // ══════════════════════════════════════════════════════
        //  INTERNALS
        // ══════════════════════════════════════════════════════

        private void ShowGlow(bool on)
        {
            if (glowRing == null) return;
            StopCoroutine(nameof(AnimateGlow));
            StartCoroutine(AnimateGlow(on));
        }

        private IEnumerator AnimateGlow(bool on)
        {
            float start = glowRing.color.a;
            float end = on ? glowColor.a : 0f;
            float t = 0f;
            float dur = 0.15f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                Color c = glowColor;
                c.a = Mathf.Lerp(start, end, t / dur);
                glowRing.color = c;
                yield return null;
            }
            Color final_ = glowColor;
            final_.a = end;
            glowRing.color = final_;
        }

        private IEnumerator ScaleTo(float target, float dur)
        {
            Vector3 s = transform.localScale;
            Vector3 e = Vector3.one * target;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                transform.localScale = Vector3.Lerp(s, e, Mathf.Clamp01(t / dur));
                yield return null;
            }
            transform.localScale = e;
        }

        private IEnumerator ShineSweepLoop()
        {
            if (shineRect == null) yield break;
            float width = shineRect.rect.width > 0 ? shineRect.rect.width : 300f;
            while (true)
            {
                yield return new WaitForSecondsRealtime(shinePeriod);
                if (!_button.interactable) continue;
                yield return UIAnimUtils.ShineSweep(shineRect, width, 0.5f);
            }
        }

        private IEnumerator IdleBreathLoop()
        {
            if (innerGlow == null) yield break;
            while (true)
            {
                float t = (Mathf.Sin(Time.time * breathSpeed) + 1f) * 0.5f;
                Color c = glowColor;
                c.a = Mathf.Lerp(0.02f, 0.12f, t);
                innerGlow.color = c;
                yield return null;
            }
        }

        private IEnumerator ClickFlash()
        {
            if (innerGlow == null) yield break;
            Color c = glowColor;
            c.a = 0.45f;
            innerGlow.color = c;
            float t = 0f;
            while (t < 0.2f)
            {
                t += Time.unscaledDeltaTime;
                c.a = 0.45f * (1f - t / 0.2f);
                innerGlow.color = c;
                yield return null;
            }
        }
    }
}
