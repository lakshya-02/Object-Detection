using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

public class RecommendationTextOutputTests
{
    [Test]
    public void DetectionStatusShowsAllFourIndicators()
    {
        var owner = new GameObject("assistant panel");
        try
        {
            var text = owner.AddComponent<TextMeshProUGUI>();
            var output = owner.AddComponent<RecommendationTextOutput>();
            var serialized = new SerializedObject(output);
            serialized.FindProperty("outputText").objectReferenceValue = text;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            output.ShowStatus("Detecting objects...");

            StringAssert.Contains("Mic:", text.text);
            StringAssert.Contains("Detection:", text.text);
            StringAssert.Contains("AI:", text.text);
            StringAssert.Contains("Connection:", text.text);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }
}
