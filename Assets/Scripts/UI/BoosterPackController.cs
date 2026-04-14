// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Booster Pack Opening Controller
//  PTCG Pocket–inspired pack opening ceremony.
//  Shows pack, opens with animation, reveals cards.
// ═══════════════════════════════════════════════════════

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace DualCraft.UI
{
    using Cards;
    using Core;

    public class BoosterPackController : MonoBehaviour
    {
        [Header("Database")]
        [SerializeField] private CardDatabase cardDatabase;

        [Header("Pack Select Phase")]
        [SerializeField] private GameObject packSelectPanel;
        [SerializeField] private Transform packCarousel;
        [SerializeField] private Button openButton;
        [SerializeField] private TextMeshProUGUI packNameText;
        [SerializeField] private TextMeshProUGUI packCountText;
        [SerializeField] private TextMeshProUGUI timerText;
        [SerializeField] private Button backButton;

        [Header("Pack Ceremony Phase")]
        [SerializeField] private GameObject ceremonyPanel;
        [SerializeField] private Image packImage;
        [SerializeField] private Image packGlow;

        [Header("Results Phase")]
        [SerializeField] private GameObject resultsPanel;
        [SerializeField] private TextMeshProUGUI resultsTitle;
        [SerializeField] private Transform resultsGrid;
        [SerializeField] private GameObject cardPrefab;
        [SerializeField] private Button nextButton;

        private int _selectedPackIndex;
        private readonly List<CardData> _pulledCards = new();

        // Pack definitions (element-themed booster packs)
        private static readonly PackDefinition[] Packs = new[]
        {
            new PackDefinition("Inferno Blaze",   Element.Flame,  new Color(0.9f, 0.3f, 0.1f)),
            new PackDefinition("Frost Tide",      Element.Ice,    new Color(0.3f, 0.7f, 0.95f)),
            new PackDefinition("Verdant Growth",  Element.Nature, new Color(0.2f, 0.8f, 0.3f)),
            new PackDefinition("Shadow Veil",     Element.Dark,   new Color(0.4f, 0.2f, 0.6f)),
            new PackDefinition("Radiant Dawn",    Element.Light,  new Color(0.95f, 0.85f, 0.4f)),
            new PackDefinition("Gale Force",      Element.Air,    new Color(0.6f, 0.85f, 0.9f)),
            new PackDefinition("Tidal Surge",     Element.Water,  new Color(0.15f, 0.45f, 0.9f)),
            new PackDefinition("Terra Forge",     Element.Earth,  new Color(0.6f, 0.45f, 0.25f)),
        };

        private void Start()
        {
            cardDatabase.Initialize();

            openButton?.onClick.AddListener(OnOpenPack);
            nextButton?.onClick.AddListener(OnNextFromResults);
            backButton?.onClick.AddListener(() => SceneManager.LoadScene("MainMenu"));

            ShowPackSelect();
        }

        private void Update()
        {
            // Live countdown timer
            if (timerText != null && packSelectPanel != null && packSelectPanel.activeSelf)
            {
                if (PackTimerManager.CanOpenPack())
                {
                    timerText.text = "READY TO OPEN!";
                    timerText.color = new Color(0.3f, 0.9f, 0.3f);
                    if (openButton) openButton.interactable = true;
                }
                else
                {
                    timerText.text = $"Next pull in: {PackTimerManager.GetTimeRemaining()}";
                    timerText.color = new Color(0.7f, 0.5f, 0.2f);
                    if (openButton) openButton.interactable = false;
                }
            }
        }

        private void ShowPackSelect()
        {
            if (packSelectPanel) packSelectPanel.SetActive(true);
            if (ceremonyPanel) ceremonyPanel.SetActive(false);
            if (resultsPanel) resultsPanel.SetActive(false);

            RefreshPackInfo();
        }

        private void RefreshPackInfo()
        {
            var pack = Packs[_selectedPackIndex];
            if (packNameText) packNameText.text = pack.Name;
            if (packCountText) packCountText.text = $"5 Cards · {pack.Element} Theme";
        }

        /// <summary>Select next pack in carousel.</summary>
        public void SelectNextPack()
        {
            _selectedPackIndex = (_selectedPackIndex + 1) % Packs.Length;
            RefreshPackInfo();
        }

        /// <summary>Select previous pack in carousel.</summary>
        public void SelectPrevPack()
        {
            _selectedPackIndex = (_selectedPackIndex - 1 + Packs.Length) % Packs.Length;
            RefreshPackInfo();
        }

        private void OnOpenPack()
        {
            if (!PackTimerManager.CanOpenPack()) return;

            var pack = Packs[_selectedPackIndex];
            _pulledCards.Clear();

            // Pull 5 cards using weighted rarity
            var pool = GetCardPool(pack.Element);
            for (int i = 0; i < 5; i++)
            {
                var card = PullCard(pool);
                if (card != null) _pulledCards.Add(card);
            }

            PackTimerManager.RecordPull();
            StartCoroutine(PackCeremony());
        }

        private IEnumerator PackCeremony()
        {
            // Phase 1: Show pack with glow effect
            if (packSelectPanel) packSelectPanel.SetActive(false);
            if (ceremonyPanel) ceremonyPanel.SetActive(true);
            if (resultsPanel) resultsPanel.SetActive(false);

            // Pack appears with scale animation
            if (packImage)
            {
                var pack = Packs[_selectedPackIndex];
                packImage.color = pack.Accent;
                packImage.transform.localScale = Vector3.zero;
            }

            // Grow in
            float t = 0f;
            while (t < 0.6f)
            {
                t += Time.deltaTime;
                float scale = Mathf.SmoothStep(0f, 1f, t / 0.6f);
                if (packImage) packImage.transform.localScale = Vector3.one * scale;
                yield return null;
            }

            // Glow pulse
            if (packGlow) packGlow.gameObject.SetActive(true);
            t = 0f;
            while (t < 1.2f)
            {
                t += Time.deltaTime;
                float pulse = 0.15f + Mathf.Sin(t * 4f) * 0.1f;
                if (packGlow) packGlow.color = new Color(1f, 1f, 1f, pulse);
                yield return null;
            }

            // Flash and transition to results
            if (packGlow)
            {
                packGlow.color = new Color(1f, 1f, 1f, 0.8f);
                yield return new WaitForSeconds(0.15f);
            }

            ShowResults();
        }

        private void ShowResults()
        {
            if (ceremonyPanel) ceremonyPanel.SetActive(false);
            if (resultsPanel) resultsPanel.SetActive(true);

            if (resultsTitle) resultsTitle.text = "Opening Results";

            // Clear old cards
            if (resultsGrid)
            {
                for (int i = resultsGrid.childCount - 1; i >= 0; i--)
                    Destroy(resultsGrid.GetChild(i).gameObject);

                // Spawn pulled cards
                foreach (var card in _pulledCards)
                {
                    if (cardPrefab == null) continue;
                    var go = Instantiate(cardPrefab, resultsGrid);
                    var visual = go.GetComponent<CardVisual>();
                    if (visual != null)
                        visual.SetCard(card);

                    // Add "NEW" badge (top-left)
                    var badge = new GameObject("NewBadge");
                    badge.transform.SetParent(go.transform, false);
                    var brt = badge.AddComponent<RectTransform>();
                    brt.anchoredPosition = new Vector2(-70, 120);
                    brt.sizeDelta = new Vector2(50, 24);
                    var badgeImg = badge.AddComponent<Image>();
                    badgeImg.color = new Color(0.95f, 0.25f, 0.45f);
                    var badgeTextGO = new GameObject("BadgeText");
                    badgeTextGO.transform.SetParent(badge.transform, false);
                    var btrt = badgeTextGO.AddComponent<RectTransform>();
                    btrt.anchoredPosition = Vector2.zero;
                    btrt.sizeDelta = new Vector2(50, 24);
                    var badgeText = badgeTextGO.AddComponent<TextMeshProUGUI>();
                    badgeText.text = "NEW";
                    badgeText.fontSize = 11;
                    badgeText.fontStyle = FontStyles.Bold;
                    badgeText.alignment = TextAlignmentOptions.Center;
                    badgeText.color = Color.white;
                }
            }
        }

        private void OnNextFromResults()
        {
            ShowPackSelect();
        }

        // ─── Card Pull Logic ──────────────────────────────

        private List<CardData> GetCardPool(Element element)
        {
            var all = cardDatabase.GetAllCards();
            // Prefer cards matching the pack element, but include all
            var pool = new List<CardData>();

            foreach (var card in all)
            {
                bool matchesElement = card switch
                {
                    DaemonCardData d => d.element == element,
                    PillarCardData p => p.element == element,
                    ConjurorCardData c => c.element == element,
                    _ => false,
                };

                // Element-matching cards get 3x weight
                pool.Add(card);
                if (matchesElement)
                {
                    pool.Add(card);
                    pool.Add(card);
                }
            }

            return pool;
        }

        private CardData PullCard(List<CardData> pool)
        {
            if (pool.Count == 0) return null;

            // Apply rarity weights: Common 60%, Rare 25%, Epic 10%, Legendary 5%
            float roll = UnityEngine.Random.value;
            Rarity targetRarity;
            if (roll < 0.05f) targetRarity = Rarity.Legendary;
            else if (roll < 0.15f) targetRarity = Rarity.Epic;
            else if (roll < 0.40f) targetRarity = Rarity.Rare;
            else targetRarity = Rarity.Common;

            // Find cards of target rarity
            var rarityPool = pool.Where(c => c.rarity == targetRarity).ToList();
            if (rarityPool.Count == 0)
                rarityPool = pool; // fallback to full pool

            return rarityPool[UnityEngine.Random.Range(0, rarityPool.Count)];
        }
    }

    public class PackDefinition
    {
        public string Name { get; }
        public Element Element { get; }
        public Color Accent { get; }

        public PackDefinition(string name, Element element, Color accent)
        {
            Name = name;
            Element = element;
            Accent = accent;
        }
    }
}
