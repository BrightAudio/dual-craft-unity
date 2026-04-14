// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Player Profile / Save Data
//  Persistent player data (collection, decks, currency)
// ═══════════════════════════════════════════════════════

using UnityEngine;
using System.Collections.Generic;
using System;

namespace DualCraft.Data
{
    using Core;

    [Serializable]
    public class PlayerProfile
    {
        public string playerName = "Conjuror";
        public int glint;  // Common currency
        public int embers; // Premium currency
        public int rank;
        public int xp;

        public List<string> ownedCardIds = new();
        public List<SavedDeck> customDecks = new();
        public ChallengeProgress challengeProgress = new();
        public List<string> ownedCosmetics = new();
        public string activeSleeve = "default";
        public string activeBoard = "default";

        // Statistics
        public int totalWins;
        public int totalLosses;
        public int totalGamesPlayed;
    }

    [Serializable]
    public class SavedDeck
    {
        public string id;
        public string name;
        public Element element;
        public List<string> cardIds = new();
        public List<string> pillarIds = new();
    }

    [Serializable]
    public class ChallengeProgress
    {
        public int wins;
        public int gamesPlayed;
        public int daemonsSummoned;
        public int pillarsDestroyed;
        public int damageDealt;
        public int cardsDrawn;
    }

    public static class ProfileManager
    {
        private const string ProfileKey = "DualCraft_Profile";

        public static PlayerProfile Load()
        {
            string json = PlayerPrefs.GetString(ProfileKey, "");
            if (string.IsNullOrEmpty(json))
                return new PlayerProfile { glint = 100 };
            return JsonUtility.FromJson<PlayerProfile>(json);
        }

        public static void Save(PlayerProfile profile)
        {
            string json = JsonUtility.ToJson(profile);
            PlayerPrefs.SetString(ProfileKey, json);
            PlayerPrefs.Save();
        }

        public static void SaveDeck(PlayerProfile profile, SavedDeck deck)
        {
            int idx = profile.customDecks.FindIndex(d => d.id == deck.id);
            if (idx >= 0)
                profile.customDecks[idx] = deck;
            else
                profile.customDecks.Add(deck);
            Save(profile);
        }

        public static void DeleteDeck(PlayerProfile profile, string deckId)
        {
            profile.customDecks.RemoveAll(d => d.id == deckId);
            Save(profile);
        }
    }
}
