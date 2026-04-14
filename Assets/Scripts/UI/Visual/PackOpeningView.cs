// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Pack Opening View
//  Premium animated booster pack opening sequence:
//  enter → shake → burst → fan → rare pause → summary.
// ═══════════════════════════════════════════════════════
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DualCraft.Core;

namespace DualCraft.UI.Visual
{
    public class PackOpeningView : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        //  INSPECTOR
        // ══════════════════════════════════════════════════════
        [Header("Pack Object")]
        [SerializeField] private RectTransform packRect;
        [SerializeField] private Image packImage;
        [SerializeField] private CanvasGroup packGroup;
        [SerializeField] private Image packGlow;

        [Header("Burst Effect")]
        [SerializeField] private Image burstFlash;
        [SerializeField] private ParticleSystem burstParticles;
        [SerializeField] private Image burstRing;
        [SerializeField] private Image burstRing2;
        [SerializeField] private ParticleSystem burstSparkles;

        [Header("Card Fan")]
        [SerializeField] private RectTransform cardFanParent;
        [SerializeField] private GameObject cardRevealPrefab;
        [SerializeField] private ParticleSystem cardTrailParticles;

        [Header("Rare Reveal")]
        [SerializeField] private CanvasGroup rareRevealOverlay;
        [SerializeField] private Image rareGlow;
        [SerializeField] private Image rareGlow2;
        [SerializeField] private TMP_Text rarityLabel;
        [SerializeField] private ParticleSystem rareParticles;

        [Header("Skip")]
        [SerializeField] private FancyButton skipButton;

        [Header("Summary")]
        [SerializeField] private CanvasGroup summaryGroup;
        [SerializeField] private RectTransform summaryCardGrid;
        [SerializeField] private TMP_Text summaryTitle;
        [SerializeField] private TMP_Text summarySubtitle;
        [SerializeField] private Button summaryCloseButton;

        [Header("Pressure Gauge (hold-to-open)")]
        [SerializeField] private Image pressureGaugeFill;
        [SerializeField] private CanvasGroup pressureGaugeGroup;

        [Header("Tuning")]
        [SerializeField] private float packEnterTime   = 0.5f;
        [SerializeField] private float shakeTime       = 0.8f;
        [SerializeField] private float burstTime       = 0.5f;
        [SerializeField] private float fanSpreadAngle  = 35f;
        [SerializeField] private float fanRadius       = 250f;
        [SerializeField] private float cardRevealDelay = 0.15f;
        [SerializeField] private float rarePauseTime   = 1.8f;
        [SerializeField] private float holdToOpenTime  = 1.0f;

        // ── State ────────────────────────────────────────────
        private bool _isAnimating;
        private DualCraftVisualTheme Theme => DualCraftVisualTheme.I;

        // ══════════════════════════════════════════════════════
        //  PUBLIC API
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// Begin the full pack-opening sequence. Pass card data as
        /// (name, rarity, element, sprite) tuples. Sprite can be null.
        /// </summary>
        public void OpenPack(List<PackCard> cards, System.Action onComplete = null)
        {
            if (_isAnimating) return;
            StartCoroutine(PackSequence(cards, onComplete));
        }

        // ══════════════════════════════════════════════════════
        //  SEQUENCE
        // ══════════════════════════════════════════════════════

        private IEnumerator PackSequence(List<PackCard> cards, System.Action onComplete)
        {
            _isAnimating = true;

            // Reset state
            HideAll();

            // ── 1. Pack enters screen ────────────────────────
            if (packRect != null && packGroup != null)
            {
                packGroup.alpha = 0f;
                packRect.localScale = Vector3.one * 0.3f;
                yield return StartCoroutine(PackEnter());
            }

            // ── 2. Hold to open (pressure gauge) ──────────────
            yield return HoldToOpen();

            // ── 3. Pack shakes ───────────────────────────────
            yield return StartCoroutine(PackShake());

            // ── 4. Burst of light ────────────────────────────
            yield return StartCoroutine(BurstEffect());

            // ── 5. Hide pack ─────────────────────────────────
            if (packGroup != null) packGroup.alpha = 0f;

            // ── 6. Cards fan out ─────────────────────────────
            yield return StartCoroutine(FanOutCards(cards));

            // ── 7. Rare card reveal pause ────────────────────
            int rareIndex = FindHighestRarity(cards);
            if (rareIndex >= 0 && cards[rareIndex].rarity >= Rarity.Epic)
                yield return StartCoroutine(RareRevealPause(cards[rareIndex]));

            // ── 8. Summary screen ────────────────────────────
            yield return StartCoroutine(ShowSummary(cards));

            _isAnimating = false;
            onComplete?.Invoke();
        }

        // ══════════════════════════════════════════════════════
        //  STEP IMPLEMENTATIONS
        // ══════════════════════════════════════════════════════

        private IEnumerator PackEnter()
        {
            float t = 0f;
            while (t < packEnterTime)
            {
                t += Time.deltaTime;
                float p = UIAnimUtils.EaseOutBack(Mathf.Clamp01(t / packEnterTime));
                packRect.localScale = Vector3.one * p;
                packGroup.alpha = Mathf.Clamp01(t / (packEnterTime * 0.5f));
                yield return null;
            }
            packRect.localScale = Vector3.one;
            packGroup.alpha = 1f;
        }

        private IEnumerator HoldToOpen()
        {
            // Show pressure gauge
            if (pressureGaugeGroup != null) { pressureGaugeGroup.alpha = 1f; pressureGaugeGroup.gameObject.SetActive(true); }
            if (pressureGaugeFill != null) pressureGaugeFill.fillAmount = 0f;

            // Wait for hold
            float holdProgress = 0f;
            bool holding = false;
            while (holdProgress < holdToOpenTime)
            {
                bool pressed = Input.GetMouseButton(0) || Input.touchCount > 0;
                if (pressed)
                {
                    holding = true;
                    holdProgress += Time.deltaTime;
                    // Pack glow intensifies with hold
                    if (packGlow != null)
                    {
                        float p = holdProgress / holdToOpenTime;
                        Color gc = Theme != null ? Theme.gold : new Color(0.78f, 0.66f, 0.42f);
                        gc.a = p * 0.5f;
                        packGlow.color = gc;
                    }
                }
                else if (holding)
                {
                    // Released early — decay slowly
                    holdProgress = Mathf.Max(0f, holdProgress - Time.deltaTime * 2f);
                }
                if (pressureGaugeFill != null)
                    pressureGaugeFill.fillAmount = holdProgress / holdToOpenTime;
                yield return null;
            }

            // Hide gauge
            if (pressureGaugeGroup != null)
                StartCoroutine(UIAnimUtils.FadeOut(pressureGaugeGroup, 0.15f));
        }

        private IEnumerator PackShake()
        {
            if (packRect == null) yield break;
            Vector2 origin = packRect.anchoredPosition;
            float t = 0f;
            float intensity = 4f;
            while (t < shakeTime)
            {
                t += Time.deltaTime;
                // Intensity ramps up
                float ramp = t / shakeTime;
                float mag = intensity * (1f + ramp * 3f);
                float x = Mathf.Sin(t * 45f) * mag;
                float y = Mathf.Cos(t * 38f) * mag * 0.5f;
                packRect.anchoredPosition = origin + new Vector2(x, y);
                yield return null;
            }
            packRect.anchoredPosition = origin;
        }

        private IEnumerator BurstEffect()
        {
            // Flash
            if (burstFlash != null)
            {
                burstFlash.gameObject.SetActive(true);
                burstFlash.color = new Color(1f, 0.95f, 0.75f, 0.95f);
            }
            // Primary ring
            if (burstRing != null)
            {
                burstRing.gameObject.SetActive(true);
                burstRing.rectTransform.localScale = Vector3.zero;
            }
            // Secondary ring (delayed, different speed)
            if (burstRing2 != null)
            {
                burstRing2.gameObject.SetActive(true);
                burstRing2.rectTransform.localScale = Vector3.zero;
            }
            // Particles
            if (burstParticles != null) burstParticles.Play();
            if (burstSparkles != null) burstSparkles.Emit(30);

            float t = 0f;
            while (t < burstTime)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / burstTime);

                if (burstFlash != null)
                {
                    Color c = burstFlash.color;
                    c.a = 0.95f * (1f - UIAnimUtils.EaseOutQuad(p));
                    burstFlash.color = c;
                }
                if (burstRing != null)
                {
                    float s = UIAnimUtils.EaseOutQuad(p) * 5f;
                    burstRing.rectTransform.localScale = Vector3.one * s;
                    Color c = burstRing.color;
                    c.a = 1f - p;
                    burstRing.color = c;
                }
                if (burstRing2 != null)
                {
                    float p2 = Mathf.Clamp01((t - 0.08f) / burstTime);
                    float s2 = UIAnimUtils.EaseOutQuad(Mathf.Max(0, p2)) * 3.5f;
                    burstRing2.rectTransform.localScale = Vector3.one * s2;
                    Color c2 = burstRing2.color;
                    c2.a = (1f - p2) * 0.6f;
                    burstRing2.color = c2;
                }
                yield return null;
            }

            if (burstFlash != null) burstFlash.gameObject.SetActive(false);
            if (burstRing != null) burstRing.gameObject.SetActive(false);
            if (burstRing2 != null) burstRing2.gameObject.SetActive(false);
        }

        private IEnumerator FanOutCards(List<PackCard> cards)
        {
            if (cardFanParent == null) yield break;

            // Clear previous cards
            foreach (Transform child in cardFanParent) Destroy(child.gameObject);

            int count = cards.Count;
            float totalAngle = fanSpreadAngle * 2f;
            float step = count > 1 ? totalAngle / (count - 1) : 0f;
            float startAngle = -fanSpreadAngle;

            for (int i = 0; i < count; i++)
            {
                float angle = startAngle + step * i;
                float rad = angle * Mathf.Deg2Rad;
                Vector2 pos = new Vector2(Mathf.Sin(rad) * fanRadius, Mathf.Cos(rad) * fanRadius * 0.3f);

                GameObject cardGo;
                if (cardRevealPrefab != null)
                    cardGo = Instantiate(cardRevealPrefab, cardFanParent);
                else
                    cardGo = CreatePlaceholderCard(cards[i]);

                RectTransform crt = cardGo.GetComponent<RectTransform>();
                if (crt != null)
                {
                    crt.anchoredPosition = Vector2.zero;
                    crt.localRotation = Quaternion.Euler(0, 0, -angle * 0.5f);
                }

                // Set card visuals
                SetCardVisuals(cardGo, cards[i]);

                // Animate to position
                StartCoroutine(AnimateCardReveal(crt, pos, angle));
                yield return new WaitForSeconds(cardRevealDelay);
            }
        }

        private IEnumerator AnimateCardReveal(RectTransform crt, Vector2 targetPos, float angle)
        {
            if (crt == null) yield break;
            crt.localScale = Vector3.zero;
            float dur = 0.35f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = UIAnimUtils.EaseOutBack(Mathf.Clamp01(t / dur));
                crt.anchoredPosition = targetPos * p;
                crt.localScale = Vector3.one * p;
                crt.localRotation = Quaternion.Euler(0, 0, -angle * 0.5f * p);
                yield return null;
            }
            // Emit trail particle at card position
            if (cardTrailParticles != null) cardTrailParticles.Emit(3);
        }

        private IEnumerator RareRevealPause(PackCard card)
        {
            if (rareRevealOverlay == null) yield break;

            rareRevealOverlay.alpha = 0f;
            rareRevealOverlay.gameObject.SetActive(true);

            Color glowCol = Theme != null ? Theme.GetRarityColor(card.rarity) : Color.yellow;
            bool isLegendary = card.rarity == Rarity.Legendary;

            if (rareGlow != null) rareGlow.color = glowCol;
            if (rareGlow2 != null)
            {
                Color g2 = glowCol;
                g2.a = 0.3f;
                rareGlow2.color = g2;
            }
            if (rarityLabel != null)
            {
                rarityLabel.text = card.rarity.ToString().ToUpper();
                rarityLabel.color = glowCol;
            }

            // Particle burst for rare reveal
            if (rareParticles != null)
            {
                var main = rareParticles.main;
                main.startColor = glowCol;
                rareParticles.Emit(isLegendary ? 50 : 25);
            }

            yield return UIAnimUtils.FadeIn(rareRevealOverlay, 0.35f);

            // Legendary gets rainbow glow cycle
            Coroutine rainbowCo = null;
            if (isLegendary && rareGlow != null)
                rainbowCo = StartCoroutine(UIAnimUtils.RainbowCycle(rareGlow, 0.5f, 0.6f));

            // Pulse the rarity label
            if (rarityLabel != null)
                StartCoroutine(UIAnimUtils.ElasticScale(rarityLabel.transform, 0.4f));

            yield return new WaitForSeconds(rarePauseTime);

            if (rainbowCo != null) StopCoroutine(rainbowCo);

            yield return UIAnimUtils.FadeOut(rareRevealOverlay, 0.3f);
            rareRevealOverlay.gameObject.SetActive(false);
        }

        private IEnumerator ShowSummary(List<PackCard> cards)
        {
            if (summaryGroup == null) yield break;

            // Clear card fan
            if (cardFanParent != null)
                yield return UIAnimUtils.FadeOut(cardFanParent.GetComponent<CanvasGroup>() ?? AddCanvasGroup(cardFanParent), 0.3f);

            summaryGroup.alpha = 0f;
            summaryGroup.gameObject.SetActive(true);
            if (summaryTitle != null) summaryTitle.text = $"{cards.Count} CARDS OBTAINED";

            // Count rarities for subtitle
            int rareCount = 0, epicCount = 0, legendaryCount = 0;
            foreach (var c in cards)
            {
                if (c.rarity == Rarity.Rare) rareCount++;
                else if (c.rarity == Rarity.Epic) epicCount++;
                else if (c.rarity == Rarity.Legendary) legendaryCount++;
            }
            if (summarySubtitle != null)
            {
                var parts = new List<string>();
                if (legendaryCount > 0) parts.Add($"{legendaryCount} Legendary");
                if (epicCount > 0) parts.Add($"{epicCount} Epic");
                if (rareCount > 0) parts.Add($"{rareCount} Rare");
                summarySubtitle.text = parts.Count > 0 ? string.Join(" · ", parts) : "";
                summarySubtitle.color = Theme != null ? Theme.textGold : new Color(0.9f, 0.78f, 0.52f);
            }

            // Populate summary grid
            if (summaryCardGrid != null)
            {
                foreach (Transform child in summaryCardGrid) Destroy(child.gameObject);
                foreach (var card in cards)
                {
                    var go = CreatePlaceholderCard(card);
                    go.transform.SetParent(summaryCardGrid, false);
                }
            }

            yield return UIAnimUtils.FadeIn(summaryGroup, 0.4f);

            // Stagger pop each summary card
            if (summaryCardGrid != null)
            {
                for (int i = 0; i < summaryCardGrid.childCount; i++)
                {
                    StartCoroutine(UIAnimUtils.PopScale(summaryCardGrid.GetChild(i), 0.2f));
                    yield return new WaitForSeconds(0.06f);
                }
            }
        }

        // ══════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════

        private void HideAll()
        {
            if (burstFlash != null) burstFlash.gameObject.SetActive(false);
            if (burstRing != null) burstRing.gameObject.SetActive(false);
            if (burstRing2 != null) burstRing2.gameObject.SetActive(false);
            if (burstSparkles != null) burstSparkles.gameObject.SetActive(false);
            if (rareRevealOverlay != null) rareRevealOverlay.gameObject.SetActive(false);
            if (rareGlow2 != null) rareGlow2.gameObject.SetActive(false);
            if (pressureGaugeGroup != null) pressureGaugeGroup.gameObject.SetActive(false);
            if (summaryGroup != null) { summaryGroup.alpha = 0f; summaryGroup.gameObject.SetActive(false); }
            if (cardFanParent != null) foreach (Transform c in cardFanParent) Destroy(c.gameObject);
        }

        private int FindHighestRarity(List<PackCard> cards)
        {
            int best = -1;
            Rarity highest = Rarity.Common;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].rarity >= highest) { highest = cards[i].rarity; best = i; }
            }
            return best;
        }

        private void SetCardVisuals(GameObject go, PackCard card)
        {
            var nameLabel = go.GetComponentInChildren<TMP_Text>();
            if (nameLabel != null) nameLabel.text = card.name;
            var img = go.transform.Find("Art")?.GetComponent<Image>();
            if (img != null && card.art != null) img.sprite = card.art;

            // Rarity frame glow
            var frame = go.transform.Find("Frame")?.GetComponent<Image>();
            if (frame != null && Theme != null)
                frame.color = Theme.GetRarityColor(card.rarity);
        }

        private GameObject CreatePlaceholderCard(PackCard card)
        {
            var go = new GameObject(card.name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(100f, 140f);
            var img = go.GetComponent<Image>();
            Color bg = Theme != null ? Theme.GetElementColor(card.element) : Color.grey;
            bg.a = 0.85f;
            img.color = bg;

            // Rarity border frame
            var frameGo = new GameObject("Frame", typeof(RectTransform), typeof(Image));
            frameGo.transform.SetParent(go.transform, false);
            var frameRt = frameGo.GetComponent<RectTransform>();
            frameRt.anchorMin = Vector2.zero;
            frameRt.anchorMax = Vector2.one;
            frameRt.offsetMin = new Vector2(-3f, -3f);
            frameRt.offsetMax = new Vector2(3f, 3f);
            frameGo.transform.SetAsFirstSibling();
            var frameImg = frameGo.GetComponent<Image>();
            Color frameCol = Theme != null ? Theme.GetRarityColor(card.rarity) : Color.white;
            frameCol.a = card.rarity >= Rarity.Rare ? 0.8f : 0.3f;
            frameImg.color = frameCol;

            // Name label
            var labelGo = new GameObject("Name", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0, 0);
            labelRt.anchorMax = new Vector2(1, 0.3f);
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text = card.name;
            tmp.fontSize = 12f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            // Rarity dot
            var dotGo = new GameObject("RarityDot", typeof(RectTransform), typeof(Image));
            dotGo.transform.SetParent(go.transform, false);
            var dotRt = dotGo.GetComponent<RectTransform>();
            dotRt.anchorMin = new Vector2(0.5f, 0.85f);
            dotRt.anchorMax = new Vector2(0.5f, 0.85f);
            dotRt.sizeDelta = new Vector2(12f, 12f);
            dotGo.GetComponent<Image>().color = Theme != null ? Theme.GetRarityColor(card.rarity) : Color.white;

            // "NEW" badge (top-right corner)
            if (card.isNew)
            {
                var newGo = new GameObject("NewBadge", typeof(RectTransform));
                newGo.transform.SetParent(go.transform, false);
                var newRt = newGo.GetComponent<RectTransform>();
                newRt.anchorMin = new Vector2(0.65f, 0.88f);
                newRt.anchorMax = new Vector2(1f, 1f);
                newRt.offsetMin = Vector2.zero;
                newRt.offsetMax = Vector2.zero;
                var newTmp = newGo.AddComponent<TextMeshProUGUI>();
                newTmp.text = "NEW";
                newTmp.fontSize = 9f;
                newTmp.alignment = TextAlignmentOptions.Center;
                newTmp.color = Theme != null ? Theme.gold : new Color(0.95f, 0.75f, 0.15f);
                newTmp.fontStyle = FontStyles.Bold;
            }

            go.transform.SetParent(cardFanParent, false);
            return go;
        }

        private static CanvasGroup AddCanvasGroup(RectTransform rt)
        {
            var cg = rt.gameObject.AddComponent<CanvasGroup>();
            return cg;
        }

        // ════════════════════════════════════════════════
        //  SKIP SUPPORT
        // ════════════════════════════════════════════════

        private bool _skipRequested;

        /// <summary>Skip to the summary screen.</summary>
        public void RequestSkip()
        {
            _skipRequested = true;
            if (skipButton != null) skipButton.SetInteractable(false);
        }

        // ════════════════════════════════════════════════
        //  ELEMENT-COLORED BURST
        // ════════════════════════════════════════════════

        /// <summary>Override burst colors based on pack element affinity.</summary>
        public void SetPackElement(Element element)
        {
            Color elColor = Theme != null ? Theme.GetElementColor(element) : Color.white;
            if (burstRing != null) burstRing.color = DualCraftVisualTheme.WithAlpha(elColor, 0.8f);
            if (burstRing2 != null) burstRing2.color = DualCraftVisualTheme.WithAlpha(elColor, 0.5f);
            if (packGlow != null) packGlow.color = DualCraftVisualTheme.WithAlpha(elColor, 0.3f);
        }

        // ════════════════════════════════════════════════
        //  DUPLICATE INDICATOR
        // ════════════════════════════════════════════════

        /// <summary>Mark a card GameObject as a duplicate.</summary>
        public void MarkDuplicate(GameObject cardGo)
        {
            var dupeGo = new GameObject("DupeBadge", typeof(RectTransform));
            dupeGo.transform.SetParent(cardGo.transform, false);
            var rt = dupeGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.88f);
            rt.anchorMax = new Vector2(0.35f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var tmp = dupeGo.AddComponent<TextMeshProUGUI>();
            tmp.text = "DUPE";
            tmp.fontSize = 8f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Theme != null ? Theme.textMuted : new Color(0.5f, 0.5f, 0.55f);
            tmp.fontStyle = FontStyles.Italic;
        }
    }

    // ══════════════════════════════════════════════════════════
    //  PACK CARD DATA  — lightweight struct for pack results
    // ══════════════════════════════════════════════════════════
    [System.Serializable]
    public struct PackCard
    {
        public string name;
        public Rarity rarity;
        public Element element;
        public Sprite art;
        public bool isNew;

        public PackCard(string name, Rarity rarity, Element element, Sprite art = null, bool isNew = false)
        {
            this.name = name;
            this.rarity = rarity;
            this.element = element;
            this.art = art;
            this.isNew = isNew;
        }
    }
}
