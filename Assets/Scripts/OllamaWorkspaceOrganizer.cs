using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Meta.XR.BuildingBlocks.AIBlocks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Sends object-detection scene data to a locally running Ollama model and
/// returns workspace organization instructions that another system can speak.
/// </summary>
public sealed class OllamaWorkspaceOrganizer : MonoBehaviour
{
    private const string DefaultEndpoint = "http://localhost:11434/api/generate";
    private const string GeneratePath = "/api/generate";
    private const string DefaultModelName = "llama3.2";
    private const string SceneJsonPlaceholder = "{SCENE_JSON_HERE}";
    private const string GoalPlaceholder = "{USER_GOAL_HERE}";
    private const string DefaultPromptTemplate =
        "You are a precise mixed-reality workspace organization assistant.\n\n" +
        "You will receive detected scene data in JSON. Use only object names from objects[].name.\n" +
        "Do not invent missing objects, furniture, drawers, shelves, storage boxes, or tools.\n" +
        "Use each object's box, horizontalPosition, verticalPosition, and zone to create concrete placement instructions.\n" +
        "Prefer relative instructions such as: place laptop left of keyboard, move bottle to the right edge, stack book on notebook, align mouse beside keyboard.\n" +
        "Only use a relative instruction when both named objects are detected. Otherwise use a detected zone like left, center, right, top, or bottom.\n" +
        "If drinks are detected, place them away from electronics such as laptop, keyboard, monitor, phone, or mouse.\n" +
        "If papers, notebooks, or books are detected, stack or align them together.\n" +
        "If the user goal mentions cleaning or clutter, reduce clutter by grouping similar detected objects and clearing the center work area.\n" +
        "Return 3 to 5 practical steps. Each step should name the object and the exact target location.\n" +
        "The speak field should be 2 to 4 natural sentences with the same specific object placement advice.\n" +
        "If no objects are detected, say that no detected objects are available yet and ask the user to look at the workspace.\n\n" +
        "User goal:\n" +
        GoalPlaceholder + "\n\n" +
        "Scene JSON:\n" +
        SceneJsonPlaceholder + "\n\n" +
        "Return valid JSON only, exactly in this shape:\n" +
        "{\n" +
        "  \"summary\": \"specific organization plan\",\n" +
        "  \"steps\": [\"specific object placement step 1\", \"specific object placement step 2\", \"specific object placement step 3\"],\n" +
        "  \"speak\": \"natural spoken guidance with specific object placements\"\n" +
        "}";

    [Header("Ollama")]
    [Tooltip("Optional provider/profile asset. If assigned, this component tries to read host, model, and prompt-template fields from it.")]
    [SerializeField] private ScriptableObject providerAsset;
    [SerializeField] private string ollamaEndpoint = DefaultEndpoint;
    [SerializeField] private string modelName = DefaultModelName;
    [SerializeField, Min(1)] private int requestTimeoutSeconds = 45;

    [Header("Prompt")]
    [TextArea(12, 24)]
    [SerializeField] private string promptTemplate = DefaultPromptTemplate;
    [Tooltip("Optional goal inserted only when the single-argument request method is used. Leave empty to use the prompt template as-is.")]
    [SerializeField] private string defaultUserGoal;

    [Header("Events")]
    [SerializeField] private WorkspaceRecommendationEvent onRecommendationReceived = new WorkspaceRecommendationEvent();
    [SerializeField] private StringEvent onRequestFailed = new StringEvent();

    [Header("Build Test Trigger")]
    [Tooltip("When enabled, pressing Quest right-controller A or keyboard A sends an Ollama request.")]
    [SerializeField] private bool requestTestOnAButton = true;
    [SerializeField, Min(0.1f)] private float buttonRequestCooldownSeconds = 1f;

    [Header("Object Detection")]
    [Tooltip("Meta object-detection agent. If empty, this component tries to find one in the scene at runtime.")]
    [SerializeField] private ObjectDetectionAgent objectDetectionAgent;
    [SerializeField, Min(0.5f)] private float detectionWaitTimeoutSeconds = 4f;

    private Coroutine activeRequest;
    private Coroutine activeDetectionRequest;
    private readonly List<UnityEngine.XR.InputDevice> rightHandDevices = new List<UnityEngine.XR.InputDevice>();
    private bool wasAButtonPressed;
    private float nextAllowedButtonRequestTime;

    /// <summary>
    /// Invoked when Ollama returns a valid recommendation JSON object.
    /// </summary>
    public WorkspaceRecommendationEvent OnRecommendationReceived => onRecommendationReceived;

    /// <summary>
    /// Invoked when the request, response envelope, or recommendation JSON is invalid.
    /// </summary>
    public StringEvent OnRequestFailed => onRequestFailed;

    /// <summary>
    /// Optional C# callback for code-driven integrations.
    /// </summary>
    public event Action<WorkspaceArrangementRecommendation> RecommendationReceived;

    /// <summary>
    /// Optional C# callback for code-driven error handling.
    /// </summary>
    public event Action<string> RequestFailed;

    /// <summary>
    /// Optional C# callback for showing immediate request status in UI.
    /// </summary>
    public event Action<string> RequestStarted;

    private void Update()
    {
        if (!requestTestOnAButton || !WasTestButtonPressedThisFrame())
        {
            return;
        }

        if (Time.unscaledTime < nextAllowedButtonRequestTime)
        {
            return;
        }

        nextAllowedButtonRequestTime = Time.unscaledTime + buttonRequestCooldownSeconds;
        Debug.Log("[OllamaWorkspaceOrganizer] A button pressed. Requesting object detection before Ollama.", this);
        RequestArrangementFromDetectedObjects();
    }

    /// <summary>
    /// Sends scene JSON using the Inspector-configured default user goal.
    /// </summary>
    public void RequestArrangementRecommendation(string sceneJson)
    {
        RequestArrangementRecommendation(sceneJson, defaultUserGoal);
    }

    /// <summary>
    /// Runs object detection, converts the detected labels to JSON, and sends them to Ollama.
    /// </summary>
    [ContextMenu("Detect Objects Then Ask Ollama")]
    public void RequestArrangementFromDetectedObjects()
    {
        ObjectDetectionAgent agent = GetObjectDetectionAgent();
        if (agent == null)
        {
            ReportFailure("ObjectDetectionAgent was not found. Add or assign the Meta object detection agent before pressing A.");
            return;
        }

        if (activeDetectionRequest != null)
        {
            StopCoroutine(activeDetectionRequest);
            activeDetectionRequest = null;
        }

        RequestStarted?.Invoke("Detecting objects...");
        activeDetectionRequest = StartCoroutine(DetectObjectsThenRequestArrangement(agent));
    }

    /// <summary>
    /// Sends an Ollama request without fake detected objects.
    /// </summary>
    [ContextMenu("Send AI-Only Ollama Request")]
    public void RequestTestArrangementRecommendation()
    {
        ApplyProviderAssetSettings();

        if (string.IsNullOrWhiteSpace(modelName))
        {
            ReportFailure("Ollama model name is empty. Set a model name in the Inspector, for example \"llama3.2\".");
            return;
        }

        if (!string.IsNullOrWhiteSpace(ollamaEndpoint))
        {
            ollamaEndpoint = ollamaEndpoint.Trim();
        }

        if (string.IsNullOrWhiteSpace(ollamaEndpoint))
        {
            ReportFailure("Ollama endpoint is empty. Set it to http://localhost:11434/api/generate or another reachable Ollama host.");
            return;
        }

        if (activeRequest != null)
        {
            StopCoroutine(activeRequest);
            activeRequest = null;
        }

        string prompt =
            "You are a concise workspace organization assistant in an AR app.\n" +
            "The live object detector is not connected yet, so no objects are available.\n" +
            "Give a short, natural message asking the user to scan or look at the workspace first.\n" +
            "Do not mention testing, model capability, original content, fake scenes, JSON, or code.\n" +
            "Do not invent objects or give object-specific arrangement instructions.\n" +
            "Return valid JSON only in exactly this shape:\n" +
            "{\n" +
            "  \"summary\": \"short status\",\n" +
            "  \"steps\": [\"short user-facing line 1\", \"short user-facing line 2\"],\n" +
            "  \"speak\": \"one short user-facing sentence\"\n" +
            "}\n" +
            "Request id: " + DateTime.UtcNow.Ticks;

        Debug.Log("[OllamaWorkspaceOrganizer] Sending AI-only request to Ollama. No fake scene data is included.", this);
        RequestStarted?.Invoke("Sending request to Ollama...");
        activeRequest = StartCoroutine(SendGenerateRequest(prompt));
    }

    private IEnumerator DetectObjectsThenRequestArrangement(ObjectDetectionAgent agent)
    {
        bool receivedDetections = false;
        List<BoxData> capturedDetections = null;

        UnityAction<List<BoxData>> onDetectionReceived = detections =>
        {
            capturedDetections = detections != null ? new List<BoxData>(detections) : new List<BoxData>();
            receivedDetections = true;
        };

        agent.OnDetectionResponseReceived.AddListener(onDetectionReceived);
        agent.CallInference();

        float deadline = Time.unscaledTime + detectionWaitTimeoutSeconds;
        while (!receivedDetections && Time.unscaledTime < deadline)
        {
            yield return null;
        }

        agent.OnDetectionResponseReceived.RemoveListener(onDetectionReceived);
        activeDetectionRequest = null;

        if (!receivedDetections)
        {
            ReportFailure("No object detections were received before timeout. Make sure passthrough/object detection is running and visible objects are in view.");
            yield break;
        }

        if (capturedDetections == null || capturedDetections.Count == 0)
        {
            ReportFailure("Object detector returned zero objects. Look at the workspace and press A again.");
            yield break;
        }

        string sceneJson = BuildSceneJson(capturedDetections);
        RequestArrangementRecommendation(sceneJson);
    }

    /// <summary>
    /// Sends scene JSON using a runtime-supplied organization goal.
    /// </summary>
    public void RequestArrangementRecommendation(string sceneJson, string userGoal)
    {
        ApplyProviderAssetSettings();

        if (string.IsNullOrWhiteSpace(sceneJson))
        {
            ReportFailure("Scene JSON is empty. Object detection must provide a JSON string before requesting a recommendation.");
            return;
        }

        if (string.IsNullOrWhiteSpace(modelName))
        {
            ReportFailure("Ollama model name is empty. Set a model name in the Inspector, for example \"llama3.2\".");
            return;
        }

        if (!string.IsNullOrWhiteSpace(ollamaEndpoint))
        {
            ollamaEndpoint = ollamaEndpoint.Trim();
        }

        if (string.IsNullOrWhiteSpace(ollamaEndpoint))
        {
            ReportFailure("Ollama endpoint is empty. Set it to http://localhost:11434/api/generate or another reachable Ollama host.");
            return;
        }

        // Keep only one in-flight recommendation request per component instance.
        if (activeRequest != null)
        {
            StopCoroutine(activeRequest);
            activeRequest = null;
        }

        string prompt = BuildPrompt(sceneJson, userGoal);
        RequestStarted?.Invoke("Sending request to Ollama...");
        activeRequest = StartCoroutine(SendGenerateRequest(prompt));
    }

    /// <summary>
    /// Builds a prompt from the configured template by replacing {SCENE_JSON_HERE}.
    /// If the template is empty, the built-in study-desk template is used.
    /// If the template does not contain {SCENE_JSON_HERE}, the scene JSON is appended.
    /// </summary>
    public string BuildPrompt(string sceneJson)
    {
        string template = GetActivePromptTemplate();
        string safeSceneJson = string.IsNullOrWhiteSpace(sceneJson) ? "{}" : sceneJson.Trim();

        if (template.Contains(SceneJsonPlaceholder))
        {
            return template.Replace(SceneJsonPlaceholder, safeSceneJson);
        }

        // Some provider assets store only system instructions. In that case,
        // append the scene JSON instead of failing the request.
        StringBuilder prompt = new StringBuilder(template.Length + safeSceneJson.Length + 32);
        prompt.AppendLine(template.TrimEnd());
        prompt.AppendLine();
        prompt.AppendLine("Scene JSON:");
        prompt.AppendLine(safeSceneJson);
        return prompt.ToString();
    }

    /// <summary>
    /// Builds a prompt from detected scene JSON and a user goal, such as
    /// "organize this study desk for productivity".
    /// </summary>
    public string BuildPrompt(string sceneJson, string userGoal)
    {
        string prompt = BuildPrompt(sceneJson);

        if (string.IsNullOrWhiteSpace(userGoal))
        {
            return prompt;
        }

        string safeGoal = userGoal.Trim();
        if (prompt.Contains(GoalPlaceholder))
        {
            return prompt.Replace(GoalPlaceholder, safeGoal);
        }

        int sceneIndex = prompt.IndexOf("Scene JSON:", StringComparison.OrdinalIgnoreCase);
        if (sceneIndex >= 0)
        {
            return prompt.Insert(sceneIndex, "User goal:\n" + safeGoal + "\n\n");
        }

        return "User goal:\n" + safeGoal + "\n\n" + prompt;
    }

    private bool WasTestButtonPressedThisFrame()
    {
        bool isPressed = IsQuestRightControllerAButtonPressed() || IsKeyboardAKeyPressed();
        bool pressedThisFrame = isPressed && !wasAButtonPressed;
        wasAButtonPressed = isPressed;
        return pressedThisFrame;
    }

    private bool IsQuestRightControllerAButtonPressed()
    {
        rightHandDevices.Clear();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
            UnityEngine.XR.InputDeviceCharacteristics.Controller | UnityEngine.XR.InputDeviceCharacteristics.Right,
            rightHandDevices);

        for (int i = 0; i < rightHandDevices.Count; i++)
        {
            if (rightHandDevices[i].TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool isPressed) && isPressed)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsKeyboardAKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.aKey.isPressed)
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.A);
#else
        return false;
#endif
    }

    private ObjectDetectionAgent GetObjectDetectionAgent()
    {
        if (objectDetectionAgent == null)
        {
            objectDetectionAgent = FindAnyObjectByType<ObjectDetectionAgent>();
        }

        return objectDetectionAgent;
    }

    private static string BuildSceneJson(List<BoxData> detections)
    {
        StringBuilder json = new StringBuilder(256 + detections.Count * 96);
        json.Append("{\"objects\":[");

        for (int i = 0; i < detections.Count; i++)
        {
            if (i > 0)
            {
                json.Append(',');
            }

            BoxData detection = detections[i];
            ParseDetectionLabel(detection.label, out string objectName, out float confidence);

            json.Append('{');
            json.Append("\"name\":");
            AppendJsonString(json, objectName);
            json.Append(",\"rawLabel\":");
            AppendJsonString(json, detection.label);
            string horizontalPosition = InferHorizontalPosition(detection.position.x);
            string verticalPosition = InferVerticalPosition(detection.position.y);
            string zone = InferZone(horizontalPosition, verticalPosition);

            json.Append(",\"zone\":");
            AppendJsonString(json, zone);
            json.Append(",\"horizontalPosition\":");
            AppendJsonString(json, horizontalPosition);
            json.Append(",\"verticalPosition\":");
            AppendJsonString(json, verticalPosition);
            json.Append(",\"size\":");
            AppendJsonString(json, InferSize(detection.scale));
            json.Append(",\"confidence\":");
            json.Append(confidence >= 0f ? confidence.ToString("0.00", CultureInfo.InvariantCulture) : "null");
            json.Append(",\"detectionIndex\":");
            json.Append(i);
            json.Append(",\"box\":{\"x\":");
            json.Append(detection.position.x.ToString("0.###", CultureInfo.InvariantCulture));
            json.Append(",\"y\":");
            json.Append(detection.position.y.ToString("0.###", CultureInfo.InvariantCulture));
            json.Append(",\"width\":");
            json.Append(detection.scale.x.ToString("0.###", CultureInfo.InvariantCulture));
            json.Append(",\"height\":");
            json.Append(detection.scale.y.ToString("0.###", CultureInfo.InvariantCulture));
            json.Append("}}");
        }

        json.Append("],\"zones\":[\"top left\",\"top\",\"top right\",\"left\",\"center\",\"right\",\"bottom left\",\"bottom\",\"bottom right\"],\"source\":\"Meta ObjectDetectionAgent\"}");
        return json.ToString();
    }

    private static string InferHorizontalPosition(float x)
    {
        if (float.IsNaN(x) || float.IsInfinity(x))
        {
            return "camera view";
        }

        if (x >= 0f && x <= 1f)
        {
            if (x < 0.33f)
            {
                return "left";
            }

            if (x > 0.66f)
            {
                return "right";
            }

            return "center";
        }

        return "camera view";
    }

    private static string InferVerticalPosition(float y)
    {
        if (float.IsNaN(y) || float.IsInfinity(y))
        {
            return "camera view";
        }

        if (y >= 0f && y <= 1f)
        {
            if (y < 0.33f)
            {
                return "top";
            }

            if (y > 0.66f)
            {
                return "bottom";
            }

            return "middle";
        }

        return "camera view";
    }

    private static string InferZone(string horizontalPosition, string verticalPosition)
    {
        if (horizontalPosition == "camera view" && verticalPosition == "camera view")
        {
            return "camera view";
        }

        if (verticalPosition == "middle")
        {
            return horizontalPosition;
        }

        if (horizontalPosition == "center")
        {
            return verticalPosition;
        }

        return verticalPosition + " " + horizontalPosition;
    }

    private static string InferSize(Vector2 scale)
    {
        float area = Mathf.Abs(scale.x * scale.y);
        if (area >= 0.16f)
        {
            return "large";
        }

        if (area >= 0.05f)
        {
            return "medium";
        }

        return "small";
    }

    private static void ParseDetectionLabel(string label, out string objectName, out float confidence)
    {
        objectName = string.IsNullOrWhiteSpace(label) ? "unknown object" : label.Trim();
        confidence = -1f;

        int lastSpace = objectName.LastIndexOf(' ');
        if (lastSpace <= 0 || lastSpace >= objectName.Length - 1)
        {
            return;
        }

        string possibleConfidence = objectName.Substring(lastSpace + 1);
        if (!float.TryParse(possibleConfidence, NumberStyles.Float, CultureInfo.InvariantCulture, out confidence))
        {
            confidence = -1f;
            return;
        }

        objectName = objectName.Substring(0, lastSpace).Trim();
        if (string.IsNullOrWhiteSpace(objectName))
        {
            objectName = "unknown object";
        }
    }

    private static void AppendJsonString(StringBuilder json, string value)
    {
        json.Append('"');

        if (!string.IsNullOrEmpty(value))
        {
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                switch (character)
                {
                    case '\\':
                        json.Append("\\\\");
                        break;
                    case '"':
                        json.Append("\\\"");
                        break;
                    case '\n':
                        json.Append("\\n");
                        break;
                    case '\r':
                        json.Append("\\r");
                        break;
                    case '\t':
                        json.Append("\\t");
                        break;
                    default:
                        json.Append(character);
                        break;
                }
            }
        }

        json.Append('"');
    }

    private IEnumerator SendGenerateRequest(string prompt)
    {
        OllamaGenerateRequest requestPayload = new OllamaGenerateRequest(modelName, prompt);
        string requestJson = JsonUtility.ToJson(requestPayload);
        byte[] requestBody = Encoding.UTF8.GetBytes(requestJson);

        using (UnityWebRequest request = new UnityWebRequest(ollamaEndpoint, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(requestBody);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.Max(1, requestTimeoutSeconds);
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");

            yield return request.SendWebRequest();

            activeRequest = null;

            if (request.result == UnityWebRequest.Result.ConnectionError)
            {
                string message = IsLikelyConnectionRefused(request.error)
                    ? "Connection refused. Is Ollama running and reachable at " + ollamaEndpoint + "?"
                    : "Connection error while contacting Ollama: " + SafeErrorText(request.error);

                ReportFailure(message);
                yield break;
            }

            if (request.result == UnityWebRequest.Result.ProtocolError)
            {
                ReportFailure("Ollama returned HTTP " + request.responseCode + ": " + BuildResponseErrorText(request));
                yield break;
            }

            if (request.result == UnityWebRequest.Result.DataProcessingError)
            {
                ReportFailure("Unity could not process the Ollama response: " + SafeErrorText(request.error));
                yield break;
            }

            string responseText = request.downloadHandler != null ? request.downloadHandler.text : null;
            if (string.IsNullOrWhiteSpace(responseText))
            {
                ReportFailure("Ollama returned an empty response.");
                yield break;
            }

            if (!TryParseRecommendation(responseText, out WorkspaceArrangementRecommendation recommendation, out string parseError))
            {
                ReportFailure(parseError);
                yield break;
            }

            onRecommendationReceived.Invoke(recommendation);
            RecommendationReceived?.Invoke(recommendation);
        }
    }

    private bool TryParseRecommendation(
        string ollamaResponseJson,
        out WorkspaceArrangementRecommendation recommendation,
        out string error)
    {
        recommendation = null;
        error = null;

        OllamaGenerateResponse ollamaResponse;
        try
        {
            ollamaResponse = JsonUtility.FromJson<OllamaGenerateResponse>(ollamaResponseJson);
        }
        catch (Exception exception)
        {
            error = "Invalid JSON from Ollama generate endpoint: " + exception.Message;
            return false;
        }

        if (ollamaResponse == null)
        {
            error = "Invalid JSON from Ollama generate endpoint.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ollamaResponse.error))
        {
            error = "Ollama reported an error: " + ollamaResponse.error;
            return false;
        }

        if (string.IsNullOrWhiteSpace(ollamaResponse.response))
        {
            error = "Ollama response JSON did not include a non-empty 'response' field.";
            return false;
        }

        try
        {
            recommendation = JsonUtility.FromJson<WorkspaceArrangementRecommendation>(ollamaResponse.response);
        }
        catch (Exception exception)
        {
            error = "Model response was not valid recommendation JSON: " + exception.Message + "\nRaw model response: " + ollamaResponse.response;
            return false;
        }

        if (!ValidateRecommendation(recommendation, out error))
        {
            error += "\nRaw model response: " + ollamaResponse.response;
            return false;
        }

        return true;
    }

    private string GetActivePromptTemplate()
    {
        ApplyProviderAssetSettings();
        if (string.IsNullOrWhiteSpace(promptTemplate) || IsLegacyDefaultPromptTemplate(promptTemplate))
        {
            promptTemplate = DefaultPromptTemplate;
        }

        return promptTemplate;
    }

    private static bool IsLegacyDefaultPromptTemplate(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return false;
        }

        return template.Contains("You are an organization assistant for a real-world study desk.")
            && template.Contains("Prefer keeping liquids away from electronics.")
            && template.Contains("Return at most 4 steps.");
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

        string providerPrompt = ReadStringMember(
            providerAsset,
            "prompt",
            "promptTemplate",
            "systemPrompt",
            "instructions",
            "systemInstructions");

        if (!string.IsNullOrWhiteSpace(providerPrompt))
        {
            promptTemplate = providerPrompt;
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

    private static bool ValidateRecommendation(WorkspaceArrangementRecommendation recommendation, out string error)
    {
        if (recommendation == null)
        {
            error = "Model response JSON could not be parsed into a recommendation object.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(recommendation.summary))
        {
            error = "Model recommendation is missing the required 'summary' field.";
            return false;
        }

        if (recommendation.steps == null || recommendation.steps.Length == 0)
        {
            error = "Model recommendation is missing the required 'steps' array.";
            return false;
        }

        for (int i = 0; i < recommendation.steps.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(recommendation.steps[i]))
            {
                error = "Model recommendation contains an empty step at index " + i + ".";
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(recommendation.speak))
        {
            error = "Model recommendation is missing the required 'speak' field.";
            return false;
        }

        error = null;
        return true;
    }

    private void ReportFailure(string message)
    {
        Debug.LogError("[OllamaWorkspaceOrganizer] " + message, this);
        onRequestFailed.Invoke(message);
        RequestFailed?.Invoke(message);
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
            || lower.Contains("destination host");
    }

    private static string BuildResponseErrorText(UnityWebRequest request)
    {
        string body = request.downloadHandler != null ? request.downloadHandler.text : null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            return body;
        }

        return SafeErrorText(request.error);
    }

    private static string SafeErrorText(string error)
    {
        return string.IsNullOrWhiteSpace(error) ? "No additional error details." : error;
    }
}

[Serializable]
public sealed class OllamaGenerateRequest
{
    public string model;
    public string prompt;
    public bool stream;
    public OllamaGenerateOptions options;

    // Ollama's JSON mode. The prompt still defines the exact object shape expected.
    public string format;

    public OllamaGenerateRequest(string model, string prompt)
    {
        this.model = model;
        this.prompt = prompt;
        stream = false;
        format = "json";
        options = new OllamaGenerateOptions();
    }
}

[Serializable]
public sealed class OllamaGenerateOptions
{
    public float temperature = 0.2f;
}

[Serializable]
public sealed class OllamaGenerateResponse
{
    public string model;
    public string created_at;
    public string response;
    public bool done;
    public string error;
}

[Serializable]
public sealed class WorkspaceArrangementRecommendation
{
    public string summary;
    public string[] steps;
    public string speak;
}

[Serializable]
public sealed class WorkspaceRecommendationEvent : UnityEvent<WorkspaceArrangementRecommendation>
{
}

[Serializable]
public sealed class StringEvent : UnityEvent<string>
{
}
