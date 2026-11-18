using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Serialization;

namespace WorkspaceVoiceAssistant
{
    /// <summary>
    /// Sends grounded voice questions through the PC gateway and emits recommendations.
    /// </summary>
    public sealed class OllamaSpeechPlanner : MonoBehaviour
    {
        private const string DefaultEndpoint = "http://localhost:8765/api/answer";
        private const string DefaultModelName = "gemini-3.5-flash-lite";

        [Header("AI Gateway")]
        [SerializeField] private string providerName = "google";
        [FormerlySerializedAs("ollamaEndpoint")]
        [SerializeField] private string gatewayEndpoint = DefaultEndpoint;
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

            if (sceneContextBuilder == null)
            {
                ReportFailure("SceneContextBuilder is not assigned.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(gatewayEndpoint))
            {
                gatewayEndpoint = gatewayEndpoint.Trim();
            }

            if (string.IsNullOrWhiteSpace(gatewayEndpoint))
            {
                ReportFailure("AI gateway endpoint is empty.");
                return;
            }

            if (string.IsNullOrWhiteSpace(modelName))
            {
                ReportFailure("AI model name is empty.");
                return;
            }

            if (providerName != "google" && providerName != "openrouter" && providerName != "grok")
            {
                ReportFailure("AI provider must be google, openrouter, or grok.");
                return;
            }

            if (Application.platform == RuntimePlatform.Android
                && !Application.isEditor
                && gatewayEndpoint.Contains("localhost", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning(
                    "[OllamaSpeechPlanner] localhost on Quest points to the headset. Set the gateway endpoint to your PC LAN IP.",
                    this);
            }

            if (activeRequest != null)
            {
                StopCoroutine(activeRequest);
            }

            if (!TryParseJson(sceneJson, out SceneData scene, out _) || scene.objects == null || scene.objects.Count == 0)
            {
                ReportFailure("No current detections are available for AI advice.");
                return;
            }

            string prompt = sceneContextBuilder.BuildPrompt(sceneJson, userTranscript);
            onRequestStarted.Invoke("Sending request to AI gateway...");
            RequestStarted?.Invoke("Sending request to AI gateway...");
            activeRequest = StartCoroutine(SendGenerateRequest(prompt, scene));
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

        private IEnumerator SendGenerateRequest(string prompt, SceneData scene)
        {
            AiGatewayRequest payload = new AiGatewayRequest(providerName, modelName, prompt);
            string requestJson = JsonUtility.ToJson(payload);

            using UnityWebRequest request = new UnityWebRequest(gatewayEndpoint, UnityWebRequest.kHttpVerbPOST);
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
                    ReportFailure("AI request timed out after " + requestTimeoutSeconds + " seconds.");
                    yield break;
                }

                if (IsLikelyConnectionRefused(networkError))
                {
                    ReportFailure("Could not reach AI gateway at " + gatewayEndpoint + ". Check the PC service and network.");
                    yield break;
                }

                ReportFailure("AI request failed: " + networkError);
                yield break;
            }

            string rawEnvelope = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            if (string.IsNullOrWhiteSpace(rawEnvelope))
            {
                ReportFailure("AI gateway returned an empty HTTP response.");
                yield break;
            }

            if (!TryParseJson(rawEnvelope, out OllamaGenerateResponse envelope, out string envelopeError))
            {
                ReportFailure("Could not parse AI gateway response: " + envelopeError);
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(envelope.error))
            {
                ReportFailure("AI gateway returned an error: " + envelope.error);
                yield break;
            }

            if (string.IsNullOrWhiteSpace(envelope.response))
            {
                ReportFailure("AI gateway returned an empty model response.");
                yield break;
            }

            string recommendationJson = ExtractJsonObject(envelope.response);
            if (!TryParseJson(recommendationJson, out AiPlanResponse plan, out string recommendationError))
            {
                ReportFailure("Model output was not valid action JSON: " + recommendationError);
                yield break;
            }

            if (!sceneContextBuilder.TryBuildAdvice(scene, plan, out Recommendation recommendation))
            {
                Debug.LogWarning("[OllamaSpeechPlanner] AI returned no supported placement action.", this);
                recommendation = sceneContextBuilder.BuildUnavailableAdvice();
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
