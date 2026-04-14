// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Main Menu View
//  Premium animated main menu with parallax background,
//  animated buttons, profile corner, featured panels.
//  Replaces prototype boxes with commercial-grade UI.
// ═══════════════════════════════════════════════════════
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DualCraft.UI.Visual
{
    public class MainMenuView : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════
        //  LAYOUT REFERENCES
        // ══════════════════════════════════════════════════════
        [Header("Root")]
        [SerializeField] private CanvasGroup rootGroup;
        [SerializeField] private RectTransform rootRect;

        [Header("Background")]
        [SerializeField] private Image backgroundImage;
        [SerializeField] private RectTransform parallaxLayer1;
        [SerializeField] private RectTransform parallaxLayer2;
        [SerializeField] private RectTransform parallaxLayer3;
        [SerializeField] private ParticleSystem magicParticles;
        [SerializeField] private ParticleSystem logoSparkles;
        [SerializeField] private Image gradientOverlay;
        [SerializeField] private Image bottomVignette;

        [Header("Title / Logo")]
        [SerializeField] private RectTransform titleGroup;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text taglineText;
        [SerializeField] private Image logoImage;
        [SerializeField] private Image logoGlow;
        [SerializeField] private RectTransform logoShineRect;

        [Header("Profile Corner")]
        [SerializeField] private RectTransform profileCorner;
        [SerializeField] private TMP_Text playerNameText;
        [SerializeField] private TMP_Text playerLevelText;
        [SerializeField] private Image profileAvatar;
        [SerializeField] private Image profileBorder;

        [Header("Primary Buttons")]
        [SerializeField] private FancyButton btnBattle;
        [SerializeField] private FancyButton btnDecks;
        [SerializeField] private FancyButton btnCollection;
        [SerializeField] private FancyButton btnStory;
        [SerializeField] private FancyButton btnShop;
        [SerializeField] private FancyButton btnSettings;

        [Header("Featured Panels")]
        [SerializeField] private RectTransform featuredPackPanel;
        [SerializeField] private TMP_Text featuredPackTitle;
        [SerializeField] private Image featuredPackArt;
        [SerializeField] private CanvasGroup featuredPackGroup;
        [SerializeField] private Image featuredPackGlow;

        [SerializeField] private RectTransform featuredDeckPanel;
        [SerializeField] private TMP_Text featuredDeckTitle;
        [SerializeField] private Image featuredDeckArt;
        [SerializeField] private CanvasGroup featuredDeckGroup;

        [Header("Info Bar")]
        [SerializeField] private TMP_Text versionText;
        [SerializeField] private TMP_Text newsTickerText;
        [SerializeField] private RectTransform newsTickerRect;
        [SerializeField] private Image dailyRewardIndicator;

        [Header("Animation Settings")]
        [SerializeField] private float parallaxSpeed1   = 3f;
        [SerializeField] private float parallaxSpeed2   = 1.5f;
        [SerializeField] private float parallaxSpeed3   = 0.8f;
        [SerializeField] private float buttonStagger    = 0.08f;
        [SerializeField] private float introDelay       = 0.3f;
        [SerializeField] private float logoShineInterval = 5f;

        // ── Internals ────────────────────────────────────────
        private readonly List<FancyButton> _buttons = new();
        private Coroutine _logoShineCo;
        private Coroutine _logoGlowCo;
        private Coroutine _newsTickerCo;
        private DualCraftVisualTheme Theme => DualCraftVisualTheme.I;

        // ══════════════════════════════════════════════════════
        //  LIFECYCLE
        // ══════════════════════════════════════════════════════

        private void Awake()
        {
            // Collect buttons for staggered intro
            FancyButton[] btns = { btnBattle, btnDecks, btnCollection, btnStory, btnShop, btnSettings };
            foreach (var b in btns)
                if (b != null) _buttons.Add(b);

            if (rootGroup != null) rootGroup.alpha = 0f;
        }

        private void Start()
        {
            ApplyThemeColors();
            StartCoroutine(IntroSequence());

            // Start logo shine loop
            if (logoShineRect != null)
                _logoShineCo = StartCoroutine(LogoShineLoop());

            // Start logo glow breathing
            if (logoGlow != null)
            {
                Color gc = Theme != null ? Theme.gold : new Color(0.78f, 0.66f, 0.42f);
                _logoGlowCo = StartCoroutine(UIAnimUtils.BreathingGlow(logoGlow, gc, 0.8f, 0.05f, 0.20f));
            }

            // Set version text
            if (versionText != null) versionText.text = $"v{Application.version}";

            // Start news ticker if present
            if (newsTickerText != null && newsTickerRect != null)
                _newsTickerCo = StartCoroutine(NewsTickerScroll());
        }

        private void OnDisable()
        {
            if (_logoShineCo != null) StopCoroutine(_logoShineCo);
            if (_logoGlowCo != null) StopCoroutine(_logoGlowCo);
            if (_newsTickerCo != null) StopCoroutine(_newsTickerCo);
        }

        private void Update()
        {
            AnimateParallax();
        }

        // ══════════════════════════════════════════════════════
        //  INTRO ANIMATION
        // ══════════════════════════════════════════════════════

        private IEnumerator IntroSequence()
        {
            yield return new WaitForSecondsRealtime(introDelay);

            // Fade in root
            if (rootGroup != null)
                yield return UIAnimUtils.FadeIn(rootGroup, 0.4f);

            // Title — elastic bounce for premium feel
            if (titleGroup != null)
                yield return UIAnimUtils.ElasticScale(titleGroup, 0.5f);

            // Trigger logo sparkles
            if (logoSparkles != null) logoSparkles.Emit(15);

            // Tagline typewriter reveal
            if (taglineText != null && !string.IsNullOrEmpty(taglineText.text))
            {
                string fullText = taglineText.text;
                StartCoroutine(UIAnimUtils.TypewriterReveal(taglineText, fullText, 0.04f));
            }

            // Staggered button slide-in (alternating sides for visual interest)
            for (int i = 0; i < _buttons.Count; i++)
            {
                var rt = _buttons[i].GetComponent<RectTransform>();
                if (rt != null)
                {
                    float xOff = i % 2 == 0 ? -200f : 200f;
                    StartCoroutine(UIAnimUtils.SlideIn(rt, new Vector2(xOff, 0f), 0.35f));
                }
                yield return new WaitForSecondsRealtime(buttonStagger);
            }

            // Profile corner
            if (profileCorner != null)
                StartCoroutine(UIAnimUtils.SlideIn(profileCorner, new Vector2(150f, 0f), 0.3f));

            // Featured panels with pop scale
            yield return new WaitForSecondsRealtime(0.15f);
            if (featuredPackGroup != null)
            {
                StartCoroutine(UIAnimUtils.FadeIn(featuredPackGroup, 0.4f));
                if (featuredPackPanel != null) StartCoroutine(UIAnimUtils.PopScale(featuredPackPanel, 0.3f));
            }
            if (featuredDeckGroup != null)
            {
                StartCoroutine(UIAnimUtils.FadeIn(featuredDeckGroup, 0.4f));
                if (featuredDeckPanel != null) StartCoroutine(UIAnimUtils.PopScale(featuredDeckPanel, 0.3f));
            }

            // Daily reward pulse
            if (dailyRewardIndicator != null)
            {
                Color rewardCol = Theme != null ? Theme.gold : Color.yellow;
                StartCoroutine(UIAnimUtils.PulseGlow(dailyRewardIndicator, rewardCol, 1f, 0.3f, 0.8f));
            }
        }

        // ══════════════════════════════════════════════════════
        //  PARALLAX BACKGROUND
        // ══════════════════════════════════════════════════════

        private void AnimateParallax()
        {
            float t = Time.time;
            if (parallaxLayer1 != null)
            {
                float x = Mathf.Sin(t * 0.1f) * parallaxSpeed1;
                float y = Mathf.Cos(t * 0.08f) * parallaxSpeed1 * 0.6f;
                parallaxLayer1.anchoredPosition = new Vector2(x, y);
                parallaxLayer1.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.03f) * 0.5f);
            }
            if (parallaxLayer2 != null)
            {
                float x = Mathf.Cos(t * 0.07f) * parallaxSpeed2;
                float y = Mathf.Sin(t * 0.06f) * parallaxSpeed2 * 0.5f;
                parallaxLayer2.anchoredPosition = new Vector2(x, y);
                parallaxLayer2.localRotation = Quaternion.Euler(0f, 0f, Mathf.Cos(t * 0.04f) * 0.3f);
            }
            if (parallaxLayer3 != null)
            {
                float x = Mathf.Sin(t * 0.04f) * parallaxSpeed3;
                float y = Mathf.Cos(t * 0.035f) * parallaxSpeed3 * 0.4f;
                parallaxLayer3.anchoredPosition = new Vector2(x, y);
            }
        }

        // ══════════════════════════════════════════════════════
        //  THEME APPLICATION
        // ══════════════════════════════════════════════════════

        private void ApplyThemeColors()
        {
            if (Theme == null) return;

            if (backgroundImage != null) backgroundImage.color = Theme.bgDark;
            if (gradientOverlay != null)
            {
                Color g = Theme.accent;
                g.a = 0.12f;
                gradientOverlay.color = g;
            }
            if (bottomVignette != null)
            {
                Color v = Theme.bgDark;
                v.a = 0.65f;
                bottomVignette.color = v;
            }
            if (titleText != null) titleText.color = Theme.gold;
            if (taglineText != null) taglineText.color = Theme.textSecondary;
            if (profileBorder != null) profileBorder.color = Theme.panelBorderGold;
            if (featuredPackGlow != null)
            {
                Color g = Theme.gold;
                g.a = 0.15f;
                featuredPackGlow.color = g;
            }
        }

        // ══════════════════════════════════════════════════════
        //  LOGO SHINE LOOP
        // ══════════════════════════════════════════════════════

        private IEnumerator LogoShineLoop()
        {
            if (logoShineRect == null) yield break;
            float width = logoShineRect.rect.width > 0 ? logoShineRect.rect.width : 400f;
            while (true)
            {
                yield return new WaitForSecondsRealtime(logoShineInterval);
                yield return UIAnimUtils.ShineSweep(logoShineRect, width, 0.7f);
                // Emit sparkles on shine
                if (logoSparkles != null) logoSparkles.Emit(8);
            }
        }

        // ══════════════════════════════════════════════════════
        //  NEWS TICKER
        // ══════════════════════════════════════════════════════

        private IEnumerator NewsTickerScroll()
        {
            if (newsTickerRect == null || newsTickerText == null) yield break;
            float textWidth = newsTickerText.preferredWidth;
            float parentWidth = newsTickerRect.parent is RectTransform parent ? parent.rect.width : 800f;
            float startX = parentWidth;
            float endX = -textWidth - 50f;
            float speed = 60f; // pixels per second

            while (true)
            {
                newsTickerRect.anchoredPosition = new Vector2(startX, newsTickerRect.anchoredPosition.y);
                float x = startX;
                while (x > endX)
                {
                    x -= speed * Time.deltaTime;
                    newsTickerRect.anchoredPosition = new Vector2(x, newsTickerRect.anchoredPosition.y);
                    yield return null;
                }
                yield return new WaitForSeconds(1f);
            }
        }

        // ══════════════════════════════════════════════════════
        //  PUBLIC API  (call from menu controller)
        // ══════════════════════════════════════════════════════

        public void SetPlayerInfo(string name, int level, Sprite avatar = null)
        {
            if (playerNameText != null) playerNameText.text = name;
            if (playerLevelText != null) playerLevelText.text = $"Lv. {level}";
            if (profileAvatar != null && avatar != null) profileAvatar.sprite = avatar;
        }

        public void SetFeaturedPack(string title, Sprite art = null)
        {
            if (featuredPackTitle != null) featuredPackTitle.text = title;
            if (featuredPackArt != null && art != null) featuredPackArt.sprite = art;
        }

        public void SetFeaturedDeck(string title, Sprite art = null)
        {
            if (featuredDeckTitle != null) featuredDeckTitle.text = title;
            if (featuredDeckArt != null && art != null) featuredDeckArt.sprite = art;
        }

        /// <summary>Fade out the menu before transitioning.</summary>
        public void FadeOutMenu(System.Action onComplete = null)
        {
            if (rootGroup != null)
                StartCoroutine(UIAnimUtils.FadeOut(rootGroup, 0.3f, onComplete));
        }

        /// <summary>Set the scrolling news ticker text.</summary>
        public void SetNewsTicker(string text)
        {
            if (newsTickerText != null) newsTickerText.text = text;
        }

        /// <summary>Show/hide daily reward indicator.</summary>
        public void SetDailyRewardAvailable(bool available)
        {
            if (dailyRewardIndicator != null)
                dailyRewardIndicator.gameObject.SetActive(available);
        }
    }
}
