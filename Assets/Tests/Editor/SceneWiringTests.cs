using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Meta.XR.BuildingBlocks.AIBlocks;
using WorkspaceVoiceAssistant;

public class SceneWiringTests
{
    [Test]
    public void SampleSceneHasConnectedAssistantComponents()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        var manager = GameObject.Find("manager");
        Assert.IsNotNull(manager);

        var voice = manager.GetComponent<VoiceAssistantOrchestrator>();
        var planner = manager.GetComponent<OllamaSpeechPlanner>();
        var speech = manager.GetComponent<SpeechInputManager>();
        var output = manager.GetComponent<RecommendationTextOutput>();
        Assert.IsNotNull(voice);
        Assert.IsNotNull(planner);
        Assert.IsNotNull(speech);
        Assert.IsNotNull(output);

        var voiceData = new SerializedObject(voice);
        Assert.IsNotNull(voiceData.FindProperty("objectDetectionAgent").objectReferenceValue);
        Assert.IsNotNull(voiceData.FindProperty("speechOutputManager").objectReferenceValue);

        var plannerData = new SerializedObject(planner);
        Assert.AreEqual("google", plannerData.FindProperty("providerName").stringValue);
        StringAssert.EndsWith("/api/answer", plannerData.FindProperty("gatewayEndpoint").stringValue);

        var speechData = new SerializedObject(speech);
        Assert.IsNotNull(speechData.FindProperty("speechToTextAgent").objectReferenceValue);

        var visualizer = GameObject.Find("[BuildingBlock] Object Detection").GetComponent<ObjectDetectionVisualizer>();
        var visualizerData = new SerializedObject(visualizer);
        Assert.IsTrue(visualizerData.FindProperty("showBoundingBoxes").boolValue);
        var marker = visualizerData.FindProperty("boundingBoxPrefab").objectReferenceValue as GameObject;
        Assert.IsNotNull(marker);
        Assert.IsEmpty(marker.GetComponentsInChildren<Renderer>(true));
    }
}
