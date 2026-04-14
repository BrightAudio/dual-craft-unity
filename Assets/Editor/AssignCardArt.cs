// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Assign AI-Generated Card Art to SOs
//  Finds PNG files in Resources/CardArt/ matching card IDs,
//  sets their TextureImporter to Sprite, then assigns them
//  to the corresponding CardData ScriptableObjects.
// ═══════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

namespace DualCraft.Editor
{
    using Cards;

    public static class AssignCardArt
    {
        [MenuItem("Dual Craft/Assign Card Art")]
        public static void Run()
        {
            Debug.Log("[AssignCardArt] Starting art assignment...");

            string artDir = "Assets/Resources/CardArt";
            if (!Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), artDir)))
            {
                Debug.LogError("[AssignCardArt] CardArt directory not found");
                return;
            }

            // First pass: ensure all PNGs are imported as Sprites
            string[] pngs = Directory.GetFiles(Path.Combine(Directory.GetCurrentDirectory(), artDir), "*.png");
            int spriteCount = 0;
            foreach (var fullPath in pngs)
            {
                string assetPath = "Assets/Resources/CardArt/" + Path.GetFileName(fullPath);
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer != null && importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.maxTextureSize = 512;
                    importer.textureCompression = TextureImporterCompression.Compressed;
                    importer.SaveAndReimport();
                    spriteCount++;
                }
            }
            if (spriteCount > 0)
            {
                Debug.Log($"[AssignCardArt] Converted {spriteCount} textures to Sprites");
                AssetDatabase.Refresh();
            }

            // Load CardDatabase
            var cardDb = AssetDatabase.LoadAssetAtPath<CardDatabase>("Assets/Resources/CardData/CardDatabase.asset");
            if (cardDb == null)
            {
                Debug.LogError("[AssignCardArt] CardDatabase not found");
                return;
            }

            int assigned = 0;
            assigned += AssignArtToArray(cardDb.daemons, artDir);
            assigned += AssignArtToArray(cardDb.pillars, artDir);
            assigned += AssignArtToArray(cardDb.domains, artDir);
            assigned += AssignArtToArray(cardDb.masks, artDir);
            assigned += AssignArtToArray(cardDb.seals, artDir);
            assigned += AssignArtToArray(cardDb.dispels, artDir);
            assigned += AssignArtToArray(cardDb.conjurors, artDir);

            AssetDatabase.SaveAssets();
            Debug.Log($"[AssignCardArt] Assigned art to {assigned} cards");
        }

        static int AssignArtToArray<T>(T[] cards, string artDir) where T : CardData
        {
            if (cards == null) return 0;
            int count = 0;
            foreach (var card in cards)
            {
                if (card == null) continue;
                string spritePath = $"{artDir}/{card.cardId}.png";
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                if (sprite != null)
                {
                    card.artwork = sprite;
                    EditorUtility.SetDirty(card);
                    count++;
                }
            }
            return count;
        }
    }
}
#endif
