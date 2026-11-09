using NUnit.Framework;
using UnityEngine;

namespace Paramatrix.EditorTests
{
    /// <summary>One quality level for the 2D game, synced to the monitor (frame rate = refresh rate).</summary>
    public sealed class QualitySettingsTests
    {
        [Test]
        public void SingleLevelWithVerticalSync()
        {
            Assert.That(QualitySettings.names, Is.EqualTo(new[] { "Default" }));
            Assert.That(QualitySettings.GetQualityLevel(), Is.EqualTo(0));
            Assert.That(QualitySettings.vSyncCount, Is.EqualTo(1));
        }
    }
}
