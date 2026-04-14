// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Combat Feedback View
//  Centralized handler for combat visual feedback:
//  floating damage, crit/weak/heal styling, pillar
//  destruction, conjuror hits, screen effects,
//  and attack line rendering.
// ═══════════════════════════════════════════════════════
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DualCraft.UI.Visual
{
    public class CombatFeedbackView : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────
        [Header("Floating Text")]
        [SerializeField] private RectTransform floatingTextParent;
        [SerializeField] private TMP_FontAsset floatingFont;
        [SerializeField] private float fontSize = 42f;

        [Header("Screen Effects")]
        [SerializeField] private Image screenFlashOverlay;
        [SerializeField] private RectTransform shakeTarget;
        [SerializeField] private Image impactVignette;

        [Header("Attack Line")]
        [SerializeField] private LineRenderer attackLine;
        [SerializeField] private Color attackLineColor = new Color(0.95f, 0.35f, 0.10f, 0.9f);

        [Header("Styling")]
        [SerializeField] private Color damageColor   = new Color(0.95f, 0.20f, 0.20f, 1f);
        [SerializeField] private Color critColor     = new Color(1.00f, 0.85f, 0.10f, 1f);
        [SerializeField] private Color weakColor     = new Color(0.55f, 0.55f, 0.55f, 1f);
        [SerializeField] private Color healColor     = new Color(0.25f, 0.90f, 0.40f, 1f);
        [SerializeField] private Color pillarColor   = new Color(0.95f, 0.75f, 0.15f, 1f);

        private Coroutine _attackLineCo;
        private int _comboCount;
        private float _lastHitTime;
        private DualCraftVisualTheme Theme => DualCraftVisualTheme.I;

        private void Awake()
        {
            if (screenFlashOverlay != null)
                screenFlashOverlay.gameObject.SetActive(false);
            if (attackLine != null)
                attackLine.positionCount = 0;
        }

        // ══════════════════════════════════════════════════════
        //  FLOATING TEXT SPAWNING
        // ══════════════════════════════════════════════════════

        /// <summary>Spawn a floating text label at the given anchored position.</summary>
        public void SpawnText(Vector2 anchoredPos, string text, Color color, float scale = 1f)
        {
            if (floatingTextParent == null) return;

            var go = new GameObject("FloatText", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(floatingTextParent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(400f, 100f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.color = color;
            tmp.fontSize = fontSize * scale;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;
            tmp.enableWordWrapping = false;
            if (floatingFont != null) tmp.font = floatingFont;

            // Thick outline + face dilate for readability
            tmp.outlineWidth = 0.25f;
            tmp.outlineColor = new Color32(0, 0, 0, 200);
            tmp.fontMaterial.SetFloat("_FaceDilate", 0.1f);

            // Random horizontal jitter to prevent overlap
            float jitter = UnityEngine.Random.Range(-25f, 25f);
            rt.anchoredPosition += new Vector2(jitter, 0f);

            float speed = Theme != null ? Theme.floatTextSpeed : 120f;
            float life  = Theme != null ? Theme.floatTextLife : 1.2f;
            StartCoroutine(UIAnimUtils.FloatingText(tmp, speed, life));
        }

        // ══════════════════════════════════════════════════════
        //  COMBAT EVENTS  (high-level API, call from battle)
        // ══════════════════════════════════════════════════════

        /// <summary>Standard damage number.</summary>
        public void ShowDamage(Vector2 pos, int amount)
        {
            SpawnText(pos, $"-{amount}", damageColor, 1f);
            TrackCombo(pos);
        }

        /// <summary>Element-colored damage.</summary>
        public void ShowElementDamage(Vector2 pos, int amount, DualCraft.Core.Element element)
        {
            Color elColor = Theme != null ? Theme.GetElementColor(element) : damageColor;
            SpawnText(pos, $"-{amount}", elColor, 1.1f);
            TrackCombo(pos);
        }

        /// <summary>Critical hit — big, gold, screen shake + hit-pause.</summary>
        public void ShowCritDamage(Vector2 pos, int amount)
        {
            SpawnText(pos, $"CRIT -{amount}", critColor, 1.5f);
            ShakeScreen(Theme != null ? Theme.critShakeIntensity : 14f, Theme != null ? Theme.critShakeDuration : 0.3f);
            FlashScreen(critColor, 0.25f);
            PulseImpactVignette(critColor);
            StartCoroutine(UIAnimUtils.HitPause(Theme != null ? Theme.hitPauseTime : 0.04f));
            TrackCombo(pos);
        }

        /// <summary>Weak hit — small, grey.</summary>
        public void ShowWeakDamage(Vector2 pos, int amount)
        {
            SpawnText(pos, $"-{amount}", weakColor, 0.8f);
            SpawnText(pos + Vector2.down * 30f, "WEAK", weakColor, 0.6f);
        }

        /// <summary>Heal — green, upward float.</summary>
        public void ShowHeal(Vector2 pos, int amount)
        {
            SpawnText(pos, $"+{amount}", healColor, 1.1f);
        }

        /// <summary>Overkill — when damage exceeds remaining HP.</summary>
        public void ShowOverkill(Vector2 pos, int amount)
        {
            Color overkillCol = Theme != null ? Theme.overkillColor : new Color(1f, 0.4f, 0.1f);
            SpawnText(pos, $"OVERKILL -{amount}", overkillCol, 1.6f);
            ShakeScreen(16f, 0.35f);
            FlashScreen(overkillCol, 0.3f);
            StartCoroutine(UIAnimUtils.HitPause(0.06f));
        }

        /// <summary>Immune — blocked by shield or effect.</summary>
        public void ShowImmune(Vector2 pos)
        {
            Color immuneCol = Theme != null ? Theme.immuneColor : new Color(0.5f, 0.7f, 1f);
            SpawnText(pos, "IMMUNE", immuneCol, 1.0f);
        }

        /// <summary>Blocked — reduced to zero.</summary>
        public void ShowBlocked(Vector2 pos)
        {
            Color blockedCol = Theme != null ? Theme.blockedColor : new Color(0.6f, 0.6f, 0.7f);
            SpawnText(pos, "BLOCKED", blockedCol, 0.9f);
        }

        /// <summary>Pillar destroyed — big gold flash + shake.</summary>
        public void ShowPillarDestroyed(Vector2 pos)
        {
            SpawnText(pos, "PILLAR DESTROYED", pillarColor, 1.3f);
            FlashScreen(pillarColor, 0.3f);
            ShakeScreen(12f, 0.25f);
            PulseImpactVignette(pillarColor);
            StartCoroutine(UIAnimUtils.HitPause(0.06f));
        }

        /// <summary>Conjuror hit — red flash, big shake, hit-pause.</summary>
        public void ShowConjurorHit(Vector2 pos, int damage, bool isPlayer)
        {
            SpawnText(pos, $"-{damage}", new Color(1f, 0.3f, 0.3f), 1.4f);
            FlashScreen(new Color(0.9f, 0.1f, 0.1f), 0.3f);
            ShakeScreen(16f, 0.35f);
            PulseImpactVignette(new Color(0.9f, 0.1f, 0.1f));
            StartCoroutine(UIAnimUtils.HitPause(Theme != null ? Theme.hitPauseTime : 0.04f));
        }

        // ══════════════════════════════════════════════════════
        //  COMBO TRACKING
        // ══════════════════════════════════════════════════════

        private void TrackCombo(Vector2 pos)
        {
            float now = Time.time;
            if (now - _lastHitTime < 1.5f)
            {
                _comboCount++;
                if (_comboCount >= 2)
                {
                    float comboScale = Theme != null ? Theme.comboTextScale : 1.8f;
                    Color comboCol = Theme != null ? Theme.gold : new Color(0.95f, 0.75f, 0.15f);
                    SpawnText(pos + Vector2.up * 50f, $"{_comboCount}x COMBO", comboCol, Mathf.Min(comboScale, 1f + _comboCount * 0.2f));
                }
            }
            else
            {
                _comboCount = 1;
            }
            _lastHitTime = now;
        }

        // ══════════════════════════════════════════════════════
        //  SCREEN FLASH
        // ══════════════════════════════════════════════════════

        public void FlashScreen(Color color, float duration = 0.3f)
        {
            if (screenFlashOverlay == null) return;
            StartCoroutine(UIAnimUtils.ScreenFlash(screenFlashOverlay, color, duration));
        }

        // ══════════════════════════════════════════════════════
        //  IMPACT VIGNETTE (colored edge pulse on big hits)
        // ══════════════════════════════════════════════════════

        public void PulseImpactVignette(Color color, float duration = 0.4f)
        {
            if (impactVignette == null) return;
            StartCoroutine(ImpactVignetteRoutine(color, duration));
        }

        private IEnumerator ImpactVignetteRoutine(Color color, float duration)
        {
            impactVignette.gameObject.SetActive(true);
            impactVignette.raycastTarget = false;
            Color c = color;
            c.a = 0.35f;
            impactVignette.color = c;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                c.a = 0.35f * (1f - UIAnimUtils.EaseOutQuad(t / duration));
                impactVignette.color = c;
                yield return null;
            }
            impactVignette.color = Color.clear;
            impactVignette.gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════
        //  SCREEN SHAKE
        // ══════════════════════════════════════════════════════

        public void ShakeScreen(float magnitude = 8f, float duration = 0.25f)
        {
            if (shakeTarget == null) return;
            StartCoroutine(UIAnimUtils.ScreenShake(shakeTarget, magnitude, duration));
        }

        // ══════════════════════════════════════════════════════
        //  ATTACK LINE / ARC
        // ══════════════════════════════════════════════════════

        /// <summary>Draw an arc from attacker to target, then fade.</summary>
        public void ShowAttackArc(Vector3 from, Vector3 to, float holdTime = 0.4f)
        {
            if (attackLine == null) return;
            if (_attackLineCo != null) StopCoroutine(_attackLineCo);
            _attackLineCo = StartCoroutine(AttackArcRoutine(from, to, holdTime));
        }

        /// <summary>Draw an arc using RectTransform anchored positions (UI space).</summary>
        public void ShowAttackArcUI(RectTransform from, RectTransform to, float holdTime = 0.4f)
        {
            if (from == null || to == null) return;
            Vector3 wFrom = from.position;
            Vector3 wTo = to.position;
            ShowAttackArc(wFrom, wTo, holdTime);
        }

        private IEnumerator AttackArcRoutine(Vector3 from, Vector3 to, float hold)
        {
            int segments = 24;
            attackLine.positionCount = segments;
            attackLine.startColor = attackLineColor;
            attackLine.endColor = new Color(attackLineColor.r, attackLineColor.g, attackLineColor.b, 0.3f);
            attackLine.startWidth = 0.05f;
            attackLine.endWidth = 0.02f;

            float arcH = Vector3.Distance(from, to) * 0.12f;

            // Build-up animation: draw line progressively
            float buildTime = 0.15f;
            float t = 0f;
            while (t < buildTime)
            {
                t += Time.deltaTime;
                float progress = Mathf.Clamp01(t / buildTime);
                int visiblePts = Mathf.Max(2, Mathf.RoundToInt(segments * progress));
                attackLine.positionCount = visiblePts;
                for (int i = 0; i < visiblePts; i++)
                {
                    float f = (float)i / (segments - 1);
                    Vector3 p = Vector3.Lerp(from, to, f);
                    p.y += Mathf.Sin(f * Mathf.PI) * arcH;
                    attackLine.SetPosition(i, p);
                }
                yield return null;
            }

            // Full line
            attackLine.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float f = (float)i / (segments - 1);
                Vector3 p = Vector3.Lerp(from, to, f);
                p.y += Mathf.Sin(f * Mathf.PI) * arcH;
                attackLine.SetPosition(i, p);
            }

            yield return new WaitForSeconds(hold);

            // Fade out
            float fade = 0.2f;
            t = 0f;
            while (t < fade)
            {
                t += Time.deltaTime;
                float a = 1f - (t / fade);
                attackLine.startColor = new Color(attackLineColor.r, attackLineColor.g, attackLineColor.b, a);
                attackLine.endColor = new Color(attackLineColor.r, attackLineColor.g, attackLineColor.b, a * 0.3f);
                yield return null;
            }
            attackLine.positionCount = 0;
        }

        // ════════════════════════════════════════════════
        //  SUMMON FLASH  (card played → board glow burst)
        // ════════════════════════════════════════════════

        /// <summary>Flash effect when a card is summoned to the board.</summary>
        public void ShowSummonFlash(Vector2 pos, Color elementColor)
        {
            SpawnText(pos, "SUMMON", elementColor, 1.2f);
            FlashScreen(DualCraftVisualTheme.WithAlpha(elementColor, 0.3f), 0.2f);
        }

        // ════════════════════════════════════════════════
        //  STATUS EFFECT FLOAT  (buff/debuff applied)
        // ════════════════════════════════════════════════

        /// <summary>Show a floating status effect label.</summary>
        public void ShowStatusApplied(Vector2 pos, string statusName, Color color)
        {
            SpawnText(pos + Vector2.up * 40f, statusName, color, 0.85f);
        }

        // ════════════════════════════════════════════════
        //  CARD ACTIVATION GLOW
        // ════════════════════════════════════════════════

        /// <summary>Glow burst at a card's position when it activates an ability.</summary>
        public void ShowCardActivation(Vector2 pos, Color glowColor)
        {
            FlashScreen(DualCraftVisualTheme.WithAlpha(glowColor, 0.15f), 0.15f);
            SpawnText(pos, "✦", glowColor, 1.8f);
        }

        // ════════════════════════════════════════════════
        //  VICTORY / DEFEAT TEXT BURST
        // ════════════════════════════════════════════════

        /// <summary>Big centered text for end-of-game result.</summary>
        public void ShowGameResult(bool victory)
        {
            var theme = DualCraftVisualTheme.I;
            string text = victory ? "VICTORY!" : "DEFEAT";
            Color color = victory
                ? (theme != null ? theme.victoryColor : Color.yellow)
                : (theme != null ? theme.defeatColor : Color.red);
            SpawnText(Vector2.zero, text, color, 2.5f);
            if (victory)
                FlashScreen(DualCraftVisualTheme.WithAlpha(color, 0.25f), 0.4f);
        }
    }
}
