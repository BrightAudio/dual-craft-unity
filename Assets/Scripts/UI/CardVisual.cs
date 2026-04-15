// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Card Visual
//  Matches official Duel Craft card design exactly:
//  Black frame, white name banner, edge-to-edge art,
//  bordered description box, stat circle, type line
// ═══════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

namespace DualCraft.UI
{
    using Cards;
    using Core;

    public class CardVisual : MonoBehaviour
    {
        [Header("Card Frame")]
        [SerializeField] private Image outerFrame;
        [SerializeField] private Image artworkImage;

        [Header("Name")]
        [SerializeField] private Image nameBanner;
        [SerializeField] private TextMeshProUGUI nameText;

        [Header("Description")]
        [SerializeField] private Image descBox;
        [SerializeField] private TextMeshProUGUI descriptionText;

        [Header("Bottom Info")]
        [SerializeField] private Image costCircle;
        [SerializeField] private TextMeshProUGUI costText;
        [SerializeField] private Image numberCircle;
        [SerializeField] private TextMeshProUGUI numberText;
        [SerializeField] private TextMeshProUGUI typeLineText;

        [Header("Card Back")]
        [SerializeField] private GameObject cardFront;
        [SerializeField] private Image cardBackImage;

        [Header("Effects")]
        [SerializeField] private Image glowEffect;
        [SerializeField] private Animator cardAnimator;

        private CardData _cardData;
        private bool _isFaceDown;
        private static Sprite _cardBackSprite;
        private static readonly Dictionary<string, Sprite> _artCache = new();

        private static Sprite LoadCardArt(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return null;
            if (_artCache.TryGetValue(cardId, out var cached)) return cached;
            var sprite = Resources.Load<Sprite>("CardArt/" + cardId);
            if (sprite != null) _artCache[cardId] = sprite;
            return sprite;
        }

        public CardData Data => _cardData;

        // ─── Category accent colors ───────────────────────
        public static Color GetCategoryColor(CardCategory cat) => cat switch
        {
            CardCategory.Daemon   => new Color(0.85f, 0.20f, 0.20f),  // crimson
            CardCategory.Pillar   => new Color(0.78f, 0.66f, 0.42f),  // amber/gold
            CardCategory.Conjuror => new Color(0.58f, 0.36f, 0.85f),  // violet
            CardCategory.Domain   => new Color(0.27f, 0.53f, 0.85f),  // blue
            CardCategory.Mask     => new Color(0.55f, 0.25f, 0.70f),  // dark purple
            CardCategory.Seal     => new Color(0.22f, 0.72f, 0.55f),  // teal
            CardCategory.Dispel   => new Color(0.70f, 0.75f, 0.80f),  // silver
            _ => new Color(0.55f, 0.55f, 0.55f),
        };

        public static Color GetElementColor(Element elem) => elem switch
        {
            Element.Flame  => new Color(1.0f, 0.35f, 0.15f),   // fire orange
            Element.Ice    => new Color(0.55f, 0.82f, 1.0f),    // ice blue
            Element.Water  => new Color(0.27f, 0.55f, 1.0f),    // deep blue
            Element.Earth  => new Color(0.60f, 0.45f, 0.25f),   // brown
            Element.Air    => new Color(0.80f, 0.88f, 0.95f),   // pale sky
            Element.Light  => new Color(1.0f, 0.85f, 0.30f),    // golden
            Element.Dark   => new Color(0.40f, 0.18f, 0.55f),   // deep purple
            Element.Nature => new Color(0.30f, 0.72f, 0.30f),   // green
            _ => Color.white,
        };

        public static Color GetRarityColor(Rarity rarity) => rarity switch
        {
            Rarity.Common    => new Color(0.60f, 0.60f, 0.60f),
            Rarity.Rare      => new Color(0.30f, 0.55f, 0.90f),
            Rarity.Epic      => new Color(0.60f, 0.30f, 0.85f),
            Rarity.Legendary => new Color(1.0f, 0.78f, 0.20f),
            _ => Color.white,
        };

        public void SetCard(CardData card)
        {
            _cardData = card;
            _isFaceDown = false;
            Refresh();
        }

        public void SetFaceDown()
        {
            _isFaceDown = true;
            if (cardFront) cardFront.SetActive(false);
            if (cardBackImage)
            {
                cardBackImage.gameObject.SetActive(true);
                if (_cardBackSprite == null)
                    _cardBackSprite = Resources.Load<Sprite>("CardArt/card-back");
                if (_cardBackSprite == null)
                    _cardBackSprite = CardBackGenerator.Generate();
                if (_cardBackSprite != null)
                    cardBackImage.sprite = _cardBackSprite;
                cardBackImage.color = Color.white;
            }
            if (outerFrame)
                outerFrame.color = new Color(0.06f, 0.06f, 0.06f, 1f);
        }

        private void Refresh()
        {
            if (_cardData == null) return;

            // Show front, hide back
            if (cardFront) cardFront.SetActive(true);
            if (cardBackImage) cardBackImage.gameObject.SetActive(false);

            // Get type-specific colors
            Color catColor = GetCategoryColor(_cardData.category);
            Element elem = GetElement();
            Color elemColor = GetElementColor(elem);
            Color rarColor = GetRarityColor(_cardData.rarity);

            // Outer frame: category-tinted dark border
            if (outerFrame)
            {
                Color frameColor = Color.Lerp(new Color(0.06f, 0.06f, 0.06f), catColor, 0.15f);
                outerFrame.color = frameColor;
            }

            // Artwork — fills edge-to-edge inside black frame
            if (artworkImage)
            {
                Sprite art = _cardData.artwork;
                if (art == null)
                    art = LoadCardArt(_cardData.cardId);
                if (art == null)
                    art = CardTextureGenerator.GenerateCardArt(
                        elem, _cardData.category, _cardData.rarity, _cardData.cardId);
                artworkImage.sprite = art;
                artworkImage.color = Color.white;
            }

            // Name banner: white with subtle element tint
            if (nameBanner)
            {
                Color bannerColor = Color.Lerp(new Color(0.97f, 0.97f, 0.97f), elemColor, 0.08f);
                nameBanner.color = new Color(bannerColor.r, bannerColor.g, bannerColor.b, 0.95f);
            }
            if (nameText)
            {
                nameText.text = _cardData.cardName;
                nameText.color = new Color(0.08f, 0.08f, 0.08f, 1f);
            }

            // Description box: white/cream
            if (descBox)
                descBox.color = new Color(0.96f, 0.95f, 0.92f, 1f);
            if (descriptionText)
            {
                descriptionText.text = BuildDescription();
                descriptionText.color = new Color(0.12f, 0.12f, 0.12f, 1f);
            }

            // Cost circle (bottom-left): summon/play cost — hide for pre-placed cards
            int cost = GetCostStat();
            if (costCircle)
            {
                if (cost < 0)
                {
                    costCircle.gameObject.SetActive(false);
                    if (costText) costText.gameObject.SetActive(false);
                }
                else
                {
                    costCircle.gameObject.SetActive(true);
                    if (costText) costText.gameObject.SetActive(true);
                    Color costColor = Color.Lerp(new Color(0.18f, 0.35f, 0.65f), elemColor, 0.3f);
                    costCircle.color = costColor;
                }
            }
            if (costText && cost >= 0)
            {
                costText.text = cost.ToString();
                costText.color = Color.white;
            }

            // Number circle (bottom-right): health/key stat, rarity-tinted
            if (numberCircle)
            {
                Color circleColor = Color.Lerp(new Color(0.96f, 0.95f, 0.92f), rarColor, 0.25f);
                numberCircle.color = circleColor;
            }
            if (numberText)
            {
                numberText.text = GetMainStat().ToString();
                numberText.color = new Color(0.08f, 0.08f, 0.08f, 1f);
            }

            // Type line: element-colored
            if (typeLineText)
            {
                typeLineText.text = GetTypeLine();
                typeLineText.color = Color.Lerp(new Color(0.55f, 0.55f, 0.55f), elemColor, 0.5f);
            }

            // Glow — gold, hover only
            if (glowEffect)
            {
                glowEffect.color = new Color(0.784f, 0.663f, 0.416f, 0.3f);
                glowEffect.gameObject.SetActive(false);
            }
        }

        private string BuildDescription()
        {
            string desc = _cardData.description ?? "";

            if (_cardData is DaemonCardData daemon)
            {
                // Show attack as a listed move with damage
                if (daemon.attack > 0)
                    desc += $"\n\nAttack  {daemon.attack} damage";

                if (daemon.ability != null && !string.IsNullOrEmpty(daemon.ability.abilityName))
                    desc += $"\n{daemon.ability.abilityName}\n{daemon.ability.description}";
            }
            else if (_cardData is PillarCardData pillar)
            {
                if (!string.IsNullOrEmpty(pillar.passiveAbility))
                    desc += $"\n\n{pillar.passiveAbility}";
            }
            else if (_cardData is ConjurorCardData conjuror)
            {
                if (conjuror.abilities != null)
                {
                    foreach (var ab in conjuror.abilities)
                    {
                        if (ab != null && !string.IsNullOrEmpty(ab.abilityName))
                        {
                            string cost = ab.loyaltyCost >= 0 ? $"+{ab.loyaltyCost}" : ab.loyaltyCost.ToString();
                            desc += $"\n\n{ab.abilityName} [{cost}]\n{ab.description}";
                        }
                    }
                }
            }
            else if (_cardData is MaskCardData mask)
            {
                if (mask.duration > 0)
                    desc += $"\n\nLasts {mask.duration} turns";
            }

            return desc;
        }

        private int GetMainStat()
        {
            // Bottom-right = health/life
            if (_cardData is DaemonCardData d) return d.ashe;
            if (_cardData is PillarCardData p) return p.hp;
            if (_cardData is ConjurorCardData c) return c.loyalty;
            if (_cardData is MaskCardData m) return m.effectValue;
            if (_cardData is SealCardData s) return s.effectValue;
            return _cardData.GetWillCost();
        }

        private int GetCostStat()
        {
            // Bottom-left = energy cost (only for cards played from hand)
            if (_cardData is DaemonCardData d) return d.asheCost;
            // Pillars & Conjurors are placed at game start, no cost
            if (_cardData is PillarCardData) return -1;
            if (_cardData is ConjurorCardData) return -1;
            return _cardData.GetWillCost() >= 0 ? _cardData.GetWillCost() : 1;
        }

        public bool HasCost() => GetCostStat() >= 0;

        private Element GetElement()
        {
            if (_cardData is DaemonCardData d) return d.element;
            if (_cardData is PillarCardData p) return p.element;
            if (_cardData is ConjurorCardData c) return c.element;
            if (_cardData is DomainCardData dm) return dm.effectElement;
            return Element.Light;
        }

        private string GetTypeLine()
        {
            if (_cardData is DaemonCardData d)
                return $"{d.element} Daemon";
            if (_cardData is PillarCardData p)
                return $"{p.element} Pillar";
            if (_cardData is ConjurorCardData c)
                return $"{c.element} Conjuror";
            return _cardData.category.ToString();
        }

        // ─── Animations ───────────────────────────────────
        public void PlaySummonAnimation() { if (cardAnimator) cardAnimator.SetTrigger("Summon"); }
        public void PlayAttackAnimation() { if (cardAnimator) cardAnimator.SetTrigger("Attack"); }
        public void PlayDestroyAnimation() { if (cardAnimator) cardAnimator.SetTrigger("Destroy"); }
        public void PlayFlipAnimation() { if (cardAnimator) cardAnimator.SetTrigger("Flip"); }

        public void SetHighlight(bool active, Color? color = null)
        {
            if (!glowEffect) return;
            glowEffect.gameObject.SetActive(active);
            if (active && color.HasValue)
            {
                Color c = color.Value;
                c.a = 0.5f;
                glowEffect.color = c;
            }
        }

        public void OnPointerEnter()
        {
            transform.localScale = Vector3.one * 1.05f;
            SetHighlight(true);
        }

        public void OnPointerExit()
        {
            transform.localScale = Vector3.one;
            SetHighlight(false);
        }
    }
}
