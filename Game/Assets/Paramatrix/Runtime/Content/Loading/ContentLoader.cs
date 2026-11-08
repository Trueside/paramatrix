using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Paramatrix.Content
{
    public sealed class ContentLoadResult
    {
        internal ContentLoadResult(ContentRegistry registry, IReadOnlyList<Diagnostic> diagnostics)
        {
            Registry = registry;
            Diagnostics = diagnostics;
        }

        /// <summary>Null when any Error was reported: invalid content blocks the start (Architecture 0.3.1 §10).</summary>
        public ContentRegistry Registry { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public bool Succeeded => Registry != null;
    }

    /// <summary>
    /// The only entry point that turns content JSON into definitions (Architecture 0.3.1 §2.1):
    /// parse every file → build drafts with structural checks → cross-reference validation → registry.
    /// Errors accumulate; loading never stops at the first one.
    /// </summary>
    public static class ContentLoader
    {
        public const int FormatVersion = 1;

        public static ContentLoadResult Load(IReadOnlyList<ContentSource> sources)
        {
            if (sources == null)
            {
                throw new ArgumentNullException(nameof(sources));
            }

            // Files are processed in path order so that the result never depends on file-system enumeration order.
            var ordered = new List<ContentSource>(sources);
            ordered.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            for (var i = 1; i < ordered.Count; i++)
            {
                if (string.Equals(ordered[i - 1].Name, ordered[i].Name, StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Content source '{ordered[i].Name}' is passed twice.", nameof(sources));
                }
            }

            var diagnostics = new DiagnosticBag();
            var drafts = new List<Draft>();
            var declarationOrder = 0;

            foreach (var source in ordered)
            {
                var root = JsonDocument.Parse(source, diagnostics);
                if (root == null)
                {
                    continue;
                }

                var rootReader = JsonObjectReader.ForObject(root, source.Name, diagnostics, "Content file root");
                if (rootReader == null)
                {
                    continue;
                }

                if (rootReader.RequiredInteger("format_version", out var version) && version != FormatVersion)
                {
                    diagnostics.Error(
                        DiagnosticCode.UnsupportedFormatVersion,
                        rootReader.LocationOf(rootReader.Object["format_version"]),
                        $"format_version {version} is not supported; expected {FormatVersion}.");
                }

                var definitions = rootReader.RequiredArray("definitions");
                rootReader.ReportUnknownFields();
                if (definitions == null)
                {
                    continue;
                }

                foreach (var element in definitions)
                {
                    var draft = DraftParser.Parse(element, source.Name, diagnostics, declarationOrder++);
                    if (draft != null)
                    {
                        drafts.Add(draft);
                    }
                }
            }

            var definitionsBuilt = ContentLinker.Link(drafts, diagnostics, ordered.Count > 0 ? ordered[0].Name : null);
            var registry = diagnostics.HasErrors ? null : new ContentRegistry(definitionsBuilt);
            return new ContentLoadResult(registry, diagnostics.Items);
        }
    }
}
