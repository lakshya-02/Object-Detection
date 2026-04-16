using TMPro;
using UnityEngine;

public class RecommendationTextOutput : MonoBehaviour
{
    [SerializeField] private TMP_Text outputText;
    [SerializeField] private OllamaWorkspaceOrganizer organizer;
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
            return;
        }

        organizer.RecommendationReceived += ShowRecommendation;
        organizer.RequestFailed += ShowError;
        organizer.RequestStarted += ShowStatus;
    }

    private void OnDisable()
    {
        if (organizer == null)
        {
            return;
        }

        organizer.RecommendationReceived -= ShowRecommendation;
        organizer.RequestFailed -= ShowError;
        organizer.RequestStarted -= ShowStatus;
    }

    public void ShowRecommendation(WorkspaceArrangementRecommendation recommendation)
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
}
