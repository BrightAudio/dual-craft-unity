// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Deck Builder Screen
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

    public class DeckBuilderScreen : MonoBehaviour
    {
        [Header("Database")]
        [SerializeField] private CardDatabase cardDatabase;

        [Header("Card Pool")]
        [SerializeField] private Transform cardPoolContainer;
        [SerializeField] private GameObject cardThumbnailPrefab;
        [SerializeField] private TMP_Dropdown elementFilterDropdown;

        [Header("Deck Panel")]
        [SerializeField] private Transform deckListContainer;
        [SerializeField] private Transform pillarListContainer;
        [SerializeField] private GameObject deckEntryPrefab;
        [SerializeField] private TMP_InputField deckNameInput;
        [SerializeField] private TMP_Dropdown deckElementDropdown;

        [Header("Stats")]
        [SerializeField] private TextMeshProUGUI cardCountText;
        [SerializeField] private TextMeshProUGUI pillarCountText;
        [SerializeField] private Button saveButton;
        [SerializeField] private Button clearButton;

        // Deck being built
        private Element _deckElement = Element.Flame;
        private readonly List<CardData> _deckCards = new();
        private readonly List<PillarCardData> _deckPillars = new();

        private void Start()
        {
            cardDatabase.Initialize();

            elementFilterDropdown?.onValueChanged.AddListener(OnFilterChanged);
            deckElementDropdown?.onValueChanged.AddListener(i => { _deckElement = (Element)i; RefreshPool(); });
            saveButton?.onClick.AddListener(SaveDeck);
            clearButton?.onClick.AddListener(ClearDeck);

            RefreshPool();
            RefreshDeckPanel();
        }

        private void OnFilterChanged(int index)
        {
            RefreshPool();
        }

        private void RefreshPool()
        {
            ClearChildren(cardPoolContainer);

            var cards = cardDatabase.GetAllCards()
                .Where(c =>
                {
                    // Show daemons/spells of the selected element + neutral spells
                    if (c is DaemonCardData d) return d.element == _deckElement;
                    if (c is PillarCardData p) return p.element == _deckElement;
                    return true; // Masks, seals, domains, dispels are element-neutral
                })
                .OrderBy(c => c.GetWillCost())
                .ToList();

            foreach (var card in cards)
            {
                var go = Instantiate(cardThumbnailPrefab, cardPoolContainer);
                var visual = go.GetComponent<CardVisual>();
                if (visual != null) visual.SetCard(card);

                var btn = go.GetComponent<Button>();
                if (btn == null) btn = go.AddComponent<Button>();
                var capturedCard = card;
                btn.onClick.AddListener(() => AddCardToDeck(capturedCard));
            }
        }

        private void AddCardToDeck(CardData card)
        {
            if (card is PillarCardData pillar)
            {
                if (_deckPillars.Count >= GameConstants.PillarCount) return;
                if (pillar.element != _deckElement) return;
                _deckPillars.Add(pillar);
            }
            else
            {
                if (_deckCards.Count >= GameConstants.DeckSize) return;
                int copies = _deckCards.Count(c => c.cardId == card.cardId);
                if (copies >= GameConstants.MaxCardCopies) return;
                _deckCards.Add(card);
            }

            RefreshDeckPanel();
        }

        private void RemoveCardFromDeck(int index, bool isPillar)
        {
            if (isPillar)
            {
                if (index >= 0 && index < _deckPillars.Count)
                    _deckPillars.RemoveAt(index);
            }
            else
            {
                if (index >= 0 && index < _deckCards.Count)
                    _deckCards.RemoveAt(index);
            }

            RefreshDeckPanel();
        }

        private void RefreshDeckPanel()
        {
            // Card count
            if (cardCountText)
            {
                bool full = _deckCards.Count == GameConstants.DeckSize;
                cardCountText.text = $"Cards: {_deckCards.Count}/{GameConstants.DeckSize}";
                cardCountText.color = full ? Color.green : Color.white;
            }

            if (pillarCountText)
            {
                bool full = _deckPillars.Count == GameConstants.PillarCount;
                pillarCountText.text = $"Pillars: {_deckPillars.Count}/{GameConstants.PillarCount}";
                pillarCountText.color = full ? Color.green : Color.white;
            }

            // Save button state
            if (saveButton)
                saveButton.interactable = _deckCards.Count == GameConstants.DeckSize && _deckPillars.Count == GameConstants.PillarCount;

            // Refresh pillar list
            ClearChildren(pillarListContainer);
            for (int i = 0; i < _deckPillars.Count; i++)
            {
                var go = Instantiate(deckEntryPrefab, pillarListContainer);
                var text = go.GetComponentInChildren<TextMeshProUGUI>();
                if (text) text.text = _deckPillars[i].cardName;

                int capturedIndex = i;
                var btn = go.GetComponent<Button>();
                if (btn != null) btn.onClick.AddListener(() => RemoveCardFromDeck(capturedIndex, true));
            }

            // Refresh deck card list
            ClearChildren(deckListContainer);
            for (int i = 0; i < _deckCards.Count; i++)
            {
                var go = Instantiate(deckEntryPrefab, deckListContainer);
                var text = go.GetComponentInChildren<TextMeshProUGUI>();
                if (text) text.text = _deckCards[i].cardName;

                int capturedIndex = i;
                var btn = go.GetComponent<Button>();
                if (btn != null) btn.onClick.AddListener(() => RemoveCardFromDeck(capturedIndex, false));
            }
        }

        private void SaveDeck()
        {
            if (_deckCards.Count != GameConstants.DeckSize || _deckPillars.Count != GameConstants.PillarCount) return;

            string deckName = deckNameInput?.text ?? "Custom Deck";
            Debug.Log($"[DeckBuilder] Saved deck '{deckName}' with {_deckCards.Count} cards and {_deckPillars.Count} pillars.");

            // TODO: Save to PlayerPrefs or persistent data
        }

        private void ClearDeck()
        {
            _deckCards.Clear();
            _deckPillars.Clear();
            RefreshDeckPanel();
        }

        private void ClearChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
                Destroy(parent.GetChild(i).gameObject);
        }
    }
}
