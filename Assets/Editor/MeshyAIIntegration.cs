// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Meshy AI Integration (Editor Only)
//  Provides Unity Editor menu items to generate card art
//  via the Meshy AI Text-to-Image API. Set your API key
//  in the MESHY_API_KEY environment variable or via the
//  Editor Preferences window.
//
//  Usage: Tools → Meshy AI → Generate Card Art
//  Or run  scripts/meshy_generate_art.py  from terminal
// ═══════════════════════════════════════════════════════
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using System.Collections;
using System.IO;

namespace DualCraft.Editor
{
    public class MeshyAIIntegration : EditorWindow
    {
        private string _apiKey = "";
        private string _customPrompt = "";
        private string _outputCardId = "custom_card";
        private string _status = "Ready";
        private bool _generating;

        private const string ApiBase = "https://api.meshy.ai/openapi/v1";
        private const string PrefKeyApiKey = "DualCraft.MeshyApiKey";

        [MenuItem("Tools/Meshy AI/Card Art Generator")]
        public static void ShowWindow()
        {
            GetWindow<MeshyAIIntegration>("Meshy AI").Show();
        }

        [MenuItem("Tools/Meshy AI/Generate All Missing Art (Terminal)")]
        public static void GenerateAllViaTerminal()
        {
            string key = EditorPrefs.GetString(PrefKeyApiKey, "");
            if (string.IsNullOrEmpty(key))
            {
                EditorUtility.DisplayDialog("Meshy AI",
                    "Set your Meshy API key first in Tools → Meshy AI → Card Art Generator",
                    "OK");
                return;
            }

            string scriptPath = Path.Combine(Application.dataPath, "..", "scripts", "meshy_generate_art.py");
            if (!File.Exists(scriptPath))
            {
                EditorUtility.DisplayDialog("Meshy AI",
                    "scripts/meshy_generate_art.py not found!", "OK");
                return;
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "python3",
                Arguments = $"\"{scriptPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.EnvironmentVariables["MESHY_API_KEY"] = key;
            System.Diagnostics.Process.Start(psi);
            Debug.Log("[Meshy AI] Started background art generation. Check terminal for progress.");
        }

        private void OnEnable()
        {
            _apiKey = EditorPrefs.GetString(PrefKeyApiKey, "");
        }

        private void OnGUI()
        {
            GUILayout.Label("Meshy AI — Card Art Generator", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // API Key
            EditorGUILayout.LabelField("API Key", EditorStyles.miniLabel);
            string newKey = EditorGUILayout.PasswordField(_apiKey);
            if (newKey != _apiKey)
            {
                _apiKey = newKey;
                EditorPrefs.SetString(PrefKeyApiKey, _apiKey);
            }

            if (string.IsNullOrEmpty(_apiKey))
            {
                EditorGUILayout.HelpBox(
                    "Get your API key at https://www.meshy.ai/\n" +
                    "Free tier gives you credits to generate images.",
                    MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Custom Card Art", EditorStyles.boldLabel);

            _outputCardId = EditorGUILayout.TextField("Card ID (filename)", _outputCardId);
            EditorGUILayout.LabelField("Art Prompt");
            _customPrompt = EditorGUILayout.TextArea(_customPrompt, GUILayout.Height(60));

            GUI.enabled = !_generating && !string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_customPrompt);
            if (GUILayout.Button("Generate Card Art"))
            {
                _generating = true;
                _status = "Submitting...";
                GenerateCustomArt();
            }
            GUI.enabled = true;

            EditorGUILayout.Space();
            GUI.enabled = !_generating && !string.IsNullOrEmpty(_apiKey);
            if (GUILayout.Button("Generate All Missing Art (Background)"))
            {
                GenerateAllViaTerminal();
            }
            GUI.enabled = true;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status", _status);
        }

        private async void GenerateCustomArt()
        {
            try
            {
                string json = JsonUtility.ToJson(new TextToImageRequest
                {
                    ai_model = "nano-banana",
                    prompt = _customPrompt,
                    aspect_ratio = "3:4"
                });

                using var createReq = new UnityWebRequest($"{ApiBase}/text-to-image", "POST");
                createReq.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
                createReq.downloadHandler = new DownloadHandlerBuffer();
                createReq.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                createReq.SetRequestHeader("Content-Type", "application/json");
                var op = createReq.SendWebRequest();
                while (!op.isDone) await System.Threading.Tasks.Task.Delay(100);

                if (createReq.result != UnityWebRequest.Result.Success)
                {
                    _status = $"Error: {createReq.error}";
                    _generating = false;
                    return;
                }

                var createResp = JsonUtility.FromJson<CreateTaskResponse>(createReq.downloadHandler.text);
                string taskId = createResp.result;
                _status = $"Task created: {taskId}. Waiting...";

                // Poll for completion
                for (int i = 0; i < 60; i++)
                {
                    await System.Threading.Tasks.Task.Delay(5000);

                    using var pollReq = UnityWebRequest.Get($"{ApiBase}/text-to-image/{taskId}");
                    pollReq.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                    var pollOp = pollReq.SendWebRequest();
                    while (!pollOp.isDone) await System.Threading.Tasks.Task.Delay(100);

                    if (pollReq.result != UnityWebRequest.Result.Success) continue;

                    var task = JsonUtility.FromJson<TaskStatusResponse>(pollReq.downloadHandler.text);
                    _status = $"{task.status} ({task.progress}%)";

                    if (task.status == "SUCCEEDED" && task.image_urls != null && task.image_urls.Length > 0)
                    {
                        // Download image
                        using var dlReq = UnityWebRequest.Get(task.image_urls[0]);
                        var dlOp = dlReq.SendWebRequest();
                        while (!dlOp.isDone) await System.Threading.Tasks.Task.Delay(100);

                        if (dlReq.result == UnityWebRequest.Result.Success)
                        {
                            string dir = Path.Combine(Application.dataPath, "Resources", "CardArt");
                            Directory.CreateDirectory(dir);
                            string outPath = Path.Combine(dir, $"{_outputCardId}.png");
                            File.WriteAllBytes(outPath, dlReq.downloadHandler.data);
                            AssetDatabase.Refresh();
                            _status = $"Saved to CardArt/{_outputCardId}.png!";
                            Debug.Log($"[Meshy AI] Card art saved: {outPath}");
                        }
                        break;
                    }
                    else if (task.status == "FAILED")
                    {
                        _status = "FAILED";
                        break;
                    }
                    Repaint();
                }
            }
            catch (System.Exception ex)
            {
                _status = $"Error: {ex.Message}";
                Debug.LogError($"[Meshy AI] {ex}");
            }
            finally
            {
                _generating = false;
                Repaint();
            }
        }

        [System.Serializable]
        private class TextToImageRequest
        {
            public string ai_model;
            public string prompt;
            public string aspect_ratio;
        }

        [System.Serializable]
        private class CreateTaskResponse
        {
            public string result;
        }

        [System.Serializable]
        private class TaskStatusResponse
        {
            public string status;
            public int progress;
            public string[] image_urls;
        }
    }
}
#endif
