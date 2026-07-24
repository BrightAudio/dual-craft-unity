using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DualCraft.UI
{
    /// <summary>
    /// Makes desktop-resolution UI physically readable on compact Windows handhelds.
    /// The scaler remains resolution-aware, so anchors and board proportions are unchanged.
    /// </summary>
    public sealed class WindowsHandheldUIScale : MonoBehaviour
    {
        private const string PlayerPrefKey = "DualMon.UI.Scale";
        private const float DefaultHandheldScale = 1.16f;
        private const float MinimumScale = 1f;
        private const float MaximumScale = 1.30f;
        private const int HandheldHeightThreshold = 1200;

        private static WindowsHandheldUIScale _instance;
        private float _lastAppliedScale = -1f;
        private int _lastWidth = -1;
        private int _lastHeight = -1;

        public static float CurrentScale => ResolveScale();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null)
                return;

            var root = new GameObject(nameof(WindowsHandheldUIScale));
            DontDestroyOnLoad(root);
            _instance = root.AddComponent<WindowsHandheldUIScale>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            StartCoroutine(ApplyAfterDynamicUiBuild());
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Update()
        {
            float scale = ResolveScale();
            if (_lastWidth == Screen.width && _lastHeight == Screen.height
                && Mathf.Approximately(_lastAppliedScale, scale))
            {
                return;
            }

            ApplyScaleToAllCanvases(scale);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            StartCoroutine(ApplyAfterDynamicUiBuild());
        }

        private IEnumerator ApplyAfterDynamicUiBuild()
        {
            // Some screens create their canvas in Start, so apply once immediately and
            // again after layout construction has completed.
            ApplyScaleToAllCanvases(ResolveScale());
            yield return null;
            yield return new WaitForEndOfFrame();
            ApplyScaleToAllCanvases(ResolveScale());
        }

        private static float ResolveScale()
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer || Screen.height > HandheldHeightThreshold)
                return 1f;

            return Mathf.Clamp(PlayerPrefs.GetFloat(PlayerPrefKey, DefaultHandheldScale), MinimumScale, MaximumScale);
        }

        private void ApplyScaleToAllCanvases(float scale)
        {
            var scalers = FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include);
            foreach (var scaler in scalers)
            {
                if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
                    continue;

                scaler.referenceResolution = new Vector2(1920f / scale, 1080f / scale);
                scaler.matchWidthOrHeight = 0.5f;
            }

            _lastAppliedScale = scale;
            _lastWidth = Screen.width;
            _lastHeight = Screen.height;
        }
    }
}
