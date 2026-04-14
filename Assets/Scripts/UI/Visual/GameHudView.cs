// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Game HUD View (Top Bar)
//  Polished status bar showing turn, phase, names,
//  will, HP, deck count, hand count with animated
//  value changes and flash feedback.
// ═══════════════════════════════════════════════════════
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DualCraft.Core;

namespace DualCraft.UI.Visual
{
    public class GameHudView : MonoBehaviour
    {
        // ── Inspector ────────────────────────────────────────
        [Header("Layout")]
        [SerializeField] private CanvasGroup hudGroup;
        [SerializeField] private RectTransform hudRect;
        [SerializeField] private Image hudBackdrop;
        [SerializeField] private Image hudBorderBottom;

        [Header("Turn / Phase")]
        [SerializeField] private TMP_Text turnText;
        [SerializeField] private TMP_Text phaseText;
        [SerializeField] private Image phaseIcon;
        [SerializeField] private Image phaseBg;
        [SerializeField] private Image centerSeparator;

        [Header("Player Panel")]
        [SerializeField] private TMP_Text playerNameText;
        [SerializeField] private TMP_Text playerHpText;
        [SerializeField] private Image playerHpBar;
        [SerializeField] private Image playerHpBarBg;
        [SerializeField] private TMP_Text playerWillText;
        [SerializeField] private Image playerWillCrystal;
        [SerializeField] private Image playerWillGlow;
        [SerializeField] private TMP_Text playerDeckText;
        [SerializeField] private TMP_Text playerHandText;
        [SerializeField] private Image playerPanel;
        [SerializeField] private Image playerPanelBorder;

        [Header("Opponent Panel")]
        [SerializeField] private TMP_Text opponentNameText;
        [SerializeField] private TMP_Text opponentHpText;
        [SerializeField] private Image opponentHpBar;
        [SerializeField] private Image opponentHpBarBg;
        [SerializeField] private TMP_Text opponentWillText;
        [SerializeField] private Image opponentWillCrystal;
        [SerializeField] private Image opponentWillGlow;
        [SerializeField] private TMP_Text opponentDeckText;
        [SerializeField] private TMP_Text opponentHandText;
        [SerializeField] private Image opponentPanel;
        [SerializeField] private Image opponentPanelBorder;

        [Header("End Turn")]
        [SerializeField] private FancyButton endTurnButton;
        [SerializeField] private CanvasGroup endTurnGroup;

        [Header("Phase Progress")]
        [SerializeField] private RectTransform phaseDotsContainer;
        [SerializeField] private Image[] phaseDots;

        [Header("Turn Timer")]
        [SerializeField] private Image turnTimerBar;
        [SerializeField] private Image turnTimerBg;
        [SerializeField] private TMP_Text turnTimerText;

        [Header("Status Effects")]
        [SerializeField] private RectTransform statusEffectsParent;

        // ── Cached previous values for diff animation ────────
        private int _prevPlayerHp, _prevPlayerWill, _prevPlayerDeck, _prevPlayerHand;
        private int _prevOppHp, _prevOppWill, _prevOppDeck, _prevOppHand;
        private DualCraftVisualTheme Theme => DualCraftVisualTheme.I;

        // ══════════════════════════════════════════════════════
        //  LIFECYCLE
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            if (hudGroup != null) hudGroup.alpha = 0f;

            // Apply theme styling
            if (Theme != null)
            {
                if (hudBackdrop != null) hudBackdrop.color = Theme.panelSurface;
                if (hudBorderBottom != null) hudBorderBottom.color = Theme.panelBorder;
                if (centerSeparator != null) centerSeparator.color = Theme.separator;
                if (playerPanelBorder != null) playerPanelBorder.color = Theme.panelBorder;
                if (opponentPanelBorder != null) opponentPanelBorder.color = Theme.panelBorder;
                if (playerHpBarBg != null) playerHpBarBg.color = new Color(0.08f, 0.08f, 0.12f, 0.8f);
                if (opponentHpBarBg != null) opponentHpBarBg.color = new Color(0.08f, 0.08f, 0.12f, 0.8f);
            }
        }

        /// <summary>Animate the HUD sliding in from top.</summary>
        public void Show()
        {
            if (hudGroup == null) return;
            StartCoroutine(ShowSequence());
        }

        private IEnumerator ShowSequence()
        {
            if (hudRect != null)
                yield return UIAnimUtils.SlideIn(hudRect, new Vector2(0, 60f), 0.3f);
            yield return UIAnimUtils.FadeIn(hudGroup, 0.2f);
        }

        public void Hide()
        {
            if (hudGroup != null)
                StartCoroutine(UIAnimUtils.FadeOut(hudGroup, 0.2f));
        }

        // ══════════════════════════════════════════════════════
        //  TURN / PHASE
        // ══════════════════════════════════════════════════════

        public void SetTurn(int turn)
        {
            if (turnText != null) turnText.text = $"TURN {turn}";
        }

        public void SetPhase(GamePhase phase)
        {
            if (phaseText == null) return;
            phaseText.text = phase switch
            {
                GamePhase.Draw   => "DRAW",
                GamePhase.Main1  => "MAIN 1",
                GamePhase.Battle => "BATTLE",
                GamePhase.Main2  => "MAIN 2",
                GamePhase.End    => "END",
                _                => phase.ToString().ToUpper(),
            };

            // Phase-colored accent
            Color phaseColor = phase == GamePhase.Battle
                ? (Theme != null ? Theme.danger : Color.red)
                : (Theme != null ? Theme.gold : Color.yellow);

            phaseText.color = phaseColor;

            // Flash the phase icon
            if (phaseIcon != null)
                StartCoroutine(UIAnimUtils.CrystalPulse(phaseIcon, phaseColor, 0.4f));

            // Tint phase background
            if (phaseBg != null)
            {
                Color bg = phaseColor;
                bg.a = 0.12f;
                StartCoroutine(LerpImageColor(phaseBg, bg, 0.3f));
            }
        }

        // ══════════════════════════════════════════════════════
        //  PLAYER STATS
        // ══════════════════════════════════════════════════════

        public void SetPlayerName(string name) { if (playerNameText != null) playerNameText.text = name; }
        public void SetOpponentName(string name) { if (opponentNameText != null) opponentNameText.text = name; }

        public void SetPlayerHP(int hp, int maxHp)
        {
            AnimateStatChange(playerHpText, playerHpBar, playerPanel,
                ref _prevPlayerHp, hp, maxHp, "{0}/" + maxHp, true);
        }

        public void SetOpponentHP(int hp, int maxHp)
        {
            AnimateStatChange(opponentHpText, opponentHpBar, opponentPanel,
                ref _prevOppHp, hp, maxHp, "{0}/" + maxHp, true);
        }

        public void SetPlayerWill(int current, int max)
        {
            AnimateWill(playerWillText, playerWillCrystal, ref _prevPlayerWill, current, max);
        }

        public void SetOpponentWill(int current, int max)
        {
            AnimateWill(opponentWillText, opponentWillCrystal, ref _prevOppWill, current, max);
        }

        public void SetPlayerDeck(int count)
        {
            SetCountText(playerDeckText, ref _prevPlayerDeck, count);
        }

        public void SetOpponentDeck(int count)
        {
            SetCountText(opponentDeckText, ref _prevOppDeck, count);
        }

        public void SetPlayerHand(int count)
        {
            SetCountText(playerHandText, ref _prevPlayerHand, count);
        }

        public void SetOpponentHand(int count)
        {
            SetCountText(opponentHandText, ref _prevOppHand, count);
        }

        // ══════════════════════════════════════════════════════
        //  INTERNAL ANIMATION HELPERS
        // ══════════════════════════════════════════════════════

        private void AnimateStatChange(TMP_Text label, Image bar, Image panel,
            ref int prev, int value, int max, string fmt, bool isHp)
        {
            if (label != null && prev != value)
            {
                StartCoroutine(UIAnimUtils.AnimateNumber(label, prev, value, 0.35f, fmt));
                // Value punch on HP loss — scale + color flash
                if (isHp && value < prev)
                    StartCoroutine(UIAnimUtils.ValuePunch(label, Theme != null ? Theme.danger : Color.red, 1.3f, 0.25f));
                else if (isHp && value > prev)
                    StartCoroutine(UIAnimUtils.ValuePunch(label, Theme != null ? Theme.healing : Color.green, 1.2f, 0.2f));
            }
            if (bar != null && max > 0)
                StartCoroutine(LerpFill(bar, (float)value / max, 0.4f, isHp));
            // Flash panel on decrease
            if (value < prev && panel != null)
                StartCoroutine(FlashPanel(panel, Theme != null ? Theme.danger : Color.red));
            prev = value;
        }

        private void AnimateWill(TMP_Text label, Image crystal, ref int prev, int current, int max)
        {
            if (label != null)
            {
                if (prev != current)
                {
                    StartCoroutine(UIAnimUtils.AnimateNumber(label, prev, current, 0.3f, "{0}/" + max));
                    // Punch on will change
                    Color punchCol = current > prev
                        ? (Theme != null ? Theme.manaBlue : Color.cyan)
                        : (Theme != null ? Theme.textMuted : Color.grey);
                    StartCoroutine(UIAnimUtils.ValuePunch(label, punchCol, 1.2f, 0.2f));
                }
                else
                    label.text = $"{current}/{max}";
            }
            // Pulse crystal on will gain
            if (current > prev && crystal != null)
                StartCoroutine(UIAnimUtils.CrystalPulse(crystal, Theme != null ? Theme.manaBlue : Color.cyan, 0.4f));
            prev = current;
        }

        private void SetCountText(TMP_Text label, ref int prev, int value)
        {
            if (label == null) return;
            if (prev != value)
                StartCoroutine(UIAnimUtils.AnimateNumber(label, prev, value, 0.25f));
            prev = value;
        }

        private IEnumerator LerpFill(Image bar, float target, float dur, bool colorShift)
        {
            float start = bar.fillAmount;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = UIAnimUtils.EaseOutQuad(Mathf.Clamp01(t / dur));
                bar.fillAmount = Mathf.Lerp(start, target, p);
                if (colorShift)
                {
                    float r = bar.fillAmount;
                    if (r > 0.5f) bar.color = Color.Lerp(new Color(0.95f, 0.85f, 0.15f), new Color(0.25f, 0.85f, 0.35f), (r - 0.5f) * 2f);
                    else           bar.color = Color.Lerp(new Color(0.90f, 0.15f, 0.15f), new Color(0.95f, 0.85f, 0.15f), r * 2f);
                }
                yield return null;
            }
            bar.fillAmount = target;
        }

        private IEnumerator FlashPanel(Image panel, Color flash)
        {
            Color orig = panel.color;
            panel.color = flash;
            yield return new WaitForSeconds(0.1f);
            float t = 0f;
            while (t < 0.25f)
            {
                t += Time.deltaTime;
                panel.color = Color.Lerp(flash, orig, t / 0.25f);
                yield return null;
            }
            panel.color = orig;
        }

        private IEnumerator LerpImageColor(Image img, Color target, float dur)
        {
            Color start = img.color;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                img.color = Color.Lerp(start, target, Mathf.Clamp01(t / dur));
                yield return null;
            }
            img.color = target;
        }

        // ════════════════════════════════════════════════
        //  END TURN BUTTON
        // ════════════════════════════════════════════════

        /// <summary>Enable/disable the End Turn button with visual feedback.</summary>
        public void SetEndTurnEnabled(bool enabled)
        {
            if (endTurnButton != null) endTurnButton.SetInteractable(enabled);
            if (endTurnGroup != null)
            {
                StopCoroutine(nameof(PulseEndTurn));
                if (enabled) StartCoroutine(PulseEndTurn());
            }
        }

        private IEnumerator PulseEndTurn()
        {
            while (true)
            {
                float t = (Mathf.Sin(Time.time * 2f) + 1f) * 0.5f;
                if (endTurnGroup != null)
                    endTurnGroup.alpha = Mathf.Lerp(0.85f, 1f, t);
                yield return null;
            }
        }

        // ════════════════════════════════════════════════
        //  PHASE PROGRESS DOTS
        // ════════════════════════════════════════════════

        /// <summary>Highlight the current phase dot (0=Draw through 4=End).</summary>
        public void SetPhaseProgress(int activeIndex)
        {
            if (phaseDots == null) return;
            for (int i = 0; i < phaseDots.Length; i++)
            {
                if (phaseDots[i] == null) continue;
                bool active = i == activeIndex;
                bool past = i < activeIndex;
                phaseDots[i].color = active
                    ? (Theme != null ? Theme.gold : Color.yellow)
                    : past
                        ? (Theme != null ? Theme.textMuted : Color.grey)
                        : (Theme != null ? DualCraftVisualTheme.WithAlpha(Theme.textMuted, 0.25f) : new Color(0.4f, 0.4f, 0.4f, 0.25f));
                phaseDots[i].transform.localScale = active ? Vector3.one * 1.4f : Vector3.one;
            }
        }

        // ════════════════════════════════════════════════
        //  TURN TIMER
        // ════════════════════════════════════════════════

        /// <summary>Update the turn timer (0-1 normalized, plus seconds).</summary>
        public void SetTurnTimer(float normalized, int secondsLeft = -1)
        {
            if (turnTimerBar != null)
            {
                turnTimerBar.fillAmount = normalized;
                Color full = Theme != null ? Theme.turnTimerFull : Color.green;
                Color low  = Theme != null ? Theme.turnTimerLow : Color.red;
                turnTimerBar.color = Color.Lerp(low, full, normalized);
            }
            if (turnTimerText != null && secondsLeft >= 0)
                turnTimerText.text = $"{secondsLeft}s";
        }

        // ════════════════════════════════════════════════
        //  STATUS EFFECT ICONS
        // ════════════════════════════════════════════════

        /// <summary>Add a status effect icon to the HUD strip.</summary>
        public void AddStatusIcon(Sprite icon, Color tint, float duration = 0f)
        {
            if (statusEffectsParent == null) return;
            var go = new GameObject("StatusIcon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(statusEffectsParent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(28f, 28f);
            var img = go.GetComponent<Image>();
            img.sprite = icon;
            img.color = tint;
            StartCoroutine(UIAnimUtils.PopScale(go.transform, 0.2f));
            if (duration > 0f) StartCoroutine(RemoveStatusAfter(go, duration));
        }

        /// <summary>Clear all status effect icons.</summary>
        public void ClearStatusIcons()
        {
            if (statusEffectsParent == null) return;
            foreach (Transform child in statusEffectsParent) Destroy(child.gameObject);
        }

        private IEnumerator RemoveStatusAfter(GameObject go, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (go != null) Destroy(go);
        }
    }
}
