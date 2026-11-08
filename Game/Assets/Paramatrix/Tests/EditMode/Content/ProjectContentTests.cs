using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using Paramatrix.Content;

namespace Paramatrix.Tests.Content
{
    /// <summary>The game's real content files must load without diagnostics.</summary>
    public sealed class ProjectContentTests
    {
        private static string ContentFolder([CallerFilePath] string here = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here), "..", "..", "..", "Content"));

        [Test]
        public void ProjectContentIsValid()
        {
            var folder = ContentFolder();
            var files = Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories);
            Assert.That(files, Is.Not.Empty, "no content found in " + folder);

            var sources = files
                .Select(f => new ContentSource("Assets/Paramatrix/Content/" + Path.GetRelativePath(folder, f).Replace('\\', '/'), File.ReadAllText(f)))
                .ToArray();
            var result = ContentLoader.Load(sources);

            Assert.That(result.Diagnostics, Is.Empty, TestContent.Describe(result.Diagnostics));
            Assert.That(result.Succeeded, Is.True);
        }
    }
}
