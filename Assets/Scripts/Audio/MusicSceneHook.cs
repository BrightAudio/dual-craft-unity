// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Music Scene Hook
//  Attach to any scene to trigger the right music mode.
//  MusicManager persists via DontDestroyOnLoad.
// ═══════════════════════════════════════════════════════

using UnityEngine;
using UnityEngine.SceneManagement;

namespace DualCraft.Audio
{
    public class MusicSceneHook : MonoBehaviour
    {
        [SerializeField] private MusicManager.MusicMode musicMode = MusicManager.MusicMode.Menu;

        private void Start()
        {
            if (MusicManager.Instance == null)
            {
                var go = new GameObject("MusicManager");
                go.AddComponent<MusicManager>();
            }

            MusicManager.Instance?.PlayMode(musicMode);
        }
    }
}
