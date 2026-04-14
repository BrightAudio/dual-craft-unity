using UnityEngine;

namespace DualCraft.Cards
{
    using Core;

    [CreateAssetMenu(fileName = "NewSeal", menuName = "Dual Craft/Seal Card")]
    public class SealCardData : CardData
    {
        [Header("Seal Stats")]
        public SealTrigger trigger;
        public SealEffectType effectType;
        public int effectValue;

        private void OnValidate()
        {
            category = CardCategory.Seal;
        }
    }
}
