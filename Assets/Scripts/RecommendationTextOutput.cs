using TMPro;
using UnityEngine;
using WorkspaceVoiceAssistant;

public class RecommendationTextOutput : MonoBehaviour
{
    [SerializeField] private TMP_Text outputText;
    [SerializeField] private OllamaWorkspaceOrganizer organizer;
    [SerializeField] private VoiceAssistantOrchestrator voiceAssistant;
    [SerializeField, Min(1f)] private float outputFontSize = 4f;
    [SerializeField, Min(1)] private int maxDisplayedSteps = 3;
    [SerializeField] private bool showFullOllamaResponse = true;

    private string latestRecommendationText = string.Empty;
    private string latestStatus = "Ready";
    private string microphoneStatus = "Ready";
    private string detectionStatus = "Waiting";
    private string aiStatus = "Ready";
    private string connectionStatus = "Not checked";

    private void Awake()
    {
        if (outputText == null)
        {
            outputText = GetComponent<TMP_Text>();
        }

        if (organizer == null)
        {
            organizer = GetComponent<OllamaWorkspaceOrganizer>();
        }

        if (voiceAssistant == null)
        {
            voiceAssistant = GetComponent<VoiceAssistantOrchestrator>();
        }

        ApplyTextSettings();
    }

    private void OnValidate()
    {
        ApplyTextSettings();
    }

    private void OnEnable()
    {
        if (organizer == null)
        {
            organizer = FindAnyObjectByType<OllamaWorkspaceOrganizer>();
        }

        if (voiceAssistant == null)
        {
            voiceAssistant = FindAnyObjectByType<VoiceAssistantOrchestrator>();
        }

        if (organizer != null)
        {
            organizer.RecommendationReceived += ShowRecommendation;
            organizer.RequestFailed += ShowError;
            organizer.RequestStarted += ShowStatus;
        }

        if (voiceAssistant != null)
        {
            voiceAssistant.RecommendationReady += ShowRecommendation;
            voiceAssistant.ErrorReceived += ShowError;
            voiceAssistant.StatusChanged += ShowStatus;
        }
    }

    private void OnDisable()
    {
        if (organizer != null)
        {
            organizer.RecommendationReceived -= ShowRecommendation;
            organizer.RequestFailed -= ShowError;
            organizer.RequestStarted -= ShowStatus;
        }

        if (voiceAssistant != null)
        {
            voiceAssistant.RecommendationReady -= ShowRecommendation;
            voiceAssistant.ErrorReceived -= ShowError;
            voiceAssistant.StatusChanged -= ShowStatus;
        }
    }

    public void ShowRecommendation(WorkspaceArrangementRecommendation recommendation)
    {
        if (outputText == null || recommendation == null)
        {
            return;
        }

        ShowAnswer(BuildInstructionText(recommendation));
    }

    public void ShowRecommendation(Recommendation recommendation)
    {
        if (outputText == null || recommendation == null)
        {
            return;
        }

        ShowAnswer(BuildInstructionText(recommendation));
    }

    public void ShowError(string error)
    {
        if (outputText == null)
        {
            return;
        }

        error ??= "Unknown error";
        aiStatus = "Error";
        if (error.Contains("gateway", System.StringComparison.OrdinalIgnoreCase)
            || error.Contains("AI request", System.StringComparison.OrdinalIgnoreCase))
        {
            connectionStatus = "Unavailable";
        }
        latestRecommendationText = "Error: " + error;
        Render();
    }

    public void ShowStatus(string status)
    {
        if (outputText == null)
        {
            return;
        }

        status ??= string.Empty;
        latestStatus = status;
        if (status.Contains("Listening", System.StringComparison.OrdinalIgnoreCase))
        {
            microphoneStatus = "Listening";
        }
        else if (status.Contains("Detecting", System.StringComparison.OrdinalIgnoreCase))
        {
            microphoneStatus = "Ready";
            detectionStatus = "Scanning";
            latestRecommendationText = string.Empty;
        }
        else if (status.Contains("Sending request to AI", System.StringComparison.OrdinalIgnoreCase))
        {
            detectionStatus = "Ready";
            aiStatus = "Waiting";
            connectionStatus = "Checking";
        }
        else if (status.Contains("Speaking", System.StringComparison.OrdinalIgnoreCase))
        {
            aiStatus = "Ready";
        }

        Render();
    }

    private void ShowAnswer(string answer)
    {
        detectionStatus = "Ready";
        aiStatus = "Ready";
        connectionStatus = connectionStatus == "Checking" ? "Connected" : "Local answer";
        latestRecommendationText = answer;
        Render();
    }

    private void Render()
    {
        outputText.text = "Mic: " + microphoneStatus + "   Detection: " + detectionStatus
            + "\nAI: " + aiStatus + "   Connection: " + connectionStatus
            + "\n\n" + (string.IsNullOrWhiteSpace(latestRecommendationText) ? latestStatus : latestRecommendationText);
    }

    private void ApplyTextSettings()
    {
        if (outputText == null)
        {
            return;
        }

        outputText.fontSize = outputFontSize;
        outputText.alignment = TextAlignmentOptions.TopLeft;
        outputText.textWrappingMode = TextWrappingModes.Normal;
        outputText.overflowMode = TextOverflowModes.Ellipsis;
    }

    private string BuildInstructionText(WorkspaceArrangementRecommendation recommendation)
    {
        if (!showFullOllamaResponse)
        {
            if (recommendation.steps == null || recommendation.steps.Length == 0)
            {
                return recommendation.speak;
            }

            int shortCount = Mathf.Min(maxDisplayedSteps, recommendation.steps.Length);
            return string.Join("\n", recommendation.steps, 0, shortCount);
        }

        string steps = recommendation.steps == null || recommendation.steps.Length == 0
            ? "(no steps)"
            : string.Join("\n", recommendation.steps, 0, Mathf.Min(maxDisplayedSteps, recommendation.steps.Length));

        return "Ollama Response\n"
            + "Summary: " + SafeText(recommendation.summary) + "\n"
            + "Steps:\n" + steps + "\n"
            + "Voice: " + SafeText(recommendation.speak);
    }

    private string BuildInstructionText(Recommendation recommendation)
    {
        if (!showFullOllamaResponse)
        {
            if (recommendation.steps == null || recommendation.steps.Count == 0)
            {
                return recommendation.speak;
            }

            int shortCount = Mathf.Min(maxDisplayedSteps, recommendation.steps.Count);
            return string.Join("\n", recommendation.steps.GetRange(0, shortCount));
        }

        string steps = recommendation.steps == null || recommendation.steps.Count == 0
            ? "(no steps)"
            : string.Join("\n", recommendation.steps.GetRange(0, Mathf.Min(maxDisplayedSteps, recommendation.steps.Count)));

        return "Ollama Response\n"
            + "Summary: " + SafeText(recommendation.summary) + "\n"
            + "Steps:\n" + steps + "\n"
            + "Voice: " + SafeText(recommendation.speak);
    }

    private static string SafeText(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? "(empty)" : text.Trim();
    }
}
