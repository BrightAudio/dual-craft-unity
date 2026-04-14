using UnityEngine;

namespace DualCraft.Cards
{
    using Core;

    [CreateAssetMenu(fileName = "NewDomain", menuName = "Dual Craft/Domain Card")]
    public class DomainCardData : CardData
    {
        [Header("Domain Effect")]
        public DomainEffectType effectType;
        public int effectValue;
        public Element effectElement;

        private void OnValidate()
        {
            category = CardCategory.Domain;
        }
    }
}
