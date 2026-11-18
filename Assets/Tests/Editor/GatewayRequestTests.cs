using NUnit.Framework;
using UnityEngine;
using WorkspaceVoiceAssistant;

public class GatewayRequestTests
{
    [Test]
    public void RequestCarriesProviderModelAndPrompt()
    {
        string json = JsonUtility.ToJson(new AiGatewayRequest("google", "gemini-2.5-flash-lite", "Count books"));

        StringAssert.Contains("\"provider\":\"google\"", json);
        StringAssert.Contains("\"model\":\"gemini-2.5-flash-lite\"", json);
        StringAssert.Contains("\"prompt\":\"Count books\"", json);
    }
}
