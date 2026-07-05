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
        public string playerName = "Invoker";
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
        public string activeInvokerDesign = "arcane";
        public string preferredAIDifficulty = "Normal";

        [Header("Story Progress")]
        public bool storyIntroComplete;
        public int storyChapter;
        public string storyStarterCardId;
        public string storyStarterDeckName;
        public string storyStarterSavedDeckId;
        public string storyAreaId;
        public int storyPlayerX;
        public int storyPlayerY;
        public bool storyHallIntroComplete;
        public bool storyTrialBriefingComplete;
        public bool storyTrialComplete;
        public bool storyFirstInvokerReported;
        public List<string> caughtWildCardIds = new();
        public string storyWildInvokerCardId;
        public List<string> defeatedInvokerIds = new();

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
                return EnsureDefaults(new PlayerProfile { glint = 100 });

            var profile = JsonUtility.FromJson<PlayerProfile>(json);
            return EnsureDefaults(profile);
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

        private static PlayerProfile EnsureDefaults(PlayerProfile profile)
        {
            profile ??= new PlayerProfile { glint = 100 };
            profile.ownedCardIds ??= new List<string>();
            profile.customDecks ??= new List<SavedDeck>();
            profile.challengeProgress ??= new ChallengeProgress();
            profile.ownedCosmetics ??= new List<string>();
            profile.caughtWildCardIds ??= new List<string>();
            profile.defeatedInvokerIds ??= new List<string>();
            profile.storyAreaId ??= string.Empty;

            if (string.IsNullOrWhiteSpace(profile.playerName))
                profile.playerName = "Invoker";
            if (string.IsNullOrWhiteSpace(profile.activeSleeve))
                profile.activeSleeve = "default";
            if (string.IsNullOrWhiteSpace(profile.activeBoard))
                profile.activeBoard = "default";
            if (string.IsNullOrWhiteSpace(profile.activeInvokerDesign))
                profile.activeInvokerDesign = "arcane";
            if (string.IsNullOrWhiteSpace(profile.preferredAIDifficulty))
                profile.preferredAIDifficulty = "Normal";

            if (profile.storyChapter >= 2)
                profile.storyTrialComplete = true;
            if (profile.defeatedInvokerIds.Count > 0)
                profile.storyChapter = Math.Max(profile.storyChapter, 3);
            if (profile.storyFirstInvokerReported)
                profile.storyChapter = Math.Max(profile.storyChapter, 4);

            return profile;
        }
    }
}
