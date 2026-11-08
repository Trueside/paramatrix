using NUnit.Framework;
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
