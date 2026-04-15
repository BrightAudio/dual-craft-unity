// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Card Components (Post 3: Fajlworks Pattern)
//  "Instead of one general Card class, create smaller
//   components that make up your Card gameObject."
//
//  Components:
//    CardIdentity   — id, name, category, rarity, art ref
//    CostComponent  — will cost, cost reduction logic
//    StatsComponent — attack, ashe, hp, loyalty
//    DisplayComponent — renders the visual card (art, frame, text)
//    AbilityComponent — holds EffectEntry[] from the database
//    ActionComponent  — can attack, summoning sickness, frozen
// ═══════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DualCraft.Core;
using DualCraft.Data;
using DualCraft.Effects;

namespace DualCraft.CardComponents
{
    // ─── CardIdentity: Who is this card? ─────────────────

    public class CardIdentity : MonoBehaviour
    {
        [HideInInspector] public string CardId;
        [HideInInspector] public string CardName;
        [HideInInspector] public CardCategory Category;
        [HideInInspector] public Rarity Rarity;
        [HideInInspector] public Element Element;
        [HideInInspector] public CreatureType CreatureType;
        [HideInInspector] public string Description;
        [HideInInspector] public string FlavorText;

        /// <summary>The full RuntimeCard data row from the database.</summary>
        [HideInInspector] public RuntimeCard Data;

        public void Setup(RuntimeCard card)
        {
            Data = card;
            CardId = card.Id;
            CardName = card.Name;
            Category = card.Category;
            Rarity = card.Rarity;
            Element = card.Element;
            CreatureType = card.CreatureType;
            Description = card.Description;
            FlavorText = card.FlavorText;
        }
    }

    // ─── CostComponent: Can we afford to play this? ──────

    public class CostComponent : MonoBehaviour
    {
        [HideInInspector] public int BaseCost;
        [HideInInspector] public int CurrentCost;

        public void Setup(RuntimeCard card)
        {
            BaseCost = card.GetWillCost();
            CurrentCost = BaseCost;
        }

        public bool CanAfford(int availableWill)
        {
            return availableWill >= CurrentCost;
        }

        public void ApplyReduction(int amount)
        {
            CurrentCost = Mathf.Max(0, CurrentCost - amount);
        }

        public void ResetCost()
        {
            CurrentCost = BaseCost;
        }
    }

    // ─── StatsComponent: Combat numbers ──────────────────

    public class StatsComponent : MonoBehaviour
    {
        // Daemon stats
        [HideInInspector] public int BaseAttack;
        [HideInInspector] public int CurrentAttack;
        [HideInInspector] public int BaseAshe;
        [HideInInspector] public int CurrentAshe;
        [HideInInspector] public int MaxAshe;

        // Pillar stats
        [HideInInspector] public int BaseHp;
        [HideInInspector] public int CurrentHp;
        [HideInInspector] public int MaxHp;
        [HideInInspector] public int Loyalty;

        // Mask stats
        [HideInInspector] public int Duration;
        [HideInInspector] public int TurnsRemaining;

        public bool IsAlive => Category == CardCategory.Daemon
            ? CurrentAshe > 0
            : Category == CardCategory.Pillar ? CurrentHp > 0 : true;

        private CardCategory Category;

        public void Setup(RuntimeCard card)
        {
            Category = card.Category;
            BaseAttack = card.Attack;
            CurrentAttack = card.Attack;
            BaseAshe = card.Ashe;
            CurrentAshe = card.Ashe;
            MaxAshe = card.Ashe;
            BaseHp = card.Hp;
            CurrentHp = card.Hp;
            MaxHp = card.Hp;
            Loyalty = card.Loyalty;
            Duration = card.Duration;
            TurnsRemaining = card.Duration;
        }

        public void TakeDamage(int amount)
        {
            if (Category == CardCategory.Daemon)
            {
                CurrentAshe = Mathf.Max(0, CurrentAshe - amount);
            }
            else if (Category == CardCategory.Pillar)
            {
                CurrentHp = Mathf.Max(0, CurrentHp - amount);
            }
        }

        public void Heal(int amount)
        {
            if (Category == CardCategory.Daemon)
                CurrentAshe = Mathf.Min(CurrentAshe + amount, MaxAshe);
            else if (Category == CardCategory.Pillar)
                CurrentHp = Mathf.Min(CurrentHp + amount, MaxHp);
        }

        public void BuffAttack(int amount) => CurrentAttack += amount;
        public void DebuffAttack(int amount) => CurrentAttack = Mathf.Max(0, CurrentAttack - amount);
    }

    // ─── AbilityComponent: The effect database row ───────

    public class AbilityComponent : MonoBehaviour
    {
        [HideInInspector] public EffectEntry[] Effects = System.Array.Empty<EffectEntry>();
        [HideInInspector] public bool Silenced;

        public void Setup(RuntimeCard card)
        {
            Effects = card.Effects ?? System.Array.Empty<EffectEntry>();
            Silenced = false;
        }

        public EffectEntry[] GetEffectsForTrigger(EffectTrigger trigger)
        {
            if (Silenced) return System.Array.Empty<EffectEntry>();
            var list = new System.Collections.Generic.List<EffectEntry>();
            foreach (var e in Effects)
            {
                if (e.trigger == trigger)
                    list.Add(e);
            }
            return list.ToArray();
        }

        public bool HasEffect(EffectFunctionId functionId)
        {
            foreach (var e in Effects)
                if (e.functionId == functionId)
                    return true;
            return false;
        }

        public void Silence()
        {
            Silenced = true;
        }
    }

    // ─── ActionComponent: Turn state & action readiness ──

    public class ActionComponent : MonoBehaviour
    {
        [HideInInspector] public bool CanAttack;
        [HideInInspector] public bool HasAttacked;
        [HideInInspector] public bool HasSummoningSickness;

        // Status effects
        [HideInInspector] public bool Frozen;
        [HideInInspector] public int FrozenTurns;
        [HideInInspector] public bool Stealthed;
        [HideInInspector] public int StealthTurns;
        [HideInInspector] public bool Entangled;
        [HideInInspector] public int EntangledTurns;
        [HideInInspector] public bool HasTaunt;
        [HideInInspector] public int TauntTurns;
        [HideInInspector] public int ShieldAmount;
        [HideInInspector] public int ThornsDamage;

        public bool CanPerformAttack => CanAttack && !HasAttacked && !Frozen && !Entangled;

        public void Setup()
        {
            HasSummoningSickness = true;
            CanAttack = false;
            HasAttacked = false;
            Frozen = false;
            Stealthed = false;
            Entangled = false;
            HasTaunt = false;
            ShieldAmount = 0;
            ThornsDamage = 0;
        }

        public void OnNewTurn()
        {
            if (HasSummoningSickness)
            {
                HasSummoningSickness = false;
                CanAttack = true;
            }
            HasAttacked = false;

            // Tick status durations
            if (Frozen && --FrozenTurns <= 0) Frozen = false;
            if (Stealthed && --StealthTurns <= 0) Stealthed = false;
            if (Entangled && --EntangledTurns <= 0) Entangled = false;
            if (HasTaunt && --TauntTurns <= 0) HasTaunt = false;
        }

        public void GrantHaste()
        {
            HasSummoningSickness = false;
            CanAttack = true;
        }
    }

    // ─── DisplayComponent: Visual rendering ──────────────

    public class DisplayComponent : MonoBehaviour
    {
        // References set by CardFactory
        [HideInInspector] public Image ArtworkImage;
        [HideInInspector] public Image FrameImage;
        [HideInInspector] public Image ElementIcon;
        [HideInInspector] public TextMeshProUGUI NameText;
        [HideInInspector] public TextMeshProUGUI CostText;
        [HideInInspector] public TextMeshProUGUI AttackText;
        [HideInInspector] public TextMeshProUGUI AsheText;
        [HideInInspector] public TextMeshProUGUI DescriptionText;
        [HideInInspector] public Image RarityGem;

        private CardIdentity _identity;
        private StatsComponent _stats;
        private CostComponent _cost;

        public void Setup(CardIdentity identity, StatsComponent stats, CostComponent cost)
        {
            _identity = identity;
            _stats = stats;
            _cost = cost;
            Refresh();
        }

        public void Refresh()
        {
            if (_identity == null) return;

            if (NameText != null) NameText.text = _identity.CardName;
            if (CostText != null) CostText.text = _cost?.CurrentCost.ToString() ?? "";
            if (DescriptionText != null) DescriptionText.text = _identity.Description;

            if (_identity.Category == CardCategory.Daemon)
            {
                if (AttackText != null) AttackText.text = _stats?.CurrentAttack.ToString() ?? "";
                if (AsheText != null) AsheText.text = _stats?.CurrentAshe.ToString() ?? "";
            }
            else if (_identity.Category == CardCategory.Pillar)
            {
                if (AttackText != null) AttackText.text = ""; // Pillars don't attack
                if (AsheText != null) AsheText.text = _stats?.CurrentHp.ToString() ?? "";
            }

            // Load artwork from Resources
            if (ArtworkImage != null && _identity.Data != null)
            {
                var sprite = Resources.Load<Sprite>(_identity.Data.ArtPath);
                if (sprite != null) ArtworkImage.sprite = sprite;
            }
        }

        public void FlashDamage()
        {
            // Visual feedback — can be enhanced by UIAnimUtils
            if (AsheText != null)
                AsheText.color = Color.red;
        }

        public void FlashHeal()
        {
            if (AsheText != null)
                AsheText.color = Color.green;
        }

        public void ResetColors()
        {
            if (AsheText != null)
                AsheText.color = Color.white;
            if (AttackText != null)
                AttackText.color = Color.white;
        }
    }
}
