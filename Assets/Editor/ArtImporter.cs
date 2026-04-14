// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — AI Art Importer
//  Assigns generated PNG artwork to CardData SOs
// ═══════════════════════════════════════════════════════

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

namespace DualCraft.Editor
{
    using Cards;

    public static class ArtImporter
    {
        [MenuItem("Dual Craft/Import AI Card Art")]
        public static void ImportArt()
        {
            string artFolder = "Assets/Resources/CardArt";
            string cardFolder = "Assets/Resources/CardData/Cards";

            if (!Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), artFolder)))
            {
                Debug.LogError("[ArtImporter] CardArt folder not found!");
                return;
            }

            // Configure texture import settings for all PNGs
            var pngGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { artFolder });
            foreach (var guid in pngGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null && importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spritePixelsPerUnit = 100;
                    importer.maxTextureSize = 512;
                    importer.textureCompression = TextureImporterCompression.Compressed;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.SaveAndReimport();
                }
            }

            // Find all card assets and assign artwork
            var cardGuids = AssetDatabase.FindAssets("t:CardData", new[] { cardFolder });
            int assigned = 0;
            int missing = 0;

            foreach (var guid in cardGuids)
            {
                string cardPath = AssetDatabase.GUIDToAssetPath(guid);
                var card = AssetDatabase.LoadAssetAtPath<CardData>(cardPath);
                if (card == null) continue;

                // Look for matching art PNG
                string artPath = $"{artFolder}/{card.cardId}.png";
                
                // Also try jpg since Pollinations returns jpeg
                if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), artPath)))
                    artPath = $"{artFolder}/{card.cardId}.jpg";

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(artPath);

                if (sprite != null)
                {
                    if (card.artwork != sprite)
                    {
                        card.artwork = sprite;
                        EditorUtility.SetDirty(card);
                        assigned++;
                    }
                }
                else
                {
                    missing++;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[ArtImporter] Assigned {assigned} card artworks, {missing} still missing");
        }
    }
}
#endif
