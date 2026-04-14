using UnityEngine;

namespace DualCraft.Cards
{
    using Core;

    [CreateAssetMenu(fileName = "NewConjuror", menuName = "Dual Craft/Conjuror Card")]
    public class ConjurorCardData : CardData
    {
        [Header("Conjuror Stats")]
        public Element element;
        public int loyalty;
        public ConjurorAbilityData[] abilities;

        private void OnValidate()
        {
            category = CardCategory.Conjuror;
        }
    }
}
