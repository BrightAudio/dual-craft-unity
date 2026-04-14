using UnityEngine;

namespace DualCraft.Cards
{
    using Core;

    [CreateAssetMenu(fileName = "NewDaemon", menuName = "Dual Craft/Daemon Card")]
    public class DaemonCardData : CardData
    {
        [Header("Daemon Stats")]
        public Element element;
        public CreatureType creatureType;
        public int ashe;
        public int attack;
        public int asheCost;

        [Header("Evolution")]
        public DaemonCardData evolvesTo;
        public int evolutionCost;

        [Header("Ability")]
        public AbilityData ability;

        private void OnValidate()
        {
            category = CardCategory.Daemon;
        }
    }
}
