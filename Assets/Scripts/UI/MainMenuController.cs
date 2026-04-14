// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Main Menu Controller
// ═══════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace DualCraft.UI
{
    public class MainMenuController : MonoBehaviour
    {
        [Header("Buttons")]
        [SerializeField] private Button playButton;
        [SerializeField] private Button aiDuelButton;
        [SerializeField] private Button collectionButton;
        [SerializeField] private Button deckBuilderButton;
        [SerializeField] private Button shopButton;
        [SerializeField] private Button storyButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button openPacksButton;

        [Header("Title")]
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI taglineText;

        private void Start()
        {
            if (titleText) titleText.text = "DUAL CRAFT";
            if (taglineText) taglineText.text = Core.GameConstants.GameTagline;

            playButton?.onClick.AddListener(() => LoadScene("OnlineLobby"));
            aiDuelButton?.onClick.AddListener(() => LoadScene("Battle"));
            collectionButton?.onClick.AddListener(() => LoadScene("Collection"));
            deckBuilderButton?.onClick.AddListener(() => LoadScene("DeckBuilder"));
            shopButton?.onClick.AddListener(() => LoadScene("Shop"));
            storyButton?.onClick.AddListener(() => LoadScene("Story"));
            openPacksButton?.onClick.AddListener(() => LoadScene("PackOpening"));
        }

        private void LoadScene(string sceneName)
        {
            SceneManager.LoadScene(sceneName);
        }
    }
}
