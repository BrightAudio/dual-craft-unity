// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Battle Board View
//  Visual framework for the battle screen. Manages zone
//  containers, highlights, phase banners, floating damage,
//  attack arrows, and conjuror HP displays.
//  Purely visual — does NOT own game state.
// ═══════════════════════════════════════════════════════
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DualCraft.Core;

namespace DualCraft.UI.Visual
{
    public class BattleBoardView : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        //  ZONE CONTAINERS  — assign from hierarchy
        // ══════════════════════════════════════════════════════
        [Header("Player Zones")]
        [SerializeField] private BoardZone playerHandZone;
        [SerializeField] private BoardZone playerDaemonZone;
        [SerializeField] private BoardZone playerPillarZone;
        [SerializeField] private BoardZone playerSealZone;
        [SerializeField] private BoardZone playerConjurorZone;

        [Header("Opponent Zones")]
        [SerializeField] private BoardZone opponentHandZone;
        [SerializeField] private BoardZone opponentDaemonZone;
        [SerializeField] private BoardZone opponentPillarZone;
        [SerializeField] private BoardZone opponentSealZone;
        [SerializeField] private BoardZone opponentConjurorZone;

        [Header("Shared Zones")]
        [SerializeField] private BoardZone domainZone;

        // ══════════════════════════════════════════════════════
        //  HUD ELEMENTS
        // ══════════════════════════════════════════════════════
        [Header("Turn / Phase")]
        [SerializeField] private TMP_Text turnCountText;
        [SerializeField] private TMP_Text phaseText;
        [SerializeField] private CanvasGroup phaseBannerGroup;
        [SerializeField] private RectTransform phaseBannerRect;

        [Header("Player Stats")]
        [SerializeField] private TMP_Text playerNameText;
        [SerializeField] private TMP_Text playerHpText;
        [SerializeField] private Image    playerHpFill;
        [SerializeField] private TMP_Text playerWillText;
        [SerializeField] private Image    playerWillIcon;
        [SerializeField] private TMP_Text playerDeckCountText;

        [Header("Opponent Stats")]
        [SerializeField] private TMP_Text opponentNameText;
        [SerializeField] private TMP_Text opponentHpText;
        [SerializeField] private Image    opponentHpFill;
        [SerializeField] private TMP_Text opponentWillText;
        [SerializeField] private TMP_Text opponentDeckCountText;

        [Header("Combat Feedback")]
        [SerializeField] private RectTransform floatingTextParent;
        [SerializeField] private TMP_Text floatingTextPrefab;
        [SerializeField] private Image screenFlashOverlay;
        [SerializeField] private LineRenderer attackLineRenderer;

        [Header("Board Canvas")]
        [SerializeField] private RectTransform boardRoot;
        [SerializeField] private Image boardBackground;
        [SerializeField] private Image boardGradientTop;
        [SerializeField] private Image boardGradientBottom;
        [SerializeField] private Image vignetteOverlay;
        [SerializeField] private Image centerSeparator;
        [SerializeField] private CanvasGroup boardGroup;

        [Header("Turn Indicators")]
        [SerializeField] private Image playerSideBorder;
        [SerializeField] private Image opponentSideBorder;
        [SerializeField] private CanvasGroup turnBannerGroup;
        [SerializeField] private TMP_Text turnBannerText;
        [SerializeField] private Image turnTimerBar;
        [SerializeField] private Image turnTimerBg;
        [SerializeField] private RectTransform phaseDotsParent;

        [Header("End State")]
        [SerializeField] private CanvasGroup endOverlayGroup;
        [SerializeField] private TMP_Text endTitleText;
        [SerializeField] private TMP_Text endSubtitleText;
        [SerializeField] private Image endOverlayBg;
        [SerializeField] private ParticleSystem victoryParticles;

        // ── Internals ────────────────────────────────────────
        private Coroutine _phaseBannerCo;
        private Coroutine _attackLineCo;
        private Coroutine _separatorCo;
        private readonly List<BoardZone> _allZones = new();
        private DualCraftVisualTheme Theme => DualCraftVisualTheme.I;

        // ══════════════════════════════════════════════════════
        //  LIFECYCLE
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            // Collect all assigned zones for batch operations
            BoardZone[] zones = {
                playerHandZone, playerDaemonZone, playerPillarZone,
                playerSealZone, playerConjurorZone,
                opponentHandZone, opponentDaemonZone, opponentPillarZone,
                opponentSealZone, opponentConjurorZone,
                domainZone
            };
            foreach (var z in zones)
                if (z != null) _allZones.Add(z);

            if (screenFlashOverlay != null)
                screenFlashOverlay.gameObject.SetActive(false);

            if (attackLineRenderer != null)
            {
                attackLineRenderer.positionCount = 0;
                attackLineRenderer.startWidth = 0.04f;
                attackLineRenderer.endWidth = 0.01f;
            }

            // Apply gradient bg layers
            if (Theme != null)
            {
                if (boardGradientTop != null) boardGradientTop.color = Theme.gradTopDark;
                if (boardGradientBottom != null) boardGradientBottom.color = Theme.gradBottomDark;
            }

            // Start separator glow animation
            if (centerSeparator != null)
                _separatorCo = StartCoroutine(AnimateSeparator());

            // Start hidden for intro
            if (boardGroup != null) boardGroup.alpha = 0f;
        }

        // ══════════════════════════════════════════════════════
        //  BOARD INTRO SEQUENCE  (fade in zones staggered)
        // ══════════════════════════════════════════════════════

        /// <summary>Animate the board appearing at match start.</summary>
        public void PlayBoardIntro()
        {
            StartCoroutine(BoardIntroSequence());
        }

        private IEnumerator BoardIntroSequence()
        {
            // Fade in board
            if (boardGroup != null)
                yield return UIAnimUtils.FadeIn(boardGroup, 0.4f);

            // Pop each zone in sequence (player side first, then opponent)
            BoardZone[] introOrder = {
                playerConjurorZone, playerHandZone, playerDaemonZone,
                playerPillarZone, playerSealZone,
                domainZone,
                opponentSealZone, opponentPillarZone, opponentDaemonZone,
                opponentHandZone, opponentConjurorZone
            };
            foreach (var zone in introOrder)
            {
                if (zone != null)
                    StartCoroutine(UIAnimUtils.PopScale(zone.transform, 0.2f));
                yield return new WaitForSeconds(0.05f);
            }

            // Flash the vignette softly
            if (vignetteOverlay != null)
                StartCoroutine(LerpAlpha(vignetteOverlay, 0.20f, 0.5f));
        }

        private IEnumerator AnimateSeparator()
        {
            if (centerSeparator == null) yield break;
            Color baseCol = Theme != null ? Theme.separator : new Color(0.4f, 0.35f, 0.65f, 0.18f);
            while (true)
            {
                float t = (Mathf.Sin(Time.time * 0.8f) + 1f) * 0.5f;
                Color c = baseCol;
                c.a = Mathf.Lerp(0.08f, 0.28f, t);
                centerSeparator.color = c;
                yield return null;
            }
        }

        // ══════════════════════════════════════════════════════
        //  ZONE HIGHLIGHTS
        // ══════════════════════════════════════════════════════

        /// <summary>Highlight a zone (targetable / active).</summary>
        public void SetZoneHighlight(BoardZone zone, bool on)
        {
            if (zone != null) zone.SetHighlight(on);
        }

        /// <summary>Pulse a zone glow (valid interaction this turn).</summary>
        public void SetZonePulse(BoardZone zone, bool on)
        {
            if (zone != null) zone.SetPulse(on);
        }

        /// <summary>Clear all zone highlights.</summary>
        public void ClearAllHighlights()
        {
            foreach (var z in _allZones)
            {
                z.SetHighlight(false);
                z.SetPulse(false);
            }
        }

        // ══════════════════════════════════════════════════════
        //  TURN / PHASE
        // ══════════════════════════════════════════════════════

        public void SetTurnCount(int turn)
        {
            if (turnCountText != null) turnCountText.text = $"Turn {turn}";
        }

        public void ShowPhaseBanner(GamePhase phase)
        {
            if (phaseText != null) phaseText.text = PhaseDisplayName(phase);
            if (phaseBannerGroup == null) return;
            if (_phaseBannerCo != null) StopCoroutine(_phaseBannerCo);
            _phaseBannerCo = StartCoroutine(PhaseBannerSequence(phase));
        }

        private IEnumerator PhaseBannerSequence(GamePhase phase)
        {
            // Flash color based on phase
            Color phaseColor = phase switch
            {
                GamePhase.Battle => Theme != null ? Theme.danger : Color.red,
                GamePhase.Draw   => Theme != null ? Theme.manaBlue : Color.cyan,
                _ => Theme != null ? Theme.gold : Color.yellow,
            };
            if (phaseText != null) phaseText.color = phaseColor;

            // Slide in + fade in
            phaseBannerGroup.alpha = 0f;
            if (phaseBannerRect != null)
                yield return UIAnimUtils.SlideIn(phaseBannerRect, new Vector2(0, 60f), 0.25f);
            yield return UIAnimUtils.FadeIn(phaseBannerGroup, 0.2f);
            // Hold
            yield return new WaitForSeconds(0.8f);
            // Fade text color to cream
            float t = 0f;
            Color target = Theme != null ? Theme.cream : Color.white;
            while (t < 0.3f)
            {
                t += Time.deltaTime;
                if (phaseText != null) phaseText.color = Color.Lerp(phaseColor, target, t / 0.3f);
                yield return null;
            }
            // Fade out
            yield return UIAnimUtils.FadeOut(phaseBannerGroup, 0.3f);
        }

        private static string PhaseDisplayName(GamePhase p) => p switch
        {
            GamePhase.Draw   => "DRAW PHASE",
            GamePhase.Main1  => "MAIN PHASE 1",
            GamePhase.Battle => "BATTLE PHASE",
            GamePhase.Main2  => "MAIN PHASE 2",
            GamePhase.End    => "END PHASE",
            _                => p.ToString().ToUpper(),
        };

        // ══════════════════════════════════════════════════════
        //  PLAYER STATS UPDATE
        // ══════════════════════════════════════════════════════

        public void SetPlayerName(string name)   { if (playerNameText   != null) playerNameText.text = name; }
        public void SetOpponentName(string name)  { if (opponentNameText != null) opponentNameText.text = name; }

        public void SetPlayerHP(int hp, int maxHp)
        {
            UpdateHP(playerHpText, playerHpFill, hp, maxHp);
        }

        public void SetOpponentHP(int hp, int maxHp)
        {
            UpdateHP(opponentHpText, opponentHpFill, hp, maxHp);
        }

        private void UpdateHP(TMP_Text label, Image fill, int hp, int maxHp)
        {
            if (label != null) label.text = $"{hp}/{maxHp}";
            if (fill != null)
            {
                float ratio = maxHp > 0 ? (float)hp / maxHp : 0f;
                fill.fillAmount = ratio;
                // Color shifts: green → yellow → red
                if (ratio > 0.5f) fill.color = Color.Lerp(new Color(0.95f, 0.85f, 0.15f), new Color(0.25f, 0.85f, 0.35f), (ratio - 0.5f) * 2f);
                else               fill.color = Color.Lerp(new Color(0.90f, 0.15f, 0.15f), new Color(0.95f, 0.85f, 0.15f), ratio * 2f);
            }
        }

        public void SetPlayerWill(int current, int max)
        {
            if (playerWillText != null) playerWillText.text = $"{current}/{max}";
            if (playerWillIcon != null) StartCoroutine(UIAnimUtils.CrystalPulse(playerWillIcon, new Color(0.3f, 0.5f, 1f, 1f), 0.35f));
        }

        public void SetOpponentWill(int current, int max)
        {
            if (opponentWillText != null) opponentWillText.text = $"{current}/{max}";
        }

        public void SetPlayerDeckCount(int count)   { if (playerDeckCountText   != null) playerDeckCountText.text = count.ToString(); }
        public void SetOpponentDeckCount(int count)  { if (opponentDeckCountText != null) opponentDeckCountText.text = count.ToString(); }

        // ══════════════════════════════════════════════════════
        //  FLOATING DAMAGE NUMBERS
        // ══════════════════════════════════════════════════════

        public void SpawnFloatingText(Vector2 position, string text, Color color, float scale = 1f)
        {
            if (floatingTextPrefab == null || floatingTextParent == null) return;
            TMP_Text instance = Instantiate(floatingTextPrefab, floatingTextParent);
            instance.text = text;
            instance.color = color;
            instance.fontSize *= scale;
            instance.rectTransform.anchoredPosition = position;
            instance.gameObject.SetActive(true);
            var theme = DualCraftVisualTheme.I;
            float speed = theme != null ? theme.floatTextSpeed : 120f;
            float life  = theme != null ? theme.floatTextLife : 1.2f;
            StartCoroutine(UIAnimUtils.FloatingText(instance, speed, life));
        }

        /// <summary>Quick damage number (red, big).</summary>
        public void SpawnDamageNumber(Vector2 pos, int damage, bool crit = false)
        {
            float sc = crit ? 1.5f : 1f;
            Color c = crit ? new Color(1f, 0.85f, 0.1f) : new Color(0.95f, 0.2f, 0.2f);
            string prefix = crit ? "CRIT " : "";
            SpawnFloatingText(pos, $"{prefix}-{damage}", c, sc);
        }

        /// <summary>Heal number (green).</summary>
        public void SpawnHealNumber(Vector2 pos, int amount)
        {
            SpawnFloatingText(pos, $"+{amount}", new Color(0.25f, 0.9f, 0.4f), 1.1f);
        }

        /// <summary>"WEAK" label (grey, smaller).</summary>
        public void SpawnWeakLabel(Vector2 pos)
        {
            SpawnFloatingText(pos, "WEAK", new Color(0.65f, 0.65f, 0.65f), 0.8f);
        }

        // ══════════════════════════════════════════════════════
        //  SCREEN FLASH
        // ══════════════════════════════════════════════════════

        public void FlashScreen(Color color, float duration = 0.3f)
        {
            if (screenFlashOverlay != null)
                StartCoroutine(UIAnimUtils.ScreenFlash(screenFlashOverlay, color, duration));
        }

        // ══════════════════════════════════════════════════════
        //  SCREEN SHAKE
        // ══════════════════════════════════════════════════════

        public void ShakeBoard(float magnitude = 8f, float duration = 0.25f)
        {
            if (boardRoot != null)
                StartCoroutine(UIAnimUtils.ScreenShake(boardRoot, magnitude, duration));
        }

        // ══════════════════════════════════════════════════════
        //  ATTACK LINE / ARC
        // ══════════════════════════════════════════════════════

        /// <summary>Draw a line from attacker world pos to target world pos.</summary>
        public void ShowAttackLine(Vector3 from, Vector3 to, Color color, float duration = 0.5f)
        {
            if (attackLineRenderer == null) return;
            if (_attackLineCo != null) StopCoroutine(_attackLineCo);
            _attackLineCo = StartCoroutine(AttackLineRoutine(from, to, color, duration));
        }

        private IEnumerator AttackLineRoutine(Vector3 from, Vector3 to, Color color, float duration)
        {
            attackLineRenderer.positionCount = 20;
            attackLineRenderer.startColor = color;
            attackLineRenderer.endColor = new Color(color.r, color.g, color.b, 0.2f);

            float arcHeight = Vector3.Distance(from, to) * 0.15f;
            for (int i = 0; i < 20; i++)
            {
                float t = i / 19f;
                Vector3 p = Vector3.Lerp(from, to, t);
                p.y += Mathf.Sin(t * Mathf.PI) * arcHeight;
                attackLineRenderer.SetPosition(i, p);
            }

            yield return new WaitForSeconds(duration);

            // Fade out
            float fade = 0.2f;
            float elapsed = 0f;
            Color startC = color;
            while (elapsed < fade)
            {
                elapsed += Time.deltaTime;
                float a = 1f - (elapsed / fade);
                attackLineRenderer.startColor = new Color(startC.r, startC.g, startC.b, a);
                attackLineRenderer.endColor = new Color(startC.r, startC.g, startC.b, a * 0.2f);
                yield return null;
            }
            attackLineRenderer.positionCount = 0;
        }

        // ══════════════════════════════════════════════════════
        //  CONJUROR HIT FEEDBACK
        // ══════════════════════════════════════════════════════

        public void PulseConjurorHit(bool isPlayer)
        {
            BoardZone zone = isPlayer ? playerConjurorZone : opponentConjurorZone;
            if (zone != null) zone.FlashHit();
            FlashScreen(new Color(0.9f, 0.15f, 0.15f, 1f), 0.25f);
            ShakeBoard(14f, 0.25f);
            // Hit-pause for impact
            StartCoroutine(UIAnimUtils.HitPause(Theme != null ? Theme.hitPauseTime : 0.04f));
        }

        // ══════════════════════════════════════════════════════
        //  PILLAR DESTROYED NOTIFICATION
        // ══════════════════════════════════════════════════════

        public void ShowPillarDestroyed(Vector2 position)
        {
            SpawnFloatingText(position, "PILLAR DESTROYED", new Color(0.95f, 0.75f, 0.15f), 1.3f);
            FlashScreen(new Color(0.95f, 0.75f, 0.15f), 0.3f);
            ShakeBoard(12f, 0.25f);
            StartCoroutine(UIAnimUtils.HitPause(0.06f));
        }

        // ══════════════════════════════════════════════════════
        //  BOARD AMBIENCE  (domain-based tint)
        // ══════════════════════════════════════════════════════

        public void SetBoardTint(Color tint, float duration = 1f)
        {
            if (boardBackground != null)
                StartCoroutine(LerpColor(boardBackground, tint, duration));
        }

        public void SetVignetteIntensity(float alpha, float duration = 0.5f)
        {
            if (vignetteOverlay != null)
                StartCoroutine(LerpAlpha(vignetteOverlay, alpha, duration));
        }

        private IEnumerator LerpColor(Image img, Color target, float dur)
        {
            Color start = img.color;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                img.color = Color.Lerp(start, target, t / dur);
                yield return null;
            }
            img.color = target;
        }

        private IEnumerator LerpAlpha(Image img, float targetA, float dur)
        {
            Color c = img.color;
            float startA = c.a;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                c.a = Mathf.Lerp(startA, targetA, t / dur);
                img.color = c;
                yield return null;
            }
            c.a = targetA;
            img.color = c;
        }

        // ════════════════════════════════════════════════
        //  ACTIVE PLAYER INDICATOR
        // ════════════════════════════════════════════════

        /// <summary>Highlight which player's turn it is.</summary>
        public void SetActivePlayer(bool isPlayerTurn)
        {
            Color active   = Theme != null ? Theme.activePlayerGlow : new Color(0.78f, 0.66f, 0.42f, 0.35f);
            Color inactive = Theme != null ? Theme.inactivePlayerGlow : new Color(0.3f, 0.28f, 0.4f, 0.1f);
            if (playerSideBorder != null)
                StartCoroutine(LerpColor(playerSideBorder, isPlayerTurn ? active : inactive, 0.4f));
            if (opponentSideBorder != null)
                StartCoroutine(LerpColor(opponentSideBorder, isPlayerTurn ? inactive : active, 0.4f));
        }

        // ════════════════════════════════════════════════
        //  TURN BANNER
        // ════════════════════════════════════════════════

        /// <summary>Flash a "YOUR TURN" or "OPPONENT'S TURN" banner.</summary>
        public void ShowTurnBanner(bool isPlayerTurn)
        {
            if (turnBannerGroup == null || turnBannerText == null) return;
            turnBannerText.text = isPlayerTurn ? "YOUR TURN" : "OPPONENT'S TURN";
            turnBannerText.color = isPlayerTurn
                ? (Theme != null ? Theme.gold : Color.yellow)
                : (Theme != null ? Theme.textSecondary : Color.grey);
            StartCoroutine(TurnBannerSequence());
        }

        private IEnumerator TurnBannerSequence()
        {
            if (turnBannerGroup == null) yield break;
            var rt = turnBannerGroup.GetComponent<RectTransform>();
            turnBannerGroup.alpha = 0f;
            turnBannerGroup.gameObject.SetActive(true);
            if (rt != null)
                yield return UIAnimUtils.SlideIn(rt, new Vector2(0f, 60f), 0.25f);
            yield return UIAnimUtils.FadeIn(turnBannerGroup, 0.15f);
            yield return new WaitForSeconds(1f);
            yield return UIAnimUtils.FadeOut(turnBannerGroup, 0.3f);
            turnBannerGroup.gameObject.SetActive(false);
        }

        // ════════════════════════════════════════════════
        //  TURN TIMER
        // ════════════════════════════════════════════════

        /// <summary>Update the turn timer bar (0 = empty, 1 = full).</summary>
        public void SetTurnTimer(float normalized)
        {
            if (turnTimerBar == null) return;
            turnTimerBar.fillAmount = normalized;
            Color full = Theme != null ? Theme.turnTimerFull : Color.green;
            Color low  = Theme != null ? Theme.turnTimerLow : Color.red;
            turnTimerBar.color = Color.Lerp(low, full, normalized);
        }

        // ════════════════════════════════════════════════
        //  PHASE PROGRESS DOTS
        // ════════════════════════════════════════════════

        /// <summary>Highlight phase dot by index (0-4).</summary>
        public void SetPhaseProgress(int activeIndex)
        {
            if (phaseDotsParent == null) return;
            for (int i = 0; i < phaseDotsParent.childCount; i++)
            {
                var dot = phaseDotsParent.GetChild(i).GetComponent<Image>();
                if (dot == null) continue;
                bool isActive = i == activeIndex;
                bool isPast = i < activeIndex;
                dot.color = isActive
                    ? (Theme != null ? Theme.gold : Color.yellow)
                    : isPast
                        ? (Theme != null ? Theme.textMuted : Color.grey)
                        : (Theme != null ? DualCraftVisualTheme.WithAlpha(Theme.textMuted, 0.25f) : new Color(0.4f, 0.4f, 0.4f, 0.25f));
                dot.transform.localScale = isActive ? Vector3.one * 1.3f : Vector3.one;
            }
        }

        // ════════════════════════════════════════════════
        //  VICTORY / DEFEAT OVERLAY
        // ════════════════════════════════════════════════

        /// <summary>Show victory or defeat end-of-game overlay.</summary>
        public void ShowEndState(bool victory, string subtitle = "")
        {
            if (endOverlayGroup == null) return;
            StartCoroutine(EndStateSequence(victory, subtitle));
        }

        private IEnumerator EndStateSequence(bool victory, string subtitle)
        {
            endOverlayGroup.alpha = 0f;
            endOverlayGroup.gameObject.SetActive(true);
            if (endOverlayBg != null)
                endOverlayBg.color = Theme != null ? Theme.overlayDim : new Color(0, 0, 0, 0.65f);

            Color titleColor = victory
                ? (Theme != null ? Theme.victoryColor : Color.yellow)
                : (Theme != null ? Theme.defeatColor : Color.red);

            if (endTitleText != null)
            {
                endTitleText.text = victory ? "VICTORY" : "DEFEAT";
                endTitleText.color = titleColor;
            }
            if (endSubtitleText != null)
            {
                endSubtitleText.text = subtitle;
                endSubtitleText.color = Theme != null ? Theme.textSecondary : Color.grey;
            }

            yield return UIAnimUtils.FadeIn(endOverlayGroup, 0.5f);
            if (endTitleText != null)
                StartCoroutine(UIAnimUtils.ElasticScale(endTitleText.transform, 0.6f));
            if (victory && victoryParticles != null)
                victoryParticles.Emit(80);
        }
    }

    // ══════════════════════════════════════════════════════════
    //  BOARD ZONE  — individual zone component
    //  Attach to each zone container in the Canvas hierarchy.
    // ══════════════════════════════════════════════════════════
    [System.Serializable]
    public class BoardZone : MonoBehaviour
    {
        [SerializeField] private ZoneType zoneType;
        [SerializeField] private Image background;
        [SerializeField] private Image glowFrame;
        [SerializeField] private Image outerGlow;
        [SerializeField] private Image highlightOverlay;
        [SerializeField] private Image borderLine;
        [SerializeField] private RectTransform cardContainer;
        [SerializeField] private TMP_Text zoneLabel;
        [SerializeField] private TMP_Text cardCountText;

        private Coroutine _pulseCo;
        private Coroutine _breathCo;
        private DualCraftVisualTheme Theme => DualCraftVisualTheme.I;

        public RectTransform CardContainer => cardContainer;
        public ZoneType Type => zoneType;

        private void Start()
        {
            // Apply theme colors
            if (Theme != null && background != null)
                background.color = Theme.GetZoneColor(zoneType);
            if (highlightOverlay != null)
            {
                Color c = Theme != null ? Theme.GetZoneGlow(zoneType) : Color.white;
                c.a = 0f;
                highlightOverlay.color = c;
            }
            if (glowFrame != null)
            {
                Color c = Theme != null ? Theme.GetZoneGlow(zoneType) : Color.white;
                c.a = 0.12f;
                glowFrame.color = c;
            }
            if (outerGlow != null)
            {
                Color c = Theme != null ? Theme.GetZoneGlow(zoneType) : Color.white;
                c.a = 0.04f;
                outerGlow.color = c;
            }
            if (borderLine != null)
            {
                Color c = Theme != null ? Theme.panelBorder : new Color(0.3f, 0.26f, 0.5f, 0.25f);
                borderLine.color = c;
            }

            // Start idle breathing glow
            _breathCo = StartCoroutine(IdleZoneBreath());
        }

        /// <summary>Update card count indicator.</summary>
        public void SetCardCount(int count)
        {
            if (cardCountText != null)
                cardCountText.text = count > 0 ? count.ToString() : "";
        }

        /// <summary>Turn on/off the solid highlight overlay.</summary>
        public void SetHighlight(bool on)
        {
            if (highlightOverlay == null) return;
            Color c = highlightOverlay.color;
            c.a = on ? 0.25f : 0f;
            highlightOverlay.color = c;
        }

        /// <summary>Start/stop a pulsing glow effect (multi-layer).</summary>
        public void SetPulse(bool on)
        {
            if (glowFrame == null) return;
            if (_pulseCo != null) { StopCoroutine(_pulseCo); _pulseCo = null; }
            if (on)
            {
                Color gc = Theme != null ? Theme.GetZoneGlow(zoneType) : new Color(1, 1, 1, 0.5f);
                _pulseCo = StartCoroutine(UIAnimUtils.GlowBorderPulse(glowFrame, outerGlow, gc));
            }
            else
            {
                Color c = glowFrame.color;
                c.a = 0.12f;
                glowFrame.color = c;
                if (outerGlow != null)
                {
                    Color o = outerGlow.color;
                    o.a = 0.04f;
                    outerGlow.color = o;
                }
            }
        }

        /// <summary>Quick red flash for hit feedback.</summary>
        public void FlashHit()
        {
            if (highlightOverlay == null) return;
            StartCoroutine(HitFlash());
        }

        private IEnumerator HitFlash()
        {
            Color danger = Theme != null ? Theme.danger : Color.red;
            highlightOverlay.color = new Color(danger.r, danger.g, danger.b, 0.55f);
            // Also flash border
            if (borderLine != null) borderLine.color = new Color(danger.r, danger.g, danger.b, 0.6f);
            yield return new WaitForSeconds(0.12f);
            float t = 0f;
            Color borderOrig = Theme != null ? Theme.panelBorder : new Color(0.3f, 0.26f, 0.5f, 0.25f);
            while (t < 0.3f)
            {
                t += Time.deltaTime;
                float p = t / 0.3f;
                Color c = danger;
                c.a = 0.55f * (1f - p);
                highlightOverlay.color = c;
                if (borderLine != null)
                    borderLine.color = Color.Lerp(new Color(danger.r, danger.g, danger.b, 0.6f), borderOrig, p);
                yield return null;
            }
            highlightOverlay.color = Color.clear;
            if (borderLine != null) borderLine.color = borderOrig;
        }

        private IEnumerator IdleZoneBreath()
        {
            if (glowFrame == null) yield break;
            Color gc = Theme != null ? Theme.GetZoneGlow(zoneType) : new Color(1, 1, 1, 0.5f);
            while (true)
            {
                float t = (Mathf.Sin(Time.time * 0.6f + (int)zoneType * 0.8f) + 1f) * 0.5f;
                Color c = gc;
                c.a = Mathf.Lerp(0.06f, 0.16f, t);
                glowFrame.color = c;
                if (outerGlow != null)
                {
                    Color o = gc;
                    o.a = Mathf.Lerp(0.02f, 0.06f, t);
                    outerGlow.color = o;
                }
                yield return null;
            }
        }
    }
}
