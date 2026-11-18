using System.Collections.Generic;
using Meta.XR.BuildingBlocks.AIBlocks;
using Unity.Collections;
using NUnit.Framework;
using UnityEngine;
using WorkspaceVoiceAssistant;

public class SceneContextBuilderTests
{
    [Test]
    public void BoxDimensionsAreComputedFromPixelCorners()
    {
        var owner = new GameObject("scene context test");
        try
        {
            var builder = owner.AddComponent<SceneContextBuilder>();
            var boxes = new List<BoxData>
            {
                new BoxData
                {
                    label = "book 0.92",
                    position = new Vector3(100, 120, 0),
                    scale = new Vector3(220, 300, 0)
                }
            };

            SceneData scene = builder.BuildSceneData(boxes);

            Assert.AreEqual(120, scene.objects[0].width);
            Assert.AreEqual(180, scene.objects[0].height);
            Assert.AreEqual("unknown", scene.surface);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void PixelBoundsUseImageDimensionsForScreenZone()
    {
        var owner = new GameObject("scene context test");
        try
        {
            var builder = owner.AddComponent<SceneContextBuilder>();
            var boxes = new List<BoxData>
            {
                new BoxData
                {
                    label = "book 0.92",
                    position = new Vector3(64, 48, 0),
                    scale = new Vector3(192, 144, 0)
                }
            };

            SceneData scene = builder.BuildSceneData(boxes, 640, 480);

            Assert.AreEqual("top left", scene.objects[0].zone);
            Assert.AreEqual("left", scene.objects[0].horizontalPosition);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void CountsCurrentDetectionsInsteadOfAskingTheModel()
    {
        var owner = new GameObject("scene context test");
        try
        {
            var builder = owner.AddComponent<SceneContextBuilder>();
            var scene = new SceneData();
            scene.objects.Add(new DetectedObject { name = "book", zone = "left" });
            scene.objects.Add(new DetectedObject { name = "book", zone = "right" });

            Assert.IsTrue(builder.TryAnswer(scene, "How many books are there?", out Recommendation answer));
            Assert.AreEqual("I can see 2 books.", answer.speak);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [TestCase("How many chairs in front of me?", "chair", "I can see 2 chairs.")]
    [TestCase("How many bottles on the table?", "bottle", "I can see 2 bottles.")]
    [TestCase("How many books are there?", "book", "I can see 2 books.")]
    [TestCase("How many people are there?", "person", "I can see 2 people.")]
    [TestCase("How many buses are there?", "bus", "I can see 2 buses.")]
    [TestCase("Now how many person did you, uh, getting observed?", "person", "I can see 2 people.")]
    [TestCase("How many laptops did you observe?", "laptop", "I can see 2 laptops.")]
    public void CountQuestionUsesObjectClassAcrossPhrasings(string question, string objectName, string expected)
    {
        var owner = new GameObject("scene context test");
        try
        {
            var builder = owner.AddComponent<SceneContextBuilder>();
            var scene = new SceneData();
            scene.objects.Add(new DetectedObject { name = objectName, zone = "left" });
            scene.objects.Add(new DetectedObject { name = objectName, zone = "right" });

            Assert.IsTrue(builder.TryAnswer(scene, question, out Recommendation answer));
            Assert.AreEqual(expected, answer.speak);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [TestCase("How many laptops or TV monitors you can tell there are right now?", "laptop", "tv monitor", true,
        "Current detections show 1 laptop and 1 tv monitor.")]
    [TestCase("How many laptops or TV can you see?", "laptop", "tv monitor", true,
        "Current detections show 1 laptop and 1 tv monitor.")]
    [TestCase("How many books and bottles can you see?", "book", "bottle", false,
        "Current detections show 1 book and 0 bottles.")]
    public void CountQuestionReportsEachRequestedClass(string question, string first, string second,
        bool secondVisible, string expected)
    {
        var owner = new GameObject("scene context test");
        try
        {
            var builder = owner.AddComponent<SceneContextBuilder>();
            var scene = new SceneData();
            scene.objects.Add(new DetectedObject { name = first });
            if (secondVisible) scene.objects.Add(new DetectedObject { name = second });

            Assert.IsTrue(builder.TryAnswer(scene, question, out Recommendation answer));
            Assert.AreEqual(expected, answer.speak);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [TestCase("So what are the objects you are currently able to see?")]
    [TestCase("What are the objects are currently visible?")]
    public void SpokenInventoryWordingUsesCurrentDetections(string question)
    {
        var owner = new GameObject("scene context test");
        try
        {
            var builder = owner.AddComponent<SceneContextBuilder>();
            var scene = new SceneData();
            scene.objects.Add(new DetectedObject { name = "laptop" });

            Assert.IsTrue(builder.TryAnswer(scene, question, out Recommendation answer));
            Assert.AreEqual("I can see 1 laptop.", answer.speak);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [TestCase("laptop")]
    [TestCase("bottle")]
    [TestCase("backpack")]
    public void SingularObjectQuestionListsCurrentDetection(string objectName)
    {
        var owner = new GameObject("scene context test");
        try
        {
            var builder = owner.AddComponent<SceneContextBuilder>();
            var scene = new SceneData();
            scene.objects.Add(new DetectedObject { name = objectName });

            Assert.IsTrue(builder.TryAnswer(scene, "What is the object?", out Recommendation answer));
            Assert.AreEqual("I can see 1 " + objectName + ".", answer.speak);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void SpokenLeadInDoesNotChangeTheDetectedObjectAnswer()
    {
        var owner = new GameObject("scene context test");
        try
        {
            var builder = owner.AddComponent<SceneContextBuilder>();
            var scene = new SceneData();
            scene.objects.Add(new DetectedObject { name = "laptop" });

            Assert.IsTrue(builder.TryAnswer(scene, "Uh, what is the object?", out Recommendation answer));
            Assert.AreEqual("I can see 1 laptop.", answer.speak);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void DemonstrativeDoesNotHideDetectedObjectColor()
    {
        var owner = new GameObject("scene context test");
        try
        {
            var builder = owner.AddComponent<SceneContextBuilder>();
            var scene = new SceneData();
            scene.objects.Add(new DetectedObject { name = "backpack", color = "blue" });

            Assert.IsTrue(builder.TryAnswer(scene, "What color is this backpack?", out Recommendation answer));
            Assert.AreEqual("The detected backpack is blue.", answer.speak);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void SpokenTvCountMatchesDetectorLabel()
    {
        var owner = new GameObject("scene context test");
        try
        {
            var builder = owner.AddComponent<SceneContextBuilder>();
            var scene = new SceneData();
            scene.objects.Add(new DetectedObject { name = "tv" });

            Assert.IsTrue(builder.TryAnswer(scene, "How many TV monitors you can see?", out Recommendation answer));
            Assert.AreEqual("I can see 1 tv.", answer.speak);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void DistinguishesTwoChairsBySupportedCameraColors()
    {
        var owner = new GameObject("scene context test");
        var pixels = new NativeArray<Color32>(100 * 100, Allocator.Temp);
        try
        {
            var builder = owner.AddComponent<SceneContextBuilder>();
            for (int y = 0; y < 100; y++)
                for (int x = 0; x < 100; x++)
                    pixels[y * 100 + x] = x < 50 ? new Color32(245, 210, 20, 255) : new Color32(25, 180, 35, 255);

            var scene = new SceneData();
            scene.objects.Add(new DetectedObject { name = "chair", x = 0, y = 0, width = 50, height = 100 });
            scene.objects.Add(new DetectedObject { name = "chair", x = 50, y = 0, width = 50, height = 100 });
            builder.ApplyColors(scene, pixels, 100, 100);

            Assert.AreEqual("yellow", scene.objects[0].color);
            Assert.AreEqual("green", scene.objects[1].color);
            var plan = new AiPlanResponse();
            plan.actions.Add(new AiPlanAction { objectIndex = 0, action = "move_left" });
            plan.actions.Add(new AiPlanAction { objectIndex = 1, action = "move_right" });
            Assert.IsTrue(builder.TryBuildAdvice(scene, plan, out Recommendation answer));
            StringAssert.Contains("yellow chair", answer.speak);
            StringAssert.Contains("green chair", answer.speak);
        }
        finally
        {
            pixels.Dispose();
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void MissingObjectIsNeverClaimedVisible()
    {
        var owner = new GameObject("scene context test");
        try
        {
            var builder = owner.AddComponent<SceneContextBuilder>();
            var scene = new SceneData();
            scene.objects.Add(new DetectedObject { name = "book", zone = "left" });

            Assert.IsTrue(builder.TryAnswer(scene, "Where is my pen?", out Recommendation answer));
            Assert.AreEqual("I can't confirm a pen from the current detections.", answer.speak);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void PromptDoesNotForceOrganizationStepsForQuestions()
    {
        var owner = new GameObject("scene context test");
        try
        {
            var builder = owner.AddComponent<SceneContextBuilder>();
            string prompt = builder.BuildPrompt(builder.BuildSceneJson(new SceneData()), "What objects can you see?");

            StringAssert.Contains("What objects can you see?", prompt);
            StringAssert.DoesNotContain("Give 3 to 5", prompt);
            StringAssert.Contains("{\"actions\":[]}", prompt);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void AdviceUsesOnlyReferencedDetections()
    {
        var owner = new GameObject("scene context test");
        try
        {
            var builder = owner.AddComponent<SceneContextBuilder>();
            var scene = new SceneData();
            scene.objects.Add(new DetectedObject { name = "bottle" });
            scene.objects.Add(new DetectedObject { name = "laptop" });
            var plan = new AiPlanResponse();
            plan.actions.Add(new AiPlanAction { objectIndex = 0, action = "separate", targetIndex = 1 });

            Assert.IsTrue(builder.TryBuildAdvice(scene, plan, out Recommendation answer));
            Assert.AreEqual("Move the bottle away from the laptop.", answer.speak);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void AdviceRejectsUnknownDetectionIndex()
    {
        var owner = new GameObject("scene context test");
        try
        {
            var builder = owner.AddComponent<SceneContextBuilder>();
            var scene = new SceneData();
            scene.objects.Add(new DetectedObject { name = "book" });
            var plan = new AiPlanResponse();
            plan.actions.Add(new AiPlanAction { objectIndex = 4, action = "move_left" });

            Assert.IsFalse(builder.TryBuildAdvice(scene, plan, out _));
            Assert.AreEqual("I can't suggest a supported placement from the current detections.",
                builder.BuildUnavailableAdvice().speak);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void UnsupportedVisualQuestionDoesNotBecomeOrganizationAdvice()
    {
        var owner = new GameObject("scene context test");
        try
        {
            var builder = owner.AddComponent<SceneContextBuilder>();
            var scene = new SceneData();
            scene.objects.Add(new DetectedObject { name = "book" });

            Assert.IsTrue(builder.TryAnswer(scene, "What color is the book?", out Recommendation answer));
            StringAssert.Contains("can't determine", answer.speak);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [TestCase("bottle", "What color is the bottle?", "The detected bottle is blue.")]
    [TestCase("backpack", "What is the color of my backpack?", "The detected backpack is blue.")]
    [TestCase("chair", "What color are the chairs?", "The detected chair is blue.")]
    [TestCase("chair", "Which color of the chair is in front of me?", "The detected chair is blue.")]
    public void ColorQuestionsUseDetectedClass(string objectName, string question, string expected)
    {
        var owner = new GameObject("scene context test");
        try
        {
            var builder = owner.AddComponent<SceneContextBuilder>();
            var scene = new SceneData();
            scene.objects.Add(new DetectedObject { name = objectName, color = "blue", zone = "left" });

            Assert.IsTrue(builder.TryAnswer(scene, question, out Recommendation answer));
            Assert.AreEqual(expected, answer.speak);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void MultipleDetectedObjectsKeepSeparateColorsAndLocations()
    {
        var owner = new GameObject("scene context test");
        try
        {
            var builder = owner.AddComponent<SceneContextBuilder>();
            var scene = new SceneData();
            scene.objects.Add(new DetectedObject { name = "bottle", color = "green", zone = "left" });
            scene.objects.Add(new DetectedObject { name = "bottle", color = "yellow", zone = "right" });

            Assert.IsTrue(builder.TryAnswer(scene, "What color are the bottles?", out Recommendation colors));
            StringAssert.Contains("green in the left", colors.speak);
            StringAssert.Contains("yellow in the right", colors.speak);
            Assert.IsTrue(builder.TryAnswer(scene, "Where are the bottles?", out Recommendation locations));
            StringAssert.Contains("green bottle in left", locations.speak);
            StringAssert.Contains("yellow bottle in right", locations.speak);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }
}
