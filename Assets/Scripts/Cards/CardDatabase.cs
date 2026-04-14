// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Card Database (Runtime Registry)
//  Like Pokemon TCG's JSON sets → loaded at runtime
// ═══════════════════════════════════════════════════════

using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace DualCraft.Cards
{
    using Core;

    [CreateAssetMenu(fileName = "CardDatabase", menuName = "Dual Craft/Card Database")]
    public class CardDatabase : ScriptableObject
    {
        [Header("All Card Data")]
        public DaemonCardData[] daemons;
        public PillarCardData[] pillars;
        public DomainCardData[] domains;
        public MaskCardData[] masks;
        public SealCardData[] seals;
        public DispelCardData[] dispels;
        public ConjurorCardData[] conjurors;

        private Dictionary<string, CardData> _lookup;

        public void Initialize()
        {
            _lookup = new Dictionary<string, CardData>();
            RegisterAll(daemons);
            RegisterAll(pillars);
            RegisterAll(domains);
            RegisterAll(masks);
            RegisterAll(seals);
            RegisterAll(dispels);
            RegisterAll(conjurors);
        }

        private void RegisterAll<T>(T[] cards) where T : CardData
        {
            if (cards == null) return;
            foreach (var card in cards)
            {
                if (card != null && !string.IsNullOrEmpty(card.cardId))
                    _lookup[card.cardId] = card;
            }
        }

        public CardData GetCard(string cardId)
        {
            if (_lookup == null) Initialize();
            return _lookup.GetValueOrDefault(cardId);
        }

        public IReadOnlyList<CardData> GetAllCards()
        {
            if (_lookup == null) Initialize();
            return _lookup.Values.ToList();
        }

        public IReadOnlyList<T> GetCardsByType<T>() where T : CardData
        {
            if (_lookup == null) Initialize();
            return _lookup.Values.OfType<T>().ToList();
        }

        public IReadOnlyList<CardData> GetCardsByElement(Element element)
        {
            if (_lookup == null) Initialize();
            return _lookup.Values.Where(c =>
            {
                if (c is DaemonCardData d) return d.element == element;
                if (c is PillarCardData p) return p.element == element;
                if (c is ConjurorCardData cj) return cj.element == element;
                return false;
            }).ToList();
        }

        public IReadOnlyList<CardData> GetCardsByRarity(Rarity rarity)
        {
            if (_lookup == null) Initialize();
            return _lookup.Values.Where(c => c.rarity == rarity).ToList();
        }

        public IReadOnlyList<DaemonCardData> GetDaemonsByCreatureType(CreatureType type)
        {
            if (_lookup == null) Initialize();
            return daemons?.Where(d => d != null && d.creatureType == type).ToList()
                ?? new List<DaemonCardData>();
        }

        public int TotalCards
        {
            get
            {
                if (_lookup == null) Initialize();
                return _lookup.Count;
            }
        }
    }
}
