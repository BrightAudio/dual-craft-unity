// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Game Constants
// ═══════════════════════════════════════════════════════

namespace DualCraft.Core
{
    public static class GameConstants
    {
        // Game Rules
        public const int ConjurorMaxHp = 30;
        public const int StartingHandSize = 5;
        public const int MaxHandSize = 10;
        public const int MaxFieldDaemons = 5;
        public const int MaxSeals = 3;
        public const int DeckSize = 40;
        public const int PillarCount = 4;
        public const int CardsDrawnPerTurn = 1;

        // Will System
        public const int StartingWill = 1;
        public const int MaxWill = 10;

        // Combat Modifiers
        public const float SuperEffectiveMult = 1.5f;
        public const float WeakMult = 0.75f;
        public const float NeutralMult = 1.0f;
        public const float CreatureAdvantageMult = 1.25f;
        public const float CreatureDisadvantageMult = 0.85f;

        // Max copies per card in a deck
        public const int MaxCardCopies = 3;

        // Branding
        public const string GameSlogan = "Craft Your Legacy.";
        public const string GameTagline = "Summon. Conquer. Craft Your Legacy.";
    }
}
