using UnityEngine;

namespace DualCraft.Cards
{
    using Core;

    [CreateAssetMenu(fileName = "NewMask", menuName = "Dual Craft/Mask Card")]
    public class MaskCardData : CardData
    {
        [Header("Mask Stats")]
        public int duration;
        public MaskEffectType effectType;
        public int effectValue;

        private void OnValidate()
        {
            category = CardCategory.Mask;
        }
    }
}
