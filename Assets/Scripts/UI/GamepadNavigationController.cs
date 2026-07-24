using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DualCraft.UI
{
    /// <summary>
    /// Keeps controller focus usable across scene-built and runtime-built DualMon UI.
    /// Unity's InputSystemUIInputModule owns movement and submit; this component only
    /// restores lost focus and supplies a consistent B/back fallback.
    /// </summary>
    public sealed class GamepadNavigationController : MonoBehaviour
    {
        private static GamepadNavigationController _instance;
        private float _nextFocusRecoveryTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null)
                return;

            var root = new GameObject(nameof(GamepadNavigationController));
            DontDestroyOnLoad(root);
            _instance = root.AddComponent<GamepadNavigationController>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _nextFocusRecoveryTime = Time.unscaledTime + 0.25f;
        }

        private void Update()
        {
            if (!HasGamepadActivity(out bool cancelPressed))
                return;

            if (cancelPressed && TryInvokeBackFallback())
                return;

            if (Time.unscaledTime < _nextFocusRecoveryTime)
                return;

            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var selectable = selected != null ? selected.GetComponentInParent<Selectable>() : null;
            if (selectable == null || !selectable.IsActive() || !selectable.IsInteractable())
                SelectBestAvailableControl();
        }

        private static bool HasGamepadActivity(out bool cancelPressed)
        {
            cancelPressed = false;
#if ENABLE_INPUT_SYSTEM
            var gamepad = Gamepad.current;
            if (gamepad == null)
                return false;

            cancelPressed = gamepad.buttonEast.wasPressedThisFrame;
            return cancelPressed
                || gamepad.buttonSouth.wasPressedThisFrame
                || gamepad.buttonNorth.wasPressedThisFrame
                || gamepad.buttonWest.wasPressedThisFrame
                || gamepad.startButton.wasPressedThisFrame
                || gamepad.selectButton.wasPressedThisFrame
                || gamepad.leftShoulder.wasPressedThisFrame
                || gamepad.rightShoulder.wasPressedThisFrame
                || gamepad.dpad.ReadValue().sqrMagnitude > 0.1f
                || gamepad.leftStick.ReadValue().sqrMagnitude > 0.25f;
#else
            cancelPressed = Input.GetKeyDown(KeyCode.JoystickButton1);
            return cancelPressed
                || Input.GetKeyDown(KeyCode.JoystickButton0)
                || Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.5f
                || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.5f;
#endif
        }

        private static void SelectBestAvailableControl()
        {
            if (EventSystem.current == null)
                return;

            string scene = SceneManager.GetActiveScene().name;
            var candidates = Selectable.allSelectablesArray
                .Where(IsControllerSelectable)
                .OrderByDescending(selectable => ScoreForScene(selectable, scene))
                .ThenBy(selectable => selectable.transform.GetSiblingIndex())
                .ToArray();

            if (candidates.Length > 0)
                EventSystem.current.SetSelectedGameObject(candidates[0].gameObject);
        }

        private static bool IsControllerSelectable(Selectable selectable)
        {
            if (selectable == null || !selectable.IsActive() || !selectable.IsInteractable())
                return false;
            if (!selectable.gameObject.activeInHierarchy || selectable.navigation.mode == Navigation.Mode.None)
                return false;

            return selectable is Button
                || selectable is InputField
                || selectable is TMP_InputField
                || selectable is Slider
                || selectable is Toggle;
        }

        private static int ScoreForScene(Selectable selectable, string scene)
        {
            string name = selectable.gameObject.name ?? string.Empty;
            int score = 0;
            if (name.Contains("Forfeit", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Delete", StringComparison.OrdinalIgnoreCase))
                score -= 500;
            if (name.Contains("Back", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Cancel", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Close", StringComparison.OrdinalIgnoreCase))
                score -= 80;

            if (scene == "Battle" && name.Contains("HandCard", StringComparison.OrdinalIgnoreCase))
                score += 400;
            else if (scene == "MainMenu" && (name.Contains("Battle", StringComparison.OrdinalIgnoreCase)
                || name.Contains("AI", StringComparison.OrdinalIgnoreCase)))
                score += 300;
            else if (scene == "Multiplayer" && name.Contains("Host", StringComparison.OrdinalIgnoreCase))
                score += 300;
            else if (scene == "PackOpening" && name.Contains("Pack", StringComparison.OrdinalIgnoreCase))
                score += 250;
            else if (scene == "DeckBuilder" && name.Contains("Card", StringComparison.OrdinalIgnoreCase))
                score += 220;

            return score;
        }

        private static bool TryInvokeBackFallback()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (scene == "Battle" || scene == "Multiplayer")
                return false;

            var backButton = Selectable.allSelectablesArray
                .OfType<Button>()
                .Where(button => button != null && button.IsActive() && button.IsInteractable())
                .FirstOrDefault(button =>
                    button.gameObject.name.Contains("Back", StringComparison.OrdinalIgnoreCase)
                    || button.gameObject.name.Contains("Cancel", StringComparison.OrdinalIgnoreCase)
                    || button.gameObject.name.Contains("Close", StringComparison.OrdinalIgnoreCase));
            if (backButton == null)
                return false;

            backButton.onClick.Invoke();
            return true;
        }
    }
}
