using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Paramatrix.Content;
using Paramatrix.Scenario;
using Paramatrix.State;

namespace Paramatrix.Gameplay
{
    public sealed class SaveLoadResult
    {
        internal SaveLoadResult(IReadOnlyList<Diagnostic> diagnostics)
        {
            Diagnostics = diagnostics;
        }

        public bool Succeeded => Diagnostics.Count == 0;

        public IReadOnlyList<Diagnostic> Diagnostics { get; }
    }

    /// <summary>
    /// Save = persistent State + Scenario snapshot (Р1, T21), as JSON through <see cref="ISaveStorage"/>.
    /// Load validates both snapshots completely before changing anything (A15): any mismatch with current content
    /// rejects the load and leaves the world as it was.
    /// </summary>
    public sealed class SaveService
    {
        public const int FormatVersion = 1;

        private readonly ContentRegistry _registry;
        private readonly StateSystem _state;
        private readonly ScenarioRuntime _scenario;
        private readonly GameFlow _flow;
        private readonly LocationSystem _location;
        private readonly ISaveStorage _storage;

        public SaveService(ContentRegistry registry, StateSystem state, ScenarioRuntime scenario, GameFlow flow, LocationSystem location, ISaveStorage storage)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
            _location = location ?? throw new ArgumentNullException(nameof(location));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        /// <summary>Only in Free Roam with Scenario idle (T6, T21).</summary>
        public bool CanSave =>
            _flow.Owner == ControlOwner.Player
            && !_flow.InTransition
            && _flow.Presentation == DialoguePresentation.None
            && _scenario.CanSave;

        public SaveLoadResult Save(string slot)
        {
            var location = new SourceLocation(slot, null, 0, 0);
            if (!CanSave)
            {
                return Fail(DiagnosticCode.SaveFailed, location, "Saving is possible only in free roam while no scene is running.");
            }

            var root = new JObject
            {
                ["format_version"] = FormatVersion,
                ["state"] = ExportState(_state.ExportPersistent()),
                ["scenario"] = ExportScenario(_scenario.Export()),
            };

            return _storage.TryWrite(slot, root.ToString(Formatting.Indented), out var error)
                ? new SaveLoadResult(Array.Empty<Diagnostic>())
                : Fail(DiagnosticCode.SaveFailed, location, "Could not write the save: " + error);
        }

        /// <summary>
        /// Reads and applies a save, then loads the hero's location scene technically (no Visit, no triggers).
        /// The caller holds a system operation of <see cref="GameFlow"/> for the duration.
        /// </summary>
        public void Load(string slot, Action<SaveLoadResult> done)
        {
            if (done == null)
            {
                throw new ArgumentNullException(nameof(done));
            }

            if (_flow.Owner != ControlOwner.System)
            {
                throw new InvalidOperationException("Load runs inside a system operation of GameFlow.");
            }

            var location = new SourceLocation(slot, null, 0, 0);
            if (!_scenario.CanSave)
            {
                done(Fail(DiagnosticCode.SaveFailed, location, "Cannot load while a scene is running."));
                return;
            }

            if (!_storage.TryRead(slot, out var json, out var readError))
            {
                done(Fail(DiagnosticCode.SaveFailed, location, "Could not read the save: " + readError));
                return;
            }

            var errors = new DiagnosticBag();
            Parse(json, location, errors, out var stateSnapshot, out var scenarioSnapshot);
            if (scenarioSnapshot != null)
            {
                foreach (var diagnostic in _scenario.ValidateSnapshot(scenarioSnapshot, slot))
                {
                    errors.Report(diagnostic);
                }
            }

            if (errors.Items.Count > 0 || stateSnapshot == null || scenarioSnapshot == null)
            {
                done(new SaveLoadResult(errors.Items));
                return;
            }

            var restored = _state.Restore(stateSnapshot, slot);
            if (!restored.Succeeded)
            {
                done(new SaveLoadResult(restored.Diagnostics)); // State unchanged, Scenario untouched
                return;
            }

            var scenarioRestored = _scenario.Restore(scenarioSnapshot, slot);
            if (!scenarioRestored.Succeeded)
            {
                throw new InvalidOperationException("Scenario snapshot failed after successful validation.");
            }

            _location.LoadTechnical(ok => done(ok
                ? new SaveLoadResult(Array.Empty<Diagnostic>())
                : Fail(DiagnosticCode.SceneLoadFailed, location, "The save was applied, but its location scene could not be loaded.")));
        }

        // ---------- format ----------

        private static JArray ExportState(StateSnapshot snapshot)
        {
            var array = new JArray();
            foreach (var entry in snapshot.Entries)
            {
                array.Add(new JObject
                {
                    ["target"] = entry.Address.Entity.Value,
                    ["property"] = entry.Address.Property,
                    ["value"] = StateValueJson.ToToken(entry.Value),
                });
            }

            return array;
        }

        private static JObject ExportScenario(ScenarioSnapshot snapshot)
        {
            var events = new JArray();
            foreach (var record in snapshot.Events)
            {
                events.Add(new JObject
                {
                    ["event"] = record.Event.Value,
                    ["completed"] = record.EverCompleted,
                    ["last_visit"] = record.LastCompletedVisit,
                });
            }

            return new JObject { ["visit"] = snapshot.CurrentVisit, ["events"] = events };
        }

        private void Parse(string json, SourceLocation location, DiagnosticBag errors, out StateSnapshot state, out ScenarioSnapshot scenario)
        {
            state = null;
            scenario = null;

            JObject root;
            try
            {
                root = JToken.Parse(json) as JObject;
            }
            catch (JsonReaderException exception)
            {
                errors.Error(DiagnosticCode.SaveFileInvalid, location, "Save is not valid JSON: " + exception.Message);
                return;
            }

            if (root == null)
            {
                errors.Error(DiagnosticCode.SaveFileInvalid, location, "Save root must be an object.");
                return;
            }

            if (!(root["format_version"] is JValue version) || version.Type != JTokenType.Integer || version.Value<int>() != FormatVersion)
            {
                errors.Error(DiagnosticCode.SaveFileInvalid, location, $"Save format_version must be {FormatVersion}.");
                return;
            }

            state = ParseState(root["state"] as JArray, location, errors);
            scenario = ParseScenario(root["scenario"] as JObject, location, errors);
        }

        private StateSnapshot ParseState(JArray array, SourceLocation location, DiagnosticBag errors)
        {
            if (array == null)
            {
                errors.Error(DiagnosticCode.SaveFileInvalid, location, "Save has no 'state' array.");
                return null;
            }

            var entries = new List<StateSnapshotEntry>();
            foreach (var token in array)
            {
                var target = (token as JObject)?["target"]?.Type == JTokenType.String ? token["target"].Value<string>() : null;
                var property = (token as JObject)?["property"]?.Type == JTokenType.String ? token["property"].Value<string>() : null;
                var valueToken = (token as JObject)?["value"];
                if (target == null || property == null || valueToken == null
                    || !EntityId.TryParse(target, out var entity, out _) || !StateAddress.IsValidPropertyName(property))
                {
                    errors.Error(DiagnosticCode.SaveFileInvalid, location, $"Malformed state entry: {token.ToString(Formatting.None)}");
                    continue;
                }

                var address = new StateAddress(entity, property);
                if (!_registry.TryGetState(address, out var definition))
                {
                    var code = _registry.TryGet<WorldEntityDefinition>(entity, out _) ? DiagnosticCode.SnapshotUnknownProperty : DiagnosticCode.SnapshotUnknownEntity;
                    errors.Error(code, location, $"Saved property {address} does not exist in current content.");
                    continue;
                }

                if (!StateValueJson.TryRead(valueToken, definition.Type, out var value, out var error))
                {
                    errors.Error(DiagnosticCode.SnapshotInvalidValue, location, $"Saved value of {address}: {error}");
                    continue;
                }

                entries.Add(new StateSnapshotEntry(address, value));
            }

            return new StateSnapshot(entries);
        }

        private static ScenarioSnapshot ParseScenario(JObject obj, SourceLocation location, DiagnosticBag errors)
        {
            if (obj == null || !(obj["visit"] is JValue visit) || visit.Type != JTokenType.Integer || !(obj["events"] is JArray events))
            {
                errors.Error(DiagnosticCode.SaveFileInvalid, location, "Save has no valid 'scenario' object.");
                return null;
            }

            var records = new List<EventRecord>();
            foreach (var token in events)
            {
                var record = token as JObject;
                var id = record?["event"]?.Type == JTokenType.String ? record["event"].Value<string>() : null;
                if (id == null || record["completed"]?.Type != JTokenType.Boolean || record["last_visit"]?.Type != JTokenType.Integer
                    || !EntityId.TryParse(id, out var eventId, out _))
                {
                    errors.Error(DiagnosticCode.SaveFileInvalid, location, $"Malformed event record: {token.ToString(Formatting.None)}");
                    continue;
                }

                records.Add(new EventRecord(eventId, record["completed"].Value<bool>(), record["last_visit"].Value<int>()));
            }

            return new ScenarioSnapshot(visit.Value<int>(), records);
        }

        private static SaveLoadResult Fail(DiagnosticCode code, SourceLocation location, string message) =>
            new SaveLoadResult(new[] { new Diagnostic(code, Severity.Error, message, location) });
    }
}
