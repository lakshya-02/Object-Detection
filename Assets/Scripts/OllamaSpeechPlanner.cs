using System;
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace WorkspaceVoiceAssistant
{
    /// <summary>
    /// Handles the Ollama HTTP request/response cycle and emits structured recommendations.
    /// </summary>
    public sealed class OllamaSpeechPlanner : MonoBehaviour
    {
        private const string DefaultEndpoint = "http://localhost:11434/api/generate";
        private const string GeneratePath = "/api/generate";
        private const string DefaultModelName = "llama3.2";

        [Header("Ollama")]
        [Tooltip("Optional provider/profile asset. If assigned, host/model/prompt are read from it when possible.")]
        [SerializeField] private ScriptableObject providerAsset;
        [SerializeField] private string ollamaEndpoint = DefaultEndpoint;
        [SerializeField] private string modelName = DefaultModelName;
        [SerializeField, Min(1)] private int requestTimeoutSeconds = 45;

        [Header("Prompt Builder")]
        [SerializeField] private SceneContextBuilder sceneContextBuilder;

        [Header("Events")]
        [SerializeField] private RecommendationEvent onRecommendationReceived = new RecommendationEvent();
        [SerializeField] private StringEvent onRequestFailed = new StringEvent();
        [SerializeField] private StringEvent onRequestStarted = new StringEvent();

        private Coroutine activeRequest;

        public event Action<Recommendation> RecommendationReceived;
        public event Action<string> RequestFailed;
        public event Action<string> RequestStarted;

        public RecommendationEvent OnRecommendationReceived => onRecommendationReceived;
        public StringEvent OnRequestFailed => onRequestFailed;
        public StringEvent OnRequestStarted => onRequestStarted;

        private void Awake()
        {
            ResolveReferences();
        }

        public void RequestRecommendation(string sceneJson, string userTranscript)
        {
            ResolveReferences();
            ApplyProviderAssetSettings();

            if (sceneContextBuilder == null)
            {
                ReportFailure("SceneContextBuilder is not assigned.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(ollamaEndpoint))
            {
                ollamaEndpoint = ollamaEndpoint.Trim();
            }

            if (string.IsNullOrWhiteSpace(ollamaEndpoint))
            {
                ReportFailure("Ollama endpoint is empty.");
                return;
            }

            if (string.IsNullOrWhiteSpace(modelName))
            {
                ReportFailure("Ollama model name is empty.");
                return;
            }

            if (Application.platform == RuntimePlatform.Android
                && !Application.isEditor
                && ollamaEndpoint.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning(
                    "[OllamaSpeechPlanner] localhost on Quest points to the headset itself. " +
                    "If Ollama runs on your PC, override the endpoint with your LAN IP in the Inspector.",
                    this);
            }

            if (activeRequest != null)
            {
                StopCoroutine(activeRequest);
            }

            string prompt = sceneContextBuilder.BuildPrompt(sceneJson, userTranscript);
            onRequestStarted.Invoke("Sending request to Ollama...");
            RequestStarted?.Invoke("Sending request to Ollama...");
            activeRequest = StartCoroutine(SendGenerateRequest(prompt));
        }

        public void CancelActiveRequest()
        {
            if (activeRequest == null)
            {
                return;
            }

            StopCoroutine(activeRequest);
            activeRequest = null;
        }

        private IEnumerator SendGenerateRequest(string prompt)
        {
            OllamaGenerateRequest payload = new OllamaGenerateRequest(modelName, prompt);
            string requestJson = JsonUtility.ToJson(payload);

            using UnityWebRequest request = new UnityWebRequest(ollamaEndpoint, UnityWebRequest.kHttpVerbPOST);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(requestJson);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = requestTimeoutSeconds;
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();
            activeRequest = null;

            if (request.result != UnityWebRequest.Result.Success)
            {
                string networkError = SafeErrorText(request.error);

                if (IsTimeoutError(networkError))
                {
                    ReportFailure("Ollama request timed out after " + requestTimeoutSeconds + " seconds.");
                    yield break;
                }

                if (IsLikelyConnectionRefused(networkError))
                {
                    ReportFailure("Could not reach Ollama at " + ollamaEndpoint + ". Check that the Ollama server is running and reachable.");
                    yield break;
                }

                ReportFailure("Ollama request failed: " + networkError);
                yield break;
            }

            string rawEnvelope = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            if (string.IsNullOrWhiteSpace(rawEnvelope))
            {
                ReportFailure("Ollama returned an empty HTTP response.");
                yield break;
            }

            if (!TryParseJson(rawEnvelope, out OllamaGenerateResponse envelope, out string envelopeError))
            {
                ReportFailure("Could not parse Ollama response envelope: " + envelopeError);
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(envelope.error))
            {
                ReportFailure("Ollama returned an error: " + envelope.error);
                yield break;
            }

            if (string.IsNullOrWhiteSpace(envelope.response))
            {
                ReportFailure("Ollama returned an empty model response.");
                yield break;
            }

            string recommendationJson = ExtractJsonObject(envelope.response);
            if (!TryParseJson(recommendationJson, out Recommendation recommendation, out string recommendationError))
            {
                ReportFailure("Model output was not valid recommendation JSON: " + recommendationError);
                yield break;
            }

            if (!ValidateRecommendation(recommendation, out string validationError))
            {
                ReportFailure(validationError);
                yield break;
            }

            onRecommendationReceived.Invoke(recommendation);
            RecommendationReceived?.Invoke(recommendation);
        }

        private void ResolveReferences()
        {
            if (sceneContextBuilder == null)
            {
                sceneContextBuilder = GetComponent<SceneContextBuilder>();
            }

            if (sceneContextBuilder == null)
            {
                sceneContextBuilder = FindAnyObjectByType<SceneContextBuilder>();
            }
        }

        private void ApplyProviderAssetSettings()
        {
            if (providerAsset == null)
            {
                return;
            }

            string providerHost = ReadStringMember(providerAsset, "host", "baseUrl", "baseURL", "endpoint", "ollamaEndpoint");
            if (!string.IsNullOrWhiteSpace(providerHost))
            {
                ollamaEndpoint = BuildGenerateEndpoint(providerHost);
            }

            string providerModel = ReadStringMember(providerAsset, "model", "modelName");
            if (!string.IsNullOrWhiteSpace(providerModel))
            {
                modelName = providerModel.Trim();
            }
        }

        private static string BuildGenerateEndpoint(string providerHost)
        {
            string trimmed = providerHost.Trim().TrimEnd('/');
            if (trimmed.EndsWith(GeneratePath, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            return trimmed + GeneratePath;
        }

        private static string ReadStringMember(UnityEngine.Object source, params string[] memberNames)
        {
            Type type = source.GetType();
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            for (int i = 0; i < memberNames.Length; i++)
            {
                FieldInfo field = type.GetField(memberNames[i], Flags);
                if (field != null && field.FieldType == typeof(string))
                {
                    return field.GetValue(source) as string;
                }

                PropertyInfo property = type.GetProperty(memberNames[i], Flags);
                if (property != null && property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0)
                {
                    return property.GetValue(source, null) as string;
                }
            }

            return null;
        }

        private static bool TryParseJson<T>(string rawJson, out T parsedObject, out string errorMessage)
        {
            parsedObject = default;

            if (string.IsNullOrWhiteSpace(rawJson))
            {
                errorMessage = "response body was empty";
                return false;
            }

            try
            {
                parsedObject = JsonUtility.FromJson<T>(rawJson);
            }
            catch (ArgumentException exception)
            {
                errorMessage = exception.Message;
                return false;
            }

            if (parsedObject == null)
            {
                errorMessage = "JsonUtility returned null";
                return false;
            }

            errorMessage = null;
            return true;
        }

        private static string ExtractJsonObject(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return string.Empty;
            }

            string trimmed = rawText.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                int firstNewline = trimmed.IndexOf('\n');
                int lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (firstNewline >= 0 && lastFence > firstNewline)
                {
                    trimmed = trimmed.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
                }
            }

            int firstBrace = trimmed.IndexOf('{');
            int lastBrace = trimmed.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                return trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            return trimmed;
        }

        private static bool ValidateRecommendation(Recommendation recommendation, out string error)
        {
            if (recommendation == null)
            {
                error = "Recommendation payload was null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(recommendation.summary))
            {
                error = "Recommendation is missing the required summary field.";
                return false;
            }

            if (recommendation.steps == null || recommendation.steps.Count == 0)
            {
                error = "Recommendation is missing the required steps array.";
                return false;
            }

            for (int i = 0; i < recommendation.steps.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(recommendation.steps[i]))
                {
                    error = "Recommendation contains an empty step at index " + i + ".";
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(recommendation.speak))
            {
                error = "Recommendation is missing the required speak field.";
                return false;
            }

            error = null;
            return true;
        }

        private void ReportFailure(string message)
        {
            Debug.LogError("[OllamaSpeechPlanner] " + message, this);
            onRequestFailed.Invoke(message);
            RequestFailed?.Invoke(message);
        }

        private static bool IsTimeoutError(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return false;
            }

            string lower = error.ToLowerInvariant();
            return lower.Contains("timed out") || lower.Contains("timeout");
        }

        private static bool IsLikelyConnectionRefused(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return false;
            }

            string lower = error.ToLowerInvariant();
            return lower.Contains("connection refused")
                || lower.Contains("cannot connect")
                || lower.Contains("failed to connect")
                || lower.Contains("destination host")
                || lower.Contains("refused");
        }

        private static string SafeErrorText(string error)
        {
            return string.IsNullOrWhiteSpace(error) ? "No additional error details." : error;
        }
    }
}
