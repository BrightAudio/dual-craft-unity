using UnityEngine;

namespace DualCraft.Cards
{
    using Core;

    [CreateAssetMenu(fileName = "NewPillar", menuName = "Dual Craft/Pillar Card")]
    public class PillarCardData : CardData
    {
        [Header("Pillar Stats")]
        public Element element;
        public CreatureType creatureType;
        public int hp;
        public int loyalty;

        [Header("Abilities")]
        [TextArea] public string passiveAbility;
        public PillarPassiveData passiveEffect;
        public PillarDestroyData onDestroyedEffect;
        public ActivatedAbilityData[] activatedAbilities;

        private void OnValidate()
        {
            category = CardCategory.Pillar;
        }
    }
}
