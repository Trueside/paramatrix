using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Paramatrix.Content
{
    /// <summary>Structural parsing of one element of <c>definitions</c>. Kind comes from the ID prefix.</summary>
    internal static class DraftParser
    {
        public static Draft Parse(JToken element, string source, DiagnosticBag diagnostics, int declarationOrder)
        {
            var reader = JsonObjectReader.ForObject(element, source, diagnostics, "Definition");
            if (reader == null)
            {
                return null;
            }

            if (!reader.RequiredEntityId("id", null, out var id))
            {
                // Without a valid ID nothing else can be attributed; field-level problems would be noise.
                return null;
            }

            Draft draft;
            switch (id.Kind)
            {
                case EntityKind.Game:
                    draft = ParseGameSettings(reader, id);
                    break;
                case EntityKind.Character:
                case EntityKind.Item:
                case EntityKind.Object:
                case EntityKind.Location:
                case EntityKind.Story:
                    draft = ParseWorldEntity(reader, id, declarationOrder);
                    break;
                case EntityKind.Condition:
                    draft = ParseNamedCondition(reader, id);
                    break;
                case EntityKind.Event:
                    draft = ParseEventHeader(reader, id);
                    break;
                case EntityKind.Emotion:
                    draft = ParseEmotion(reader, id);
                    break;
                case EntityKind.Dialogue:
                case EntityKind.Response:
                    // Parsed entirely by the linker, which also reports their unknown fields.
                    return new RawDraft(id, reader.Location, source, reader.Object);
                default:
                    diagnostics.Error(
                        DiagnosticCode.UnsupportedDefinitionKind,
                        reader.Location,
                        $"Definitions of kind '{EntityKinds.Prefix(id.Kind)}' are not supported yet.");
                    return null;
            }

            if (draft != null)
            {
                reader.ReportUnknownFields();
            }

            return draft;
        }

        private static Draft ParseEventHeader(JsonObjectReader reader, EntityId id)
        {
            var draft = new EventDraft(id, reader.Location, reader.Source);

            if (reader.RequiredString("execution", out var execution))
            {
                switch (execution)
                {
                    case "exclusive": draft.ExecutionType = ExecutionType.Exclusive; break;
                    case "background": draft.ExecutionType = ExecutionType.Background; break;
                    default:
                        reader.Diagnostics.Error(
                            DiagnosticCode.InvalidExecutionType,
                            reader.LocationOf(reader.Object["execution"]),
                            $"Execution type '{execution}' is not supported; use exclusive or background.");
                        break;
                }
            }

            if (reader.RequiredString("repeat", out var repeat))
            {
                switch (repeat)
                {
                    case "once": draft.RepeatPolicy = RepeatPolicy.Once; break;
                    case "repeatable": draft.RepeatPolicy = RepeatPolicy.Repeatable; break;
                    case "once_per_location_visit": draft.RepeatPolicy = RepeatPolicy.OncePerLocationVisit; break;
                    default:
                        reader.Diagnostics.Error(
                            DiagnosticCode.InvalidRepeatPolicy,
                            reader.LocationOf(reader.Object["repeat"]),
                            $"Repeat policy '{repeat}' is not supported; use once, repeatable or once_per_location_visit.");
                        break;
                }
            }

            draft.Triggers = reader.RequiredArray("triggers");
            draft.LaunchCondition = reader.Optional("launch_condition");
            draft.Actions = reader.RequiredArray("actions");
            return draft;
        }

        /// <summary>A dictionary emotion: its ID name is the avatar file name, 'name' is how authors call it.</summary>
        private static Draft ParseEmotion(JsonObjectReader reader, EntityId id)
        {
            if (!Emotions.IsValidName(id.Name))
            {
                reader.Diagnostics.Error(DiagnosticCode.InvalidEmotion, reader.LocationOf(reader.Object["id"]),
                    $"Emotion '{id.Name}' is not a valid name: lowercase Latin letters, digits and '_', starting with a letter.");
            }

            return new EmotionDraft(id, reader.Location) { Name = ReadText(reader, "name") };
        }

        private static Draft ParseNamedCondition(JsonObjectReader reader, EntityId id)
        {
            var token = reader.Required("condition");
            if (token == null)
            {
                return null;
            }

            var root = ConditionParser.Parse(token, reader.Source, reader.Diagnostics);

            // An invalid tree still yields a draft (with no root) so the ID counts as defined:
            // references to it must not add "unknown condition" noise on top of the real error.
            return new ConditionDefinitionDraft(id, reader.Location, root);
        }

        private static Draft ParseGameSettings(JsonObjectReader reader, EntityId id)
        {
            if (id.Value != GameSettingsDefinition.SettingsId)
            {
                reader.Diagnostics.Error(
                    DiagnosticCode.InvalidEntityId,
                    reader.LocationOf(reader.Object["id"]),
                    $"The only allowed 'game' definition is '{GameSettingsDefinition.SettingsId}', found '{id}'.");
                return null;
            }

            var draft = new GameSettingsDraft(id, reader.Location);
            var token = reader.Required("player_character");
            if (token != null && reader.ReadEntityId(token, "player_character", EntityKind.Character, out var player))
            {
                draft.PlayerCharacter = player;
                draft.PlayerCharacterSource = reader.LocationOf(token);
            }

            return draft;
        }

        private static Draft ParseWorldEntity(JsonObjectReader reader, EntityId id, int declarationOrder)
        {
            var draft = new WorldDraft(id, reader.Location, declarationOrder);

            switch (id.Kind)
            {
                case EntityKind.Character:
                    draft.Name = ReadText(reader, "name");
                    draft.PrimaryInteraction = ReadPrimaryInteraction(reader);
                    ReadInitialPlace(reader, draft, "initial_location");
                    ReadEmotions(reader, draft);
                    break;
                case EntityKind.Item:
                    draft.Name = ReadText(reader, "name");
                    draft.Description = ReadText(reader, "description");
                    ReadInitialPlace(reader, draft, "initial_holder");
                    break;
                case EntityKind.Object:
                    draft.PrimaryInteraction = ReadPrimaryInteraction(reader);
                    break;
                case EntityKind.Location:
                    draft.Scene = ReadText(reader, "scene");
                    break;
            }

            var statesToken = reader.Optional("states");
            if (statesToken != null)
            {
                var states = JsonObjectReader.ForObject(statesToken, reader.Source, reader.Diagnostics, "Field 'states'");
                if (states != null)
                {
                    foreach (var property in states.Object.Properties())
                    {
                        draft.DeclaredProperties.Add(property.Name);
                        var state = ParseState(reader, id, property);
                        if (state != null)
                        {
                            draft.States.Add(state);
                        }
                    }
                }
            }

            return draft;
        }

        private static string ReadText(JsonObjectReader reader, string field)
        {
            if (!reader.RequiredString(field, out var text))
            {
                return null;
            }

            if (text.Trim().Length == 0)
            {
                reader.Diagnostics.Error(DiagnosticCode.EmptyText, reader.LocationOf(reader.Object[field]), $"Field '{field}' must not be empty.");
                return null;
            }

            return text;
        }

        /// <summary>Optional avatar emotions (Р10): unique valid names, <c>default</c> among them.</summary>
        private static void ReadEmotions(JsonObjectReader reader, WorldDraft draft)
        {
            var array = reader.OptionalArray("emotions", out var present);
            if (!present || array == null)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in array)
            {
                if (!reader.ReadString(token, "emotions", out var name))
                {
                    continue;
                }

                if (!Emotions.IsValidName(name))
                {
                    reader.Diagnostics.Error(DiagnosticCode.InvalidEmotion, reader.LocationOf(token),
                        $"Emotion '{name}' is not a valid name: lowercase Latin letters, digits and '_', starting with a letter.");
                    continue;
                }

                if (!seen.Add(name))
                {
                    reader.Diagnostics.Error(DiagnosticCode.DuplicateEmotion, reader.LocationOf(token), $"Emotion '{name}' is declared twice.");
                    continue;
                }

                draft.Emotions.Add(name);
                draft.EmotionSources.Add(reader.LocationOf(token));
            }

            if (!seen.Contains(Emotions.Default))
            {
                reader.Diagnostics.Error(DiagnosticCode.MissingDefaultEmotion, reader.LocationOf(array),
                    $"Emotions of {draft.Id} must include '{Emotions.Default}': it is used by every line that names no emotion.");
            }
        }

        private static InteractionType ReadPrimaryInteraction(JsonObjectReader reader)
        {
            if (!reader.RequiredString("primary_interaction", out var text))
            {
                return default;
            }

            if (InteractionTypes.TryParse(text, out var type) && InteractionTypes.IsAllowedAsPrimary(type))
            {
                return type;
            }

            reader.Diagnostics.Error(
                DiagnosticCode.InvalidInteractionType,
                reader.LocationOf(reader.Object["primary_interaction"]),
                $"primary_interaction '{text}' is not allowed; use one of: primary, take, talk, use, exit.");
            return default;
        }

        private static void ReadInitialPlace(JsonObjectReader reader, WorldDraft draft, string field)
        {
            if (!reader.RequiredString(field, out var text))
            {
                return;
            }

            var location = reader.LocationOf(reader.Object[field]);
            if (draft.Id.Kind == EntityKind.Character && text == BuiltInStates.None)
            {
                draft.InitialPlace = text;
                draft.InitialPlaceSource = location;
                return;
            }

            if (draft.Id.Kind == EntityKind.Item && (text == BuiltInStates.Inventory || text == BuiltInStates.Consumed))
            {
                draft.InitialPlace = text;
                draft.InitialPlaceSource = location;
                return;
            }

            if (!EntityId.TryParse(text, out var target, out var error))
            {
                reader.Diagnostics.Error(DiagnosticCode.InvalidEntityId, location, error);
                return;
            }

            var allowed = target.Kind == EntityKind.Location
                || (draft.Id.Kind == EntityKind.Item && target.Kind == EntityKind.Character);
            if (!allowed)
            {
                var expected = draft.Id.Kind == EntityKind.Character
                    ? "'none' or a location ID"
                    : "'inventory', 'consumed', a location ID or a character ID";
                reader.Diagnostics.Error(DiagnosticCode.InvalidReferenceKind, location, $"Field '{field}' must be {expected}, found '{text}'.");
                return;
            }

            draft.InitialPlace = text;
            draft.InitialPlaceSource = location;
        }

        private static StateDraft ParseState(JsonObjectReader owner, EntityId entity, JProperty property)
        {
            var diagnostics = owner.Diagnostics;
            var propertyLocation = owner.LocationOf(property);

            if (!StateAddress.IsValidPropertyName(property.Name))
            {
                diagnostics.Error(
                    DiagnosticCode.InvalidPropertyName,
                    propertyLocation,
                    $"State property name '{property.Name}' is invalid: lowercase Latin letters, digits and '_', starting with a letter.");
                return null;
            }

            if ((entity.Kind == EntityKind.Character && property.Name == BuiltInStates.Location)
                || (entity.Kind == EntityKind.Item && property.Name == BuiltInStates.Holder))
            {
                diagnostics.Error(
                    DiagnosticCode.BuiltInPropertyRedeclared,
                    propertyLocation,
                    $"'{property.Name}' is a built-in property of {EntityKinds.Prefix(entity.Kind)} entities; set its initial value with the dedicated field instead.");
                return null;
            }

            var reader = JsonObjectReader.ForObject(property.Value, owner.Source, diagnostics, $"State '{property.Name}'");
            if (reader == null)
            {
                return null;
            }

            var draft = new StateDraft(new StateAddress(entity, property.Name), propertyLocation);
            var valid = true;

            valid &= ReadStateType(reader, draft);
            valid &= ReadPersistence(reader, draft);
            valid &= ReadMutationPolicy(reader, draft);
            valid &= ReadRange(reader, draft);
            valid &= ReadEnumValues(reader, draft);
            valid &= ReadDefault(reader, draft);

            reader.ReportUnknownFields();
            return valid ? draft : null;
        }

        private static bool ReadStateType(JsonObjectReader reader, StateDraft draft)
        {
            if (!reader.RequiredString("type", out var text))
            {
                return false;
            }

            switch (text)
            {
                case "boolean": draft.Type = StateType.Boolean; return true;
                case "integer": draft.Type = StateType.Integer; return true;
                case "string": draft.Type = StateType.String; return true;
                case "enum": draft.Type = StateType.Enum; return true;
            }

            reader.Diagnostics.Error(
                DiagnosticCode.InvalidStateType,
                reader.LocationOf(reader.Object["type"]),
                $"State type '{text}' is not supported; use boolean, integer, string or enum.");
            return false;
        }

        private static bool ReadPersistence(JsonObjectReader reader, StateDraft draft)
        {
            if (!reader.RequiredString("persistence", out var text))
            {
                return false;
            }

            switch (text)
            {
                case "persistent": draft.Persistence = Persistence.Persistent; return true;
                case "transient": draft.Persistence = Persistence.Transient; return true;
            }

            var message = text == "reconstructible"
                ? "Persistence 'reconstructible' is not part of MVP; use persistent or transient."
                : $"Persistence '{text}' is not supported; use persistent or transient.";
            reader.Diagnostics.Error(DiagnosticCode.InvalidPersistence, reader.LocationOf(reader.Object["persistence"]), message);
            return false;
        }

        private static bool ReadMutationPolicy(JsonObjectReader reader, StateDraft draft)
        {
            if (!reader.RequiredString("mutation_policy", out var text))
            {
                return false;
            }

            switch (text)
            {
                case "scenario_only": draft.MutationPolicy = MutationPolicy.ScenarioOnly; return true;
                case "system_only": draft.MutationPolicy = MutationPolicy.SystemOnly; return true;
                case "scenario_or_system": draft.MutationPolicy = MutationPolicy.ScenarioOrSystem; return true;
            }

            reader.Diagnostics.Error(
                DiagnosticCode.InvalidMutationPolicy,
                reader.LocationOf(reader.Object["mutation_policy"]),
                $"Mutation policy '{text}' is not supported; use scenario_only, system_only or scenario_or_system.");
            return false;
        }

        private static bool ReadRange(JsonObjectReader reader, StateDraft draft)
        {
            var minOk = reader.OptionalInteger("min", out var min, out var hasMin);
            var maxOk = reader.OptionalInteger("max", out var max, out var hasMax);
            if (!minOk || !maxOk)
            {
                return false;
            }

            if ((hasMin || hasMax) && draft.Type != 0 && draft.Type != StateType.Integer)
            {
                var field = hasMin ? "min" : "max";
                reader.Diagnostics.Error(
                    DiagnosticCode.FieldNotApplicable,
                    reader.LocationOf(reader.Object[field]),
                    $"Field '{field}' applies only to integer State.");
                return false;
            }

            if (hasMin && hasMax && min > max)
            {
                // The range itself is wrong; checking the default against it would only add noise.
                reader.Diagnostics.Error(DiagnosticCode.InvalidRange, reader.LocationOf(reader.Object["min"]), $"min {min} is greater than max {max}.");
                return false;
            }

            draft.Min = hasMin ? min : (int?)null;
            draft.Max = hasMax ? max : (int?)null;
            return true;
        }

        private static bool ReadEnumValues(JsonObjectReader reader, StateDraft draft)
        {
            var array = reader.OptionalArray("values", out var present);
            if (!present)
            {
                if (draft.Type == StateType.Enum)
                {
                    reader.Diagnostics.Error(DiagnosticCode.MissingRequiredField, reader.Location, "Enum State needs field 'values'.");
                    return false;
                }

                return true;
            }

            if (array == null)
            {
                return false;
            }

            if (draft.Type != 0 && draft.Type != StateType.Enum)
            {
                reader.Diagnostics.Error(DiagnosticCode.FieldNotApplicable, reader.LocationOf(array), "Field 'values' applies only to enum State.");
                return false;
            }

            if (array.Count == 0)
            {
                reader.Diagnostics.Error(DiagnosticCode.EmptyEnumValues, reader.LocationOf(array), "Enum 'values' must not be empty.");
                return false;
            }

            var valid = true;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in array)
            {
                if (!reader.ReadString(item, "values", out var text))
                {
                    valid = false;
                    continue;
                }

                if (!StateValueReader.IsValidEnumValueFormat(text))
                {
                    reader.Diagnostics.Error(
                        DiagnosticCode.InvalidEnumValue,
                        reader.LocationOf(item),
                        $"Enum value '{text}' is invalid: use a lowercase name or an Entity ID.");
                    valid = false;
                    continue;
                }

                if (!seen.Add(text))
                {
                    reader.Diagnostics.Error(DiagnosticCode.DuplicateEnumValue, reader.LocationOf(item), $"Enum value '{text}' is listed twice.");
                    valid = false;
                    continue;
                }

                draft.EnumValues.Add(text);
                draft.EnumValueSources.Add(reader.LocationOf(item));
            }

            return valid;
        }

        private static bool ReadDefault(JsonObjectReader reader, StateDraft draft)
        {
            var token = reader.Required("default");
            if (token == null || draft.Type == 0)
            {
                return false;
            }

            if (!StateValueReader.TryRead(token, draft.Type, out var value, out var error))
            {
                reader.Diagnostics.Error(DiagnosticCode.InvalidDefaultValue, reader.LocationOf(token), "Invalid default: " + error);
                return false;
            }

            if (draft.Type == StateType.Integer
                && ((draft.Min.HasValue && value.AsInteger < draft.Min.Value) || (draft.Max.HasValue && value.AsInteger > draft.Max.Value)))
            {
                reader.Diagnostics.Error(DiagnosticCode.InvalidDefaultValue, reader.LocationOf(token), $"Default {value} is outside min/max.");
                return false;
            }

            if (draft.Type == StateType.Enum && draft.EnumValues.Count > 0 && !draft.EnumValues.Contains(value.AsEnum))
            {
                reader.Diagnostics.Error(DiagnosticCode.InvalidDefaultValue, reader.LocationOf(token), $"Default '{value.AsEnum}' is not one of the enum values.");
                return false;
            }

            draft.Default = value;
            return true;
        }
    }
}
