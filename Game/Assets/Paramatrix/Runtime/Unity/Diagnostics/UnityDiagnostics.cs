using System;
using System.Collections.Generic;
using Paramatrix.Content;
using UnityEngine;

namespace Paramatrix.Unity
{
    /// <summary>Diagnostics sink of the running game: Unity Console plus a list the on-screen overlay shows (T20).</summary>
    public sealed class UnityDiagnostics : IDiagnosticsSink
    {
        private readonly List<Diagnostic> _items = new List<Diagnostic>();

        public IReadOnlyList<Diagnostic> Items => _items;

        public event Action<Diagnostic> Reported;

        public void Report(Diagnostic diagnostic)
        {
            if (diagnostic == null)
            {
                throw new ArgumentNullException(nameof(diagnostic));
            }

            _items.Add(diagnostic);
            if (diagnostic.Severity == Severity.Error)
            {
                Debug.LogError("[Paramatrix] " + diagnostic);
            }
            else
            {
                Debug.LogWarning("[Paramatrix] " + diagnostic);
            }

            Reported?.Invoke(diagnostic);
        }
    }
}
