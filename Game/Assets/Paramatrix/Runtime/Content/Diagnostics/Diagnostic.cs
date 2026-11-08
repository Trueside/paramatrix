using System;
using System.Collections.Generic;
using System.Globalization;

namespace Paramatrix.Content
{
    public enum Severity
    {
        Warning = 1,
        Error,
    }

    /// <summary>
    /// Where a diagnostic points: content source name, JSON path inside it and, when known, line/column (1-based).
    /// </summary>
    public readonly struct SourceLocation
    {
        public SourceLocation(string source, string path, int line, int column)
        {
            Source = source;
            Path = path;
            Line = line;
            Column = column;
        }

        public static SourceLocation None => default;

        public string Source { get; }

        public string Path { get; }

        public int Line { get; }

        public int Column { get; }

        public override string ToString()
        {
            if (Source == null)
            {
                return "<no location>";
            }

            var position = Line > 0
                ? string.Format(CultureInfo.InvariantCulture, "({0},{1})", Line, Column)
                : string.Empty;
            var path = string.IsNullOrEmpty(Path) ? string.Empty : " at " + Path;
            return Source + position + path;
        }
    }

    public sealed class Diagnostic
    {
        public Diagnostic(DiagnosticCode code, Severity severity, string message, SourceLocation location)
        {
            Code = code;
            Severity = severity;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Location = location;
        }

        public DiagnosticCode Code { get; }

        public Severity Severity { get; }

        public string Message { get; }

        public SourceLocation Location { get; }

        public override string ToString() =>
            $"{(Severity == Severity.Error ? "error" : "warning")} {Code}: {Message} [{Location}]";
    }

    /// <summary>Receiver of diagnostics. Content loading and runtime systems report through it.</summary>
    public interface IDiagnosticsSink
    {
        void Report(Diagnostic diagnostic);
    }

    /// <summary>Collects diagnostics in report order.</summary>
    public sealed class DiagnosticBag : IDiagnosticsSink
    {
        private readonly List<Diagnostic> _items = new List<Diagnostic>();

        public IReadOnlyList<Diagnostic> Items => _items;

        public bool HasErrors
        {
            get
            {
                foreach (var item in _items)
                {
                    if (item.Severity == Severity.Error)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public void Report(Diagnostic diagnostic) => _items.Add(diagnostic ?? throw new ArgumentNullException(nameof(diagnostic)));

        public void Error(DiagnosticCode code, SourceLocation location, string message) =>
            _items.Add(new Diagnostic(code, Severity.Error, message, location));

        public void Warning(DiagnosticCode code, SourceLocation location, string message) =>
            _items.Add(new Diagnostic(code, Severity.Warning, message, location));
    }
}
