using System.Collections;
using Meta.XR.BuildingBlocks.AIBlocks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public class ModelInferenceSmokeTests
{
    [UnityTest, Timeout(180000)]
    public IEnumerator BundledModelRunsOnAnEditorTexture()
    {
        var provider = AssetDatabase.LoadAssetAtPath<UnityInferenceEngineProvider>(
            "Assets/MetaXR/ObjectDetection_UnityInferenceEngine_ProviderProfile.asset");
        Assert.IsNotNull(provider);

        var image = new Texture2D(640, 640, TextureFormat.RGBA32, false);
        try
        {
            var pixels = new Color32[640 * 640];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(128, 128, 128, 255);
            image.SetPixels32(pixels);
            image.Apply();

            var task = provider.DetectAsync(image);
            while (!task.IsCompleted) yield return null;

            Assert.IsFalse(task.IsFaulted, task.Exception?.ToString());
            Assert.IsNotNull(task.Result);
        }
        finally
        {
            Object.DestroyImmediate(image);
        }
    }
}
