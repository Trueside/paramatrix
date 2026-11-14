using NUnit.Framework;
using Paramatrix.Unity;
using Paramatrix.Unity.Editor;
using UnityEditor;

namespace Paramatrix.EditorTests
{
    public sealed class PlayFromBootstrapTests
    {
        [Test]
        public void PlayStartsFromTheBootstrapScene()
        {
            var scene = PlayFromBootstrap.StartScene();

            Assert.That(scene, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(scene), Is.EqualTo("Assets/Paramatrix/Scenes/Bootstrap.unity"));
        }

        [TestCase(60.0, 1)]
        [TestCase(59.94, 1)]
        [TestCase(120.0, 2)]
        [TestCase(240.0, 4)]
        [TestCase(144.0, 0)]
        [TestCase(165.0, 0)]
        [TestCase(75.0, 0)]
        [TestCase(0.0, 0)]
        public void VerticalSyncOnlyOnMultiplesOfSixtyHertz(double refreshRate, int expected)
        {
            Assert.That(FramePacing.VSyncInterval(refreshRate), Is.EqualTo(expected));
        }

        [Test]
        public void GameRunsAtSixtyFrames()
        {
            Assert.That(FramePacing.FrameRate, Is.EqualTo(60));
            Assert.That(FramePacing.FrameRate, Is.EqualTo(Walker.TicksPerSecond), "one movement tick per frame");
        }

        [TestCase("SampleScene", false, true)]
        [TestCase("Location_Office", false, true)]
        [TestCase("Bootstrap", true, false)]
        [TestCase("InitTestScene638123456789", false, false)]
        public void RedirectOnlyWhenTheGameDidNotStart(string activeScene, bool hasBootstrap, bool expected)
        {
            Assert.That(PlayFromBootstrap.NeedsRedirect(activeScene, hasBootstrap), Is.EqualTo(expected));
        }
    }
}
