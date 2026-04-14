// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Collection/Grimoire Screen
//  Search, filter, sort, group — Pokemon TCG inspired
// ═══════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

namespace DualCraft.UI
{
    using Cards;
    using Core;

    public class CollectionScreen : MonoBehaviour
    {
        [Header("Database")]
        [SerializeField] private CardDatabase cardDatabase;

        [Header("Card Grid")]
        [SerializeField] private Transform cardGridContainer;
        [SerializeField] private GameObject cardThumbnailPrefab;
        [SerializeField] private bool binderMode = true; // numbered slot grid like PTCG Pocket

        [Header("Search & Filter")]
        [SerializeField] private TMP_InputField searchInput;
        [SerializeField] private TMP_Dropdown categoryDropdown;
        [SerializeField] private TMP_Dropdown elementDropdown;
        [SerializeField] private TMP_Dropdown rarityDropdown;
        [SerializeField] private TMP_Dropdown sortDropdown;
        [SerializeField] private Toggle ascendingToggle;

        [Header("Group Buttons")]
        [SerializeField] private Button groupNoneButton;
        [SerializeField] private Button groupElementButton;
        [SerializeField] private Button groupCategoryButton;
        [SerializeField] private Button groupRarityButton;

        [Header("Card Detail")]
        [SerializeField] private GameObject cardDetailPanel;
        [SerializeField] private CardVisual detailCardVisual;
        [SerializeField] private TextMeshProUGUI detailNameText;
        [SerializeField] private TextMeshProUGUI detailTypeText;
        [SerializeField] private TextMeshProUGUI detailDescText;
        [SerializeField] private TextMeshProUGUI detailFlavorText;
        [SerializeField] private TextMeshProUGUI detailStatsText;
        [SerializeField] private Button detailCloseButton;

        [Header("Result Count")]
        [SerializeField] private TextMeshProUGUI resultCountText;

        // Filter state
        private string _searchQuery = "";
        private CardCategory? _categoryFilter;
        private Element? _elementFilter;
        private Rarity? _rarityFilter;
        private string _sortBy = "name";
        private bool _ascending = true;

        private void Start()
        {
            cardDatabase.Initialize();

            searchInput?.onValueChanged.AddListener(OnSearchChanged);
            categoryDropdown?.onValueChanged.AddListener(OnCategoryChanged);
            elementDropdown?.onValueChanged.AddListener(OnElementChanged);
            rarityDropdown?.onValueChanged.AddListener(OnRarityChanged);
            sortDropdown?.onValueChanged.AddListener(OnSortChanged);

            groupNoneButton?.onClick.AddListener(() => RefreshGrid());
            groupElementButton?.onClick.AddListener(() => RefreshGrid());
            groupCategoryButton?.onClick.AddListener(() => RefreshGrid());
            groupRarityButton?.onClick.AddListener(() => RefreshGrid());

            detailCloseButton?.onClick.AddListener(() => cardDetailPanel?.SetActive(false));

            if (cardDetailPanel) cardDetailPanel.SetActive(false);

            RefreshGrid();
        }

        private void OnSearchChanged(string value)
        {
            _searchQuery = value;
            RefreshGrid();
        }

        private void OnCategoryChanged(int index)
        {
            _categoryFilter = index == 0 ? null : (CardCategory)(index - 1);
            RefreshGrid();
        }

        private void OnElementChanged(int index)
        {
            _elementFilter = index == 0 ? null : (Element)(index - 1);
            RefreshGrid();
        }

        private void OnRarityChanged(int index)
        {
            _rarityFilter = index == 0 ? null : (Rarity)(index - 1);
            RefreshGrid();
        }

        private void OnSortChanged(int index)
        {
            _sortBy = index switch
            {
                0 => "name",
                1 => "rarity",
                2 => "element",
                3 => "attack",
                4 => "ashe",
                5 => "willCost",
                _ => "name",
            };
            RefreshGrid();
        }

        private void RefreshGrid()
        {
            // Clear existing cards
            for (int i = cardGridContainer.childCount - 1; i >= 0; i--)
                Destroy(cardGridContainer.GetChild(i).gameObject);

            var allCards = cardDatabase.GetAllCards();

            if (binderMode && string.IsNullOrWhiteSpace(_searchQuery) && !_categoryFilter.HasValue && !_elementFilter.HasValue && !_rarityFilter.HasValue)
            {
                // Binder mode: show numbered slots with cards filling in (PTCG Pocket style)
                int totalSlots = Mathf.Max(allCards.Count, 120); // At least 120 grid slots
                for (int i = 0; i < totalSlots; i++)
                {
                    if (i < allCards.Count)
                    {
                        // Filled slot — show card
                        var go = Instantiate(cardThumbnailPrefab, cardGridContainer);
                        var visual = go.GetComponent<CardVisual>();
                        if (visual != null)
                            visual.SetCard(allCards[i]);

                        // Slot number badge (bottom-left, like PTCG Pocket)
                        var slotBadge = new GameObject("SlotBadge");
                        slotBadge.transform.SetParent(go.transform, false);
                        var sbrt = slotBadge.AddComponent<RectTransform>();
                        sbrt.anchoredPosition = new Vector2(-70, -120);
                        sbrt.sizeDelta = new Vector2(36, 22);
                        var sbImg = slotBadge.AddComponent<Image>();
                        sbImg.color = new Color(0.25f, 0.28f, 0.35f, 0.9f);
                        var sbTextGO = new GameObject("Text");
                        sbTextGO.transform.SetParent(slotBadge.transform, false);
                        var sbtrt = sbTextGO.AddComponent<RectTransform>();
                        sbtrt.anchoredPosition = Vector2.zero;
                        sbtrt.sizeDelta = new Vector2(36, 22);
                        var sbText = sbTextGO.AddComponent<TextMeshProUGUI>();
                        sbText.text = (i + 1).ToString();
                        sbText.fontSize = 10;
                        sbText.fontStyle = FontStyles.Bold;
                        sbText.alignment = TextAlignmentOptions.Center;
                        sbText.color = Color.white;

                        var button = go.GetComponent<Button>();
                        if (button == null) button = go.AddComponent<Button>();
                        var capturedCard = allCards[i];
                        button.onClick.AddListener(() => ShowCardDetail(capturedCard));
                    }
                    else
                    {
                        // Empty slot — numbered placeholder (like PTCG Pocket binder)
                        var emptySlot = new GameObject($"Slot_{i + 1:000}");
                        emptySlot.transform.SetParent(cardGridContainer, false);
                        var esrt = emptySlot.AddComponent<RectTransform>();
                        esrt.sizeDelta = new Vector2(200, 280);
                        var esImg = emptySlot.AddComponent<Image>();
                        esImg.color = new Color(0.08f, 0.08f, 0.12f, 0.5f);

                        // Slot number in center
                        var numGO = new GameObject("Number");
                        numGO.transform.SetParent(emptySlot.transform, false);
                        var numrt = numGO.AddComponent<RectTransform>();
                        numrt.anchoredPosition = Vector2.zero;
                        numrt.sizeDelta = new Vector2(100, 40);
                        var numText = numGO.AddComponent<TextMeshProUGUI>();
                        numText.text = $"{i + 1:000}";
                        numText.fontSize = 26;
                        numText.fontStyle = FontStyles.Bold;
                        numText.alignment = TextAlignmentOptions.Center;
                        numText.color = new Color(0.25f, 0.25f, 0.3f, 0.6f);

                        emptySlot.AddComponent<LayoutElement>().preferredWidth = 200;
                    }
                }

                if (resultCountText) resultCountText.text = $"{allCards.Count}/{totalSlots}";
                return;
            }

            // Filtered/search mode: just show matching cards
            var filtered = FilterCards(allCards);
            var sorted = SortCards(filtered);

            foreach (var card in sorted)
            {
                var go = Instantiate(cardThumbnailPrefab, cardGridContainer);
                var visual = go.GetComponent<CardVisual>();
                if (visual != null)
                {
                    visual.SetCard(card);
                }

                // Click to show detail
                var button = go.GetComponent<Button>();
                if (button == null) button = go.AddComponent<Button>();
                var capturedCard = card;
                button.onClick.AddListener(() => ShowCardDetail(capturedCard));
            }

            if (resultCountText) resultCountText.text = $"{sorted.Count} cards";
        }

        private List<CardData> FilterCards(IReadOnlyList<CardData> cards)
        {
            var result = new List<CardData>(cards);

            // Category filter
            if (_categoryFilter.HasValue)
                result.RemoveAll(c => c.category != _categoryFilter.Value);

            // Element filter
            if (_elementFilter.HasValue)
            {
                result.RemoveAll(c =>
                {
                    if (c is DaemonCardData d) return d.element != _elementFilter.Value;
                    if (c is PillarCardData p) return p.element != _elementFilter.Value;
                    if (c is ConjurorCardData cj) return cj.element != _elementFilter.Value;
                    return true;
                });
            }

            // Rarity filter
            if (_rarityFilter.HasValue)
                result.RemoveAll(c => c.rarity != _rarityFilter.Value);

            // Search
            if (!string.IsNullOrWhiteSpace(_searchQuery))
            {
                var q = _searchQuery.ToLower();
                result.RemoveAll(c =>
                    !c.cardName.ToLower().Contains(q) &&
                    !c.description.ToLower().Contains(q));
            }

            return result;
        }

        private List<CardData> SortCards(List<CardData> cards)
        {
            IOrderedEnumerable<CardData> sorted = _sortBy switch
            {
                "name" => _ascending ? cards.OrderBy(c => c.cardName) : cards.OrderByDescending(c => c.cardName),
                "rarity" => _ascending ? cards.OrderBy(c => c.rarity) : cards.OrderByDescending(c => c.rarity),
                "element" => _ascending
                    ? cards.OrderBy(c => c is DaemonCardData d ? (int)d.element : 99)
                    : cards.OrderByDescending(c => c is DaemonCardData d ? (int)d.element : 99),
                "attack" => _ascending
                    ? cards.OrderBy(c => c is DaemonCardData d ? d.attack : 0)
                    : cards.OrderByDescending(c => c is DaemonCardData d ? d.attack : 0),
                "ashe" => _ascending
                    ? cards.OrderBy(c => c is DaemonCardData d ? d.ashe : 0)
                    : cards.OrderByDescending(c => c is DaemonCardData d ? d.ashe : 0),
                "willCost" => _ascending
                    ? cards.OrderBy(c => c.GetWillCost())
                    : cards.OrderByDescending(c => c.GetWillCost()),
                _ => cards.OrderBy(c => c.cardName),
            };

            return sorted.ToList();
        }

        private void ShowCardDetail(CardData card)
        {
            if (cardDetailPanel == null) return;
            cardDetailPanel.SetActive(true);

            if (detailCardVisual) detailCardVisual.SetCard(card);
            if (detailNameText) detailNameText.text = card.cardName;
            if (detailDescText) detailDescText.text = card.description;
            if (detailFlavorText) detailFlavorText.text = card.flavorText ?? "";

            if (detailTypeText)
            {
                detailTypeText.text = card switch
                {
                    DaemonCardData d => $"{d.element} {d.creatureType} — {d.rarity}",
                    PillarCardData p => $"{p.element} Pillar — {p.rarity}",
                    ConjurorCardData c => $"{c.element} Conjuror — {c.rarity}",
                    _ => $"{card.category} — {card.rarity}",
                };
                detailTypeText.color = CardVisual.GetCategoryColor(card.category);
            }

            if (detailStatsText)
            {
                detailStatsText.text = card switch
                {
                    DaemonCardData d => $"Attack: {d.attack}  |  Health: {d.ashe}  |  Cost: {d.asheCost}",
                    PillarCardData p => $"Health: {p.hp}  |  Placed at start",
                    ConjurorCardData c => $"Loyalty: {c.loyalty}  |  Placed at start",
                    MaskCardData m => $"Power: +{m.effectValue}  |  Lasts {m.duration} turns",
                    SealCardData s => $"Power: {s.effectValue}",
                    _ => "",
                };
            }
        }
    }
}
