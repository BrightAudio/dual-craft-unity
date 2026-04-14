// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — UI Animation Utilities
//  Lightweight coroutine-based tweens.  No paid deps.
//  Every helper returns a Coroutine so callers can
//  yield or stop it.
// ═══════════════════════════════════════════════════════
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DualCraft.UI.Visual
{
    public static class UIAnimUtils
    {
        // ── Easing curves ────────────────────────────────────
        public static float EaseOutBack(float t)
        {
            const float c = 1.70158f;
            return 1f + (c + 1f) * Mathf.Pow(t - 1f, 3f) + c * Mathf.Pow(t - 1f, 2f);
        }
        public static float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);
        public static float EaseInOutCubic(float t) =>
            t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;

        // ══════════════════════════════════════════════════════
        //  FADE  (CanvasGroup)
        // ══════════════════════════════════════════════════════
        public static IEnumerator FadeIn(CanvasGroup cg, float duration = 0.25f, Action onComplete = null)
        {
            cg.blocksRaycasts = true;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                cg.alpha = Mathf.Clamp01(t / duration);
                yield return null;
            }
            cg.alpha = 1f;
            onComplete?.Invoke();
        }

        public static IEnumerator FadeOut(CanvasGroup cg, float duration = 0.25f, Action onComplete = null)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                cg.alpha = 1f - Mathf.Clamp01(t / duration);
                yield return null;
            }
            cg.alpha = 0f;
            cg.blocksRaycasts = false;
            onComplete?.Invoke();
        }

        // ══════════════════════════════════════════════════════
        //  POP SCALE  (bounce-in)
        // ══════════════════════════════════════════════════════
        public static IEnumerator PopScale(Transform tr, float duration = 0.2f, float overshoot = 1.15f)
        {
            float t = 0f;
            Vector3 target = tr.localScale;
            tr.localScale = Vector3.zero;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / duration);
                float s = EaseOutBack(p);
                tr.localScale = target * s;
                yield return null;
            }
            tr.localScale = target;
        }

        // ══════════════════════════════════════════════════════
        //  SLIDE IN  (from offset)
        // ══════════════════════════════════════════════════════
        public static IEnumerator SlideIn(RectTransform rt, Vector2 fromOffset, float duration = 0.35f)
        {
            Vector2 target = rt.anchoredPosition;
            Vector2 start = target + fromOffset;
            rt.anchoredPosition = start;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float p = EaseOutQuad(Mathf.Clamp01(t / duration));
                rt.anchoredPosition = Vector2.Lerp(start, target, p);
                yield return null;
            }
            rt.anchoredPosition = target;
        }

        // ══════════════════════════════════════════════════════
        //  PULSE GLOW  (loops — caller stops it)
        // ══════════════════════════════════════════════════════
        public static IEnumerator PulseGlow(Image img, Color glowColor, float period = 0.6f, float minAlpha = 0.15f, float maxAlpha = 0.65f)
        {
            while (true)
            {
                float t = Mathf.PingPong(Time.time / period, 1f);
                Color c = glowColor;
                c.a = Mathf.Lerp(minAlpha, maxAlpha, t);
                img.color = c;
                yield return null;
            }
        }

        // ══════════════════════════════════════════════════════
        //  BUTTON HOVER GROW
        // ══════════════════════════════════════════════════════
        public static IEnumerator HoverGrow(Transform tr, float targetScale = 1.08f, float duration = 0.12f)
        {
            Vector3 start = tr.localScale;
            Vector3 end = Vector3.one * targetScale;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                tr.localScale = Vector3.Lerp(start, end, Mathf.Clamp01(t / duration));
                yield return null;
            }
            tr.localScale = end;
        }

        public static IEnumerator HoverShrink(Transform tr, float duration = 0.10f)
        {
            Vector3 start = tr.localScale;
            Vector3 end = Vector3.one;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                tr.localScale = Vector3.Lerp(start, end, Mathf.Clamp01(t / duration));
                yield return null;
            }
            tr.localScale = end;
        }

        // ══════════════════════════════════════════════════════
        //  CLICK BOUNCE  (quick scale down then back)
        // ══════════════════════════════════════════════════════
        public static IEnumerator ClickBounce(Transform tr, float downScale = 0.90f, float duration = 0.15f)
        {
            float half = duration * 0.4f;
            Vector3 normal = tr.localScale;
            Vector3 down = normal * downScale;
            // scale down
            float t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                tr.localScale = Vector3.Lerp(normal, down, Mathf.Clamp01(t / half));
                yield return null;
            }
            // bounce back
            t = 0f;
            float rest = duration - half;
            while (t < rest)
            {
                t += Time.unscaledDeltaTime;
                float p = EaseOutBack(Mathf.Clamp01(t / rest));
                tr.localScale = Vector3.Lerp(down, normal, p);
                yield return null;
            }
            tr.localScale = normal;
        }

        // ══════════════════════════════════════════════════════
        //  FLOATING COMBAT TEXT
        // ══════════════════════════════════════════════════════
        public static IEnumerator FloatingText(TMP_Text label, float speed = 120f, float life = 1.2f)
        {
            RectTransform rt = label.rectTransform;
            CanvasGroup cg = label.GetComponent<CanvasGroup>();
            if (cg == null) cg = label.gameObject.AddComponent<CanvasGroup>();
            float t = 0f;
            Vector2 start = rt.anchoredPosition;
            while (t < life)
            {
                t += Time.deltaTime;
                rt.anchoredPosition = start + Vector2.up * speed * (t / life);
                cg.alpha = 1f - Mathf.Pow(t / life, 2f);
                float sc = 1f + 0.2f * Mathf.Sin(t * 6f);
                rt.localScale = Vector3.one * sc;
                yield return null;
            }
            UnityEngine.Object.Destroy(label.gameObject);
        }

        // ══════════════════════════════════════════════════════
        //  SCREEN SHAKE  (applied to a RectTransform / Camera)
        // ══════════════════════════════════════════════════════
        public static IEnumerator ScreenShake(Transform target, float magnitude = 8f, float duration = 0.25f)
        {
            Vector3 origin = target.localPosition;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float decay = 1f - (t / duration);
                float x = UnityEngine.Random.Range(-1f, 1f) * magnitude * decay;
                float y = UnityEngine.Random.Range(-1f, 1f) * magnitude * decay;
                target.localPosition = origin + new Vector3(x, y, 0f);
                yield return null;
            }
            target.localPosition = origin;
        }

        // ══════════════════════════════════════════════════════
        //  SOFT ROTATION SWAY  (looping)
        // ══════════════════════════════════════════════════════
        public static IEnumerator RotationSway(Transform tr, float angle = 3f, float speed = 1.5f)
        {
            Quaternion origin = tr.localRotation;
            while (true)
            {
                float z = Mathf.Sin(Time.time * speed) * angle;
                tr.localRotation = origin * Quaternion.Euler(0f, 0f, z);
                yield return null;
            }
        }

        // ══════════════════════════════════════════════════════
        //  MANA / WILL CRYSTAL PULSE
        // ══════════════════════════════════════════════════════
        public static IEnumerator CrystalPulse(Image img, Color baseColor, float duration = 0.5f)
        {
            Color bright = baseColor * 1.6f;
            bright.a = 1f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI);
                img.color = Color.Lerp(baseColor, bright, p);
                yield return null;
            }
            img.color = baseColor;
        }

        // ══════════════════════════════════════════════════════
        //  VALUE COUNTER  (number counts up/down)
        // ══════════════════════════════════════════════════════
        public static IEnumerator AnimateNumber(TMP_Text label, int from, int to, float duration = 0.4f, string format = "{0}")
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float p = EaseOutQuad(Mathf.Clamp01(t / duration));
                int current = Mathf.RoundToInt(Mathf.Lerp(from, to, p));
                label.text = string.Format(format, current);
                yield return null;
            }
            label.text = string.Format(format, to);
        }

        // ══════════════════════════════════════════════════════
        //  SCREEN FLASH  (full-screen overlay)
        // ══════════════════════════════════════════════════════
        public static IEnumerator ScreenFlash(Image overlay, Color color, float duration = 0.3f)
        {
            overlay.gameObject.SetActive(true);
            overlay.raycastTarget = false;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                Color c = color;
                c.a = (1f - Mathf.Clamp01(t / duration)) * 0.45f;
                overlay.color = c;
                yield return null;
            }
            overlay.color = Color.clear;
            overlay.gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════
        //  SHINE SWEEP (sweep a highlight across a RectTransform)
        // ══════════════════════════════════════════════════════
        public static IEnumerator ShineSweep(RectTransform shine, float width, float duration = 0.6f)
        {
            float startX = -width;
            float endX = width;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / duration);
                float x = Mathf.Lerp(startX, endX, p);
                shine.anchoredPosition = new Vector2(x, shine.anchoredPosition.y);
                yield return null;
            }
        }

        // ══════════════════════════════════════════════════════
        //  ELASTIC BOUNCE  (more dramatic than ClickBounce)
        // ══════════════════════════════════════════════════════
        public static float EaseOutElastic(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            float p = 0.3f;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t - p / 4f) * (2f * Mathf.PI) / p) + 1f;
        }

        public static IEnumerator ElasticScale(Transform tr, float duration = 0.5f)
        {
            Vector3 target = tr.localScale;
            tr.localScale = Vector3.zero;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float s = EaseOutElastic(Mathf.Clamp01(t / duration));
                tr.localScale = target * s;
                yield return null;
            }
            tr.localScale = target;
        }

        // ══════════════════════════════════════════════════════
        //  BREATHING GLOW  (idle aura — subtle, continuous)
        // ══════════════════════════════════════════════════════
        public static IEnumerator BreathingGlow(Image img, Color baseColor, float speed = 1.2f, float minAlpha = 0.08f, float maxAlpha = 0.30f)
        {
            while (true)
            {
                float t = (Mathf.Sin(Time.time * speed) + 1f) * 0.5f;
                Color c = baseColor;
                c.a = Mathf.Lerp(minAlpha, maxAlpha, t);
                img.color = c;
                yield return null;
            }
        }

        // ══════════════════════════════════════════════════════
        //  MULTI-BAND SHIMMER  (two shine bars at different speeds)
        // ══════════════════════════════════════════════════════
        public static IEnumerator ShimmerSweep(RectTransform shine1, RectTransform shine2, float width, float duration = 0.8f)
        {
            float startX = -width;
            float endX = width;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float p1 = Mathf.Clamp01(t / duration);
                float p2 = Mathf.Clamp01((t - 0.08f) / duration);
                if (shine1 != null)
                    shine1.anchoredPosition = new Vector2(Mathf.Lerp(startX, endX, p1), shine1.anchoredPosition.y);
                if (shine2 != null)
                    shine2.anchoredPosition = new Vector2(Mathf.Lerp(startX, endX, Mathf.Max(0f, p2)), shine2.anchoredPosition.y);
                yield return null;
            }
        }

        // ══════════════════════════════════════════════════════
        //  TYPEWRITER TEXT REVEAL
        // ══════════════════════════════════════════════════════
        public static IEnumerator TypewriterReveal(TMP_Text label, string fullText, float charDelay = 0.03f)
        {
            label.text = "";
            for (int i = 0; i < fullText.Length; i++)
            {
                label.text = fullText.Substring(0, i + 1);
                yield return new WaitForSecondsRealtime(charDelay);
            }
        }

        // ══════════════════════════════════════════════════════
        //  STAGGER-IN GROUP  (animate children one by one)
        // ══════════════════════════════════════════════════════
        public static IEnumerator StaggerIn(Transform parent, Vector2 fromOffset, float perChild = 0.06f, float slideDur = 0.3f)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                var rt = child as RectTransform;
                var cg = child.GetComponent<CanvasGroup>();
                if (rt != null)
                {
                    MonoBehaviour mb = child.GetComponent<MonoBehaviour>();
                    if (mb != null)
                        mb.StartCoroutine(SlideIn(rt, fromOffset, slideDur));
                }
                if (cg != null) cg.alpha = 1f;
                yield return new WaitForSecondsRealtime(perChild);
            }
        }

        // ══════════════════════════════════════════════════════
        //  RADIAL BURST  (expanding ring effect)
        // ══════════════════════════════════════════════════════
        public static IEnumerator RadialBurst(RectTransform ring, float maxScale = 4f, float duration = 0.5f)
        {
            var img = ring.GetComponent<Image>();
            Color startCol = img != null ? img.color : Color.white;
            ring.localScale = Vector3.zero;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / duration);
                ring.localScale = Vector3.one * (EaseOutQuad(p) * maxScale);
                if (img != null)
                {
                    Color c = startCol;
                    c.a = startCol.a * (1f - p);
                    img.color = c;
                }
                yield return null;
            }
            if (img != null) img.color = Color.clear;
        }

        // ══════════════════════════════════════════════════════
        //  CARD FLIP  (Y-axis 3D rotation illusion)
        // ══════════════════════════════════════════════════════
        public static IEnumerator CardFlip(RectTransform card, System.Action onMidpoint = null, float duration = 0.4f)
        {
            float half = duration * 0.5f;
            float t = 0f;
            // First half: rotate to 90
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / half);
                float angle = EaseInOutCubic(p) * 90f;
                card.localRotation = Quaternion.Euler(0f, angle, 0f);
                card.localScale = new Vector3(1f - p * 0.15f, 1f, 1f);
                yield return null;
            }
            // Midpoint callback (swap face)
            onMidpoint?.Invoke();
            // Second half: rotate from -90 to 0
            t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / half);
                float angle = 90f - EaseOutBack(p) * 90f;
                card.localRotation = Quaternion.Euler(0f, -angle, 0f);
                card.localScale = new Vector3(1f - (1f - p) * 0.15f, 1f, 1f);
                yield return null;
            }
            card.localRotation = Quaternion.identity;
            card.localScale = Vector3.one;
        }

        // ══════════════════════════════════════════════════════
        //  VALUE PUNCH  (scale + color flash on stat change)
        // ══════════════════════════════════════════════════════
        public static IEnumerator ValuePunch(TMP_Text label, Color flashColor, float scale = 1.35f, float duration = 0.3f)
        {
            Color orig = label.color;
            Vector3 origScale = label.transform.localScale;
            label.color = flashColor;
            label.transform.localScale = origScale * scale;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / duration);
                label.color = Color.Lerp(flashColor, orig, EaseOutQuad(p));
                label.transform.localScale = Vector3.Lerp(origScale * scale, origScale, EaseOutBack(p));
                yield return null;
            }
            label.color = orig;
            label.transform.localScale = origScale;
        }

        // ══════════════════════════════════════════════════════
        //  RAINBOW CYCLE  (for Legendary rarity)
        // ══════════════════════════════════════════════════════
        public static IEnumerator RainbowCycle(Image img, float speed = 0.5f, float saturation = 0.7f)
        {
            while (true)
            {
                float hue = Mathf.Repeat(Time.time * speed, 1f);
                img.color = Color.HSVToRGB(hue, saturation, 1f);
                yield return null;
            }
        }

        // ══════════════════════════════════════════════════════
        //  HIT PAUSE  (brief freeze-frame for impact)
        // ══════════════════════════════════════════════════════
        public static IEnumerator HitPause(float pauseTime = 0.04f)
        {
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(pauseTime);
            Time.timeScale = 1f;
        }

        // ══════════════════════════════════════════════════════
        //  SCENE TRANSITION WIPE  (black overlay sweep)
        // ══════════════════════════════════════════════════════
        public static IEnumerator TransitionWipe(CanvasGroup overlay, float duration = 0.6f, System.Action onMid = null)
        {
            float half = duration * 0.5f;
            overlay.alpha = 0f;
            overlay.blocksRaycasts = true;
            overlay.gameObject.SetActive(true);
            float t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                overlay.alpha = EaseInOutCubic(Mathf.Clamp01(t / half));
                yield return null;
            }
            overlay.alpha = 1f;
            onMid?.Invoke();
            t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                overlay.alpha = 1f - EaseInOutCubic(Mathf.Clamp01(t / half));
                yield return null;
            }
            overlay.alpha = 0f;
            overlay.blocksRaycasts = false;
        }

        // ══════════════════════════════════════════════════════
        //  GENTLE FLOAT  (idle up/down bobbing)
        // ══════════════════════════════════════════════════════
        public static IEnumerator GentleFloat(RectTransform rt, float amplitude = 6f, float speed = 1f)
        {
            Vector2 origin = rt.anchoredPosition;
            while (true)
            {
                float y = Mathf.Sin(Time.time * speed) * amplitude;
                rt.anchoredPosition = origin + new Vector2(0f, y);
                yield return null;
            }
        }

        // ══════════════════════════════════════════════════════
        //  GLOW BORDER PULSE  (zone/panel frame multi-layer glow)
        // ══════════════════════════════════════════════════════
        public static IEnumerator GlowBorderPulse(Image innerGlow, Image outerGlow, Color color, float speed = 1.2f)
        {
            while (true)
            {
                float t = (Mathf.Sin(Time.time * speed) + 1f) * 0.5f;
                if (innerGlow != null)
                {
                    Color c = color;
                    c.a = Mathf.Lerp(0.15f, 0.45f, t);
                    innerGlow.color = c;
                }
                if (outerGlow != null)
                {
                    Color c = color;
                    c.a = Mathf.Lerp(0.05f, 0.18f, t) * 0.6f;
                    outerGlow.color = c;
                }
                yield return null;
            }
        }

        // ════════════════════════════════════════════════
        //  SPRING SCALE  (critically damped spring physics)
        // ════════════════════════════════════════════════
        public static IEnumerator SpringScale(Transform tr, Vector3 target, float stiffness = 300f, float damping = 20f)
        {
            Vector3 current = tr.localScale;
            Vector3 velocity = Vector3.zero;
            float threshold = 0.001f;
            while (true)
            {
                Vector3 delta = target - current;
                if (delta.sqrMagnitude < threshold && velocity.sqrMagnitude < threshold) break;
                velocity += (delta * stiffness - velocity * damping) * Time.unscaledDeltaTime;
                current += velocity * Time.unscaledDeltaTime;
                tr.localScale = current;
                yield return null;
            }
            tr.localScale = target;
        }

        // ════════════════════════════════════════════════
        //  PANEL REVEAL  (scale up from 85% + fade in)
        // ════════════════════════════════════════════════
        public static IEnumerator PanelReveal(CanvasGroup cg, float duration = 0.35f)
        {
            Transform tr = cg.transform;
            Vector3 target = tr.localScale;
            tr.localScale = target * 0.85f;
            cg.alpha = 0f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float p = EaseOutQuad(Mathf.Clamp01(t / duration));
                tr.localScale = Vector3.Lerp(target * 0.85f, target, p);
                cg.alpha = p;
                yield return null;
            }
            tr.localScale = target;
            cg.alpha = 1f;
            cg.blocksRaycasts = true;
        }

        // ════════════════════════════════════════════════
        //  ARC MOVE  (parabolic path for card dealing)
        // ════════════════════════════════════════════════
        public static IEnumerator ArcMoveTo(RectTransform rt, Vector2 target, float arcHeight = 80f, float duration = 0.4f)
        {
            Vector2 start = rt.anchoredPosition;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float p = EaseInOutCubic(Mathf.Clamp01(t / duration));
                Vector2 pos = Vector2.Lerp(start, target, p);
                pos.y += Mathf.Sin(p * Mathf.PI) * arcHeight;
                rt.anchoredPosition = pos;
                yield return null;
            }
            rt.anchoredPosition = target;
        }

        // ════════════════════════════════════════════════
        //  CROSSFADE IMAGES  (smooth blend between two Images)
        // ════════════════════════════════════════════════
        public static IEnumerator CrossfadeImages(Image from, Image to, float duration = 0.5f)
        {
            Color fromCol = from != null ? from.color : Color.white;
            Color toCol = to != null ? to.color : Color.white;
            if (to != null) { Color c = toCol; c.a = 0f; to.color = c; to.gameObject.SetActive(true); }
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / duration);
                if (from != null) { Color c = fromCol; c.a = 1f - p; from.color = c; }
                if (to != null) { Color c = toCol; c.a = p; to.color = c; }
                yield return null;
            }
            if (from != null) from.gameObject.SetActive(false);
            if (to != null) to.color = toCol;
        }

        // ════════════════════════════════════════════════
        //  PULSE SCALE  (looping idle breath for icons/badges)
        // ════════════════════════════════════════════════
        public static IEnumerator PulseScale(Transform tr, float minScale = 0.95f, float maxScale = 1.05f, float speed = 1.5f)
        {
            Vector3 baseScale = tr.localScale;
            while (true)
            {
                float t = (Mathf.Sin(Time.time * speed) + 1f) * 0.5f;
                tr.localScale = baseScale * Mathf.Lerp(minScale, maxScale, t);
                yield return null;
            }
        }

        // ════════════════════════════════════════════════
        //  PROGRESS FILL  (animated bar fill with easing)
        // ════════════════════════════════════════════════
        public static IEnumerator ProgressFill(Image bar, float target, float duration = 0.5f)
        {
            float start = bar.fillAmount;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                bar.fillAmount = Mathf.Lerp(start, target, EaseOutQuad(Mathf.Clamp01(t / duration)));
                yield return null;
            }
            bar.fillAmount = target;
        }

        // ════════════════════════════════════════════════
        //  RIPPLE EFFECT  (expanding circle at click point)
        // ════════════════════════════════════════════════
        public static IEnumerator RippleEffect(RectTransform parent, Vector2 localPos, Color color, float maxRadius = 120f, float duration = 0.4f)
        {
            var go = new GameObject("Ripple", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = localPos;
            rt.sizeDelta = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / duration);
                float size = EaseOutQuad(p) * maxRadius * 2f;
                rt.sizeDelta = new Vector2(size, size);
                Color c = color; c.a = color.a * (1f - p);
                img.color = c;
                yield return null;
            }
            UnityEngine.Object.Destroy(go);
        }

        // ════════════════════════════════════════════════
        //  GLOW INTENSITY PULSE  (HDR-style brightness cycle)
        // ════════════════════════════════════════════════
        public static IEnumerator GlowIntensityPulse(Image img, float minI = 0.8f, float maxI = 1.5f, float speed = 1f)
        {
            Color baseColor = img.color;
            while (true)
            {
                float t = (Mathf.Sin(Time.time * speed) + 1f) * 0.5f;
                float i = Mathf.Lerp(minI, maxI, t);
                img.color = new Color(baseColor.r * i, baseColor.g * i, baseColor.b * i, baseColor.a);
                yield return null;
            }
        }

        // ════════════════════════════════════════════════
        //  BANNER SLIDE  (slide down, hold, slide up)
        // ════════════════════════════════════════════════
        public static IEnumerator BannerSlide(CanvasGroup cg, RectTransform rt, float holdTime = 1.2f)
        {
            cg.alpha = 0f;
            yield return SlideIn(rt, new Vector2(0f, 80f), 0.25f);
            yield return FadeIn(cg, 0.15f);
            yield return new WaitForSeconds(holdTime);
            yield return FadeOut(cg, 0.25f);
        }
    }
}
