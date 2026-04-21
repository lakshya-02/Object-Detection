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

        outputText.text = BuildInstructionText(recommendation);
    }

    public void ShowRecommendation(Recommendation recommendation)
    {
        if (outputText == null || recommendation == null)
        {
            return;
        }

        outputText.text = BuildInstructionText(recommendation);
    }

    public void ShowError(string error)
    {
        if (outputText == null)
        {
            return;
        }

        outputText.text = "LLM Error:\n" + error;
    }

    public void ShowStatus(string status)
    {
        if (outputText == null)
        {
            return;
        }

        outputText.text = status;
    }

    private void ApplyTextSettings()
    {
        if (outputText == null)
        {
            return;
        }

        outputText.fontSize = outputFontSize;
        outputText.textWrappingMode = TextWrappingModes.Normal;
        outputText.overflowMode = TextOverflowModes.Ellipsis;
    }

    private string BuildInstructionText(WorkspaceArrangementRecommendation recommendation)
    {
        if (recommendation.steps == null || recommendation.steps.Length == 0)
        {
            return recommendation.speak;
        }

        int count = Mathf.Min(maxDisplayedSteps, recommendation.steps.Length);
        return string.Join("\n", recommendation.steps, 0, count);
    }

    private string BuildInstructionText(Recommendation recommendation)
    {
        if (recommendation.steps == null || recommendation.steps.Count == 0)
        {
            return recommendation.speak;
        }

        int count = Mathf.Min(maxDisplayedSteps, recommendation.steps.Count);
        return string.Join("\n", recommendation.steps.GetRange(0, count));
    }
}
