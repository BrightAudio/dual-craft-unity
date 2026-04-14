using UnityEngine;

namespace DualCraft.Cards
{
    using Core;

    [CreateAssetMenu(fileName = "NewDispel", menuName = "Dual Craft/Dispel Card")]
    public class DispelCardData : CardData
    {
        [Header("Dispel Stats")]
        public DispelTarget target;
        public DispelCounterEffectData counterEffect;

        private void OnValidate()
        {
            category = CardCategory.Dispel;
        }
    }
}
