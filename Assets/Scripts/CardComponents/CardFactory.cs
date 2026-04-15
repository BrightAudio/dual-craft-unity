// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Card Factory (Post 3: Factory Pattern)
//  "Create a Factory class that parses through data,
//   attaches necessary components and populates with values."
//
//  CardFactory.Create(cardId) → fully assembled GameObject
//  with all the right components for that card type.
// ═══════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DualCraft.Core;
using DualCraft.Data;

namespace DualCraft.CardComponents
{
    public static class CardFactory
    {
        /// <summary>
        /// Create a card GameObject from a card ID.
        /// Loads data from CardLoader, attaches components based on type.
        /// This is the runtime equivalent of a prefab — built from data.
        /// </summary>
        public static GameObject Create(string cardId, Transform parent = null)
        {
            var card = CardLoader.Instance.GetCard(cardId);
            if (card == null)
            {
                Debug.LogWarning($"[CardFactory] Card not found: {cardId}");
                return null;
            }
            return Create(card, parent);
        }

        /// <summary>
        /// Create a card GameObject from RuntimeCard data.
        /// </summary>
        public static GameObject Create(RuntimeCard card, Transform parent = null)
        {
            var go = new GameObject($"Card_{card.Id}");
            if (parent != null) go.transform.SetParent(parent, false);

            // Every card gets Identity + Cost + Display
            var identity = go.AddComponent<CardIdentity>();
            identity.Setup(card);

            var cost = go.AddComponent<CostComponent>();
            cost.Setup(card);

            // Stats — only for types that have combat stats
            StatsComponent stats = null;
            if (card.Category == CardCategory.Daemon
                || card.Category == CardCategory.Pillar
                || card.Category == CardCategory.Mask)
            {
                stats = go.AddComponent<StatsComponent>();
                stats.Setup(card);
            }

            // Abilities — only for cards that have effects
            if (card.Effects != null && card.Effects.Length > 0)
            {
                var ability = go.AddComponent<AbilityComponent>();
                ability.Setup(card);
            }

            // Action — only for daemons (attack, summoning sickness, status)
            if (card.Category == CardCategory.Daemon)
            {
                var action = go.AddComponent<ActionComponent>();
                action.Setup();
            }

            // Display — builds the visual hierarchy
            var display = go.AddComponent<DisplayComponent>();
            BuildVisualHierarchy(go, card, display);
            display.Setup(identity, stats, cost);

            return go;
        }

        /// <summary>
        /// Create a card back (face-down card) GameObject.
        /// </summary>
        public static GameObject CreateCardBack(Transform parent = null)
        {
            var go = new GameObject("CardBack");
            if (parent != null) go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(180, 252);

            var img = go.AddComponent<Image>();
            var sprite = Resources.Load<Sprite>("CardArt/card-back");
            if (sprite != null) img.sprite = sprite;
            img.color = Color.white;

            return go;
        }

        // ─── Visual hierarchy builder ────────────────────

        private static void BuildVisualHierarchy(GameObject go, RuntimeCard card,
            DisplayComponent display)
        {
            // Root RectTransform
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(180, 252);

            // Card frame background
            var frame = CreateChild(go.transform, "Frame", new Vector2(180, 252));
            display.FrameImage = frame.gameObject.AddComponent<Image>();
            display.FrameImage.color = GetFrameColor(card);

            // Artwork
            var artHolder = CreateChild(go.transform, "Artwork", new Vector2(160, 110));
            artHolder.anchoredPosition = new Vector2(0, 40);
            display.ArtworkImage = artHolder.gameObject.AddComponent<Image>();
            display.ArtworkImage.color = Color.white;

            // Load card art
            var artSprite = Resources.Load<Sprite>(card.ArtPath);
            if (artSprite != null)
                display.ArtworkImage.sprite = artSprite;

            // Name bar
            var nameBar = CreateChild(go.transform, "NameBar", new Vector2(160, 24));
            nameBar.anchoredPosition = new Vector2(0, 96);
            var nameImg = nameBar.gameObject.AddComponent<Image>();
            nameImg.color = new Color(0, 0, 0, 0.7f);
            display.NameText = CreateText(nameBar, "NameText", card.Name, 12,
                TextAlignmentOptions.Center, new Vector2(150, 22));

            // Cost crystal (top-left)
            var costHolder = CreateChild(go.transform, "Cost", new Vector2(30, 30));
            costHolder.anchoredPosition = new Vector2(-70, 106);
            var costBg = costHolder.gameObject.AddComponent<Image>();
            costBg.color = new Color(0.1f, 0.3f, 0.8f, 0.9f);
            display.CostText = CreateText(costHolder, "CostText",
                card.GetWillCost().ToString(), 14,
                TextAlignmentOptions.Center, new Vector2(28, 28));

            // Description area
            var descArea = CreateChild(go.transform, "Desc", new Vector2(160, 50));
            descArea.anchoredPosition = new Vector2(0, -30);
            display.DescriptionText = CreateText(descArea, "DescText",
                card.Description, 8,
                TextAlignmentOptions.TopLeft, new Vector2(155, 48));

            // Bottom stat bar (for daemons and pillars)
            if (card.Category == CardCategory.Daemon)
            {
                // Attack (bottom-left)
                var atkHolder = CreateChild(go.transform, "Atk", new Vector2(30, 30));
                atkHolder.anchoredPosition = new Vector2(-70, -106);
                var atkBg = atkHolder.gameObject.AddComponent<Image>();
                atkBg.color = new Color(0.8f, 0.2f, 0.1f, 0.9f);
                display.AttackText = CreateText(atkHolder, "AtkText",
                    card.Attack.ToString(), 14,
                    TextAlignmentOptions.Center, new Vector2(28, 28));

                // Ashe (bottom-right)
                var asheHolder = CreateChild(go.transform, "Ashe", new Vector2(30, 30));
                asheHolder.anchoredPosition = new Vector2(70, -106);
                var asheBg = asheHolder.gameObject.AddComponent<Image>();
                asheBg.color = new Color(0.1f, 0.7f, 0.2f, 0.9f);
                display.AsheText = CreateText(asheHolder, "AsheText",
                    card.Ashe.ToString(), 14,
                    TextAlignmentOptions.Center, new Vector2(28, 28));
            }
            else if (card.Category == CardCategory.Pillar)
            {
                // HP (bottom-right)
                var hpHolder = CreateChild(go.transform, "Hp", new Vector2(30, 30));
                hpHolder.anchoredPosition = new Vector2(70, -106);
                var hpBg = hpHolder.gameObject.AddComponent<Image>();
                hpBg.color = new Color(0.1f, 0.7f, 0.2f, 0.9f);
                display.AsheText = CreateText(hpHolder, "HpText",
                    card.Hp.ToString(), 14,
                    TextAlignmentOptions.Center, new Vector2(28, 28));

                // Loyalty (bottom-left)
                var loyHolder = CreateChild(go.transform, "Loyalty", new Vector2(30, 30));
                loyHolder.anchoredPosition = new Vector2(-70, -106);
                var loyBg = loyHolder.gameObject.AddComponent<Image>();
                loyBg.color = new Color(0.7f, 0.6f, 0.1f, 0.9f);
                display.AttackText = CreateText(loyHolder, "LoyaltyText",
                    card.Loyalty.ToString(), 14,
                    TextAlignmentOptions.Center, new Vector2(28, 28));
            }

            // Rarity gem (top-right)
            var rarityHolder = CreateChild(go.transform, "Rarity", new Vector2(16, 16));
            rarityHolder.anchoredPosition = new Vector2(72, 112);
            display.RarityGem = rarityHolder.gameObject.AddComponent<Image>();
            display.RarityGem.color = GetRarityColor(card.Rarity);

            // Element indicator (small icon next to name)
            var elemHolder = CreateChild(go.transform, "Element", new Vector2(18, 18));
            elemHolder.anchoredPosition = new Vector2(72, 96);
            display.ElementIcon = elemHolder.gameObject.AddComponent<Image>();
            display.ElementIcon.color = GetElementColor(card.Element);
        }

        // ─── Helpers ─────────────────────────────────────

        private static RectTransform CreateChild(Transform parent, string name, Vector2 size)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            var rt = child.AddComponent<RectTransform>();
            rt.sizeDelta = size;
            return rt;
        }

        private static TextMeshProUGUI CreateText(RectTransform parent, string name,
            string text, int fontSize, TextAlignmentOptions align, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = size;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = align;
            tmp.color = Color.white;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            return tmp;
        }

        private static Color GetFrameColor(RuntimeCard card)
        {
            return card.Category switch
            {
                CardCategory.Daemon => new Color(0.15f, 0.12f, 0.08f),
                CardCategory.Pillar => new Color(0.08f, 0.12f, 0.18f),
                CardCategory.Domain => new Color(0.18f, 0.08f, 0.18f),
                CardCategory.Mask => new Color(0.12f, 0.08f, 0.15f),
                CardCategory.Seal => new Color(0.08f, 0.15f, 0.08f),
                CardCategory.Dispel => new Color(0.18f, 0.18f, 0.08f),
                CardCategory.Conjuror => new Color(0.2f, 0.15f, 0.05f),
                _ => new Color(0.1f, 0.1f, 0.1f),
            };
        }

        private static Color GetRarityColor(Rarity rarity)
        {
            return rarity switch
            {
                Rarity.Common => new Color(0.6f, 0.6f, 0.6f),
                Rarity.Rare => new Color(0.2f, 0.5f, 1f),
                Rarity.Epic => new Color(0.6f, 0.2f, 0.9f),
                Rarity.Legendary => new Color(1f, 0.8f, 0.1f),
                _ => Color.white,
            };
        }

        private static Color GetElementColor(Element element)
        {
            return element switch
            {
                Element.Flame => new Color(1f, 0.3f, 0.1f),
                Element.Ice => new Color(0.5f, 0.8f, 1f),
                Element.Water => new Color(0.2f, 0.5f, 1f),
                Element.Earth => new Color(0.6f, 0.4f, 0.2f),
                Element.Air => new Color(0.8f, 0.9f, 1f),
                Element.Light => new Color(1f, 1f, 0.7f),
                Element.Dark => new Color(0.3f, 0.1f, 0.4f),
                Element.Nature => new Color(0.2f, 0.8f, 0.3f),
                _ => Color.white,
            };
        }
    }
}
