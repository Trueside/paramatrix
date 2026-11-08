using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Paramatrix.Content;
using static Paramatrix.Tests.Content.TestContent;

namespace Paramatrix.Tests.Content
{
    public sealed class ContentLoaderTests
    {
        // ---------- valid content ----------

        [Test]
        public void MinimalContentLoads()
        {
            var registry = LoadValid();

            Assert.That(registry.Game.PlayerCharacter, Is.EqualTo(EntityId.Parse("character.paranoik")));
            var hero = registry.Get<CharacterDefinition>(EntityId.Parse("character.paranoik"));
            Assert.That(hero.Name, Is.EqualTo("Параноик"));
            Assert.That(hero.PrimaryInteraction, Is.EqualTo(InteractionType.Talk));
            Assert.That(registry.Get<LocationDefinition>(EntityId.Parse("location.office")).Scene, Is.EqualTo("Location_Office"));
        }

        [Test]
        public void CharacterGetsBuiltInLocation()
        {
            var registry = LoadValid(
                "{ 'id': 'location.corridor', 'scene': 'Location_Corridor' }",
                "{ 'id': 'character.trinity', 'name': 'Тринити', 'initial_location': 'none', 'primary_interaction': 'talk' }");

            var state = registry.Get<CharacterDefinition>(EntityId.Parse("character.trinity")).LocationState;
            Assert.That(state.IsBuiltIn, Is.True);
            Assert.That(state.Type, Is.EqualTo(StateType.Enum));
            Assert.That(state.Default, Is.EqualTo(StateValue.Enum("none")));
            Assert.That(state.EnumValues, Is.EqualTo(new[] { "none", "location.corridor", "location.office" }));
            Assert.That(state.Persistence, Is.EqualTo(Persistence.Persistent));
            Assert.That(state.MutationPolicy, Is.EqualTo(MutationPolicy.ScenarioOrSystem));
        }

        [Test]
        public void ItemGetsBuiltInHolder()
        {
            var registry = LoadValid(
                "{ 'id': 'item.key', 'name': 'Ключ', 'description': 'Латунный.', 'initial_holder': 'location.office' }",
                "{ 'id': 'item.pill', 'name': 'Таблетка', 'description': 'Красная.', 'initial_holder': 'character.paranoik' }",
                "{ 'id': 'item.note', 'name': 'Записка', 'description': 'Мятая.', 'initial_holder': 'inventory' }");

            var key = registry.Get<ItemDefinition>(EntityId.Parse("item.key")).HolderState;
            Assert.That(key.IsBuiltIn, Is.True);
            Assert.That(key.Default, Is.EqualTo(StateValue.Enum("location.office")));
            Assert.That(key.EnumValues, Is.EqualTo(new[] { "inventory", "consumed", "location.office", "character.paranoik" }));
            Assert.That(key.MutationPolicy, Is.EqualTo(MutationPolicy.SystemOnly));
            Assert.That(registry.Get<ItemDefinition>(EntityId.Parse("item.pill")).HolderState.Default.AsEnum, Is.EqualTo("character.paranoik"));
            Assert.That(registry.Get<ItemDefinition>(EntityId.Parse("item.note")).HolderState.Default.AsEnum, Is.EqualTo("inventory"));
        }

        [Test]
        public void AllStateTypesLoad()
        {
            var registry = LoadValid(Story(
                "{ 'flag': { 'type': 'boolean', 'default': false, 'persistence': 'persistent', 'mutation_policy': 'scenario_only' }," +
                "  'count': { 'type': 'integer', 'default': 2, 'min': 0, 'max': 3, 'persistence': 'transient', 'mutation_policy': 'system_only' }," +
                "  'note': { 'type': 'string', 'default': '', 'persistence': 'persistent', 'mutation_policy': 'scenario_or_system' }," +
                "  'mood': { 'type': 'enum', 'values': ['calm', 'alarmed'], 'default': 'calm', 'persistence': 'persistent', 'mutation_policy': 'scenario_only' }," +
                "  'where': { 'type': 'enum', 'values': ['location.office', 'nowhere'], 'default': 'nowhere', 'persistence': 'persistent', 'mutation_policy': 'scenario_only' } }"));

            Assert.That(registry.TryGetState(StateAddress.Parse("story.test", "flag"), out var flag), Is.True);
            Assert.That(flag.Default, Is.EqualTo(StateValue.Boolean(false)));
            Assert.That(registry.TryGetState(StateAddress.Parse("story.test", "count"), out var count), Is.True);
            Assert.That(count.Min, Is.EqualTo(0));
            Assert.That(count.Max, Is.EqualTo(3));
            Assert.That(count.Persistence, Is.EqualTo(Persistence.Transient));
            Assert.That(registry.TryGetState(StateAddress.Parse("story.test", "note"), out var note), Is.True);
            Assert.That(note.Default, Is.EqualTo(StateValue.String("")));
            Assert.That(registry.TryGetState(StateAddress.Parse("story.test", "mood"), out var mood), Is.True);
            Assert.That(mood.EnumValues, Is.EqualTo(new[] { "calm", "alarmed" }));
            Assert.That(mood.IsBuiltIn, Is.False);
            Assert.That(registry.TryGetState(StateAddress.Parse("story.test", "where"), out _), Is.True);
        }

        [Test]
        public void ObjectStoryAndStatelessEntitiesLoad()
        {
            var registry = LoadValid(
                "{ 'id': 'object.office_door', 'primary_interaction': 'exit', 'states': {} }",
                "{ 'id': 'story.morpheus_meeting' }");

            Assert.That(registry.Get<ObjectDefinition>(EntityId.Parse("object.office_door")).PrimaryInteraction, Is.EqualTo(InteractionType.Exit));
            Assert.That(registry.Get<StoryDefinition>(EntityId.Parse("story.morpheus_meeting")).States, Is.Empty);
        }

        [Test]
        public void ResultDoesNotDependOnSourceOrder()
        {
            var a = new ContentSource("a.json", File("{ 'id': 'item.b', 'name': 'B', 'description': 'B.', 'initial_holder': 'inventory' }"));
            var b = new ContentSource("b.json", File("{ 'id': 'item.a', 'name': 'A', 'description': 'A.', 'initial_holder': 'inventory' }"));

            var first = LoadFiles(Base(), a, b).Registry;
            var second = LoadFiles(b, a, Base()).Registry;

            // Files are taken in path order: a.json, b.json, base.json → item.b is declared first.
            Assert.That(first.ItemsInDeclarationOrder.Select(i => i.Id.Value), Is.EqualTo(new[] { "item.b", "item.a" }));
            Assert.That(second.ItemsInDeclarationOrder.Select(i => i.Id.Value), Is.EqualTo(new[] { "item.b", "item.a" }));
            Assert.That(first.All.Select(d => d.Id.Value), Is.EqualTo(second.All.Select(d => d.Id.Value)));
        }

        [Test]
        public void SameSourcePassedTwiceIsCodeError()
        {
            Assert.Throws<ArgumentException>(() => LoadFiles(Base(), Base()));
        }

        // ---------- file structure ----------

        [Test]
        public void InvalidJson() => AssertErrors(LoadFiles(Base(), new ContentSource("bad.json", "{ 'format_version': 1, ")), DiagnosticCode.InvalidJson);

        [Test]
        public void RootMustBeObject() => AssertErrors(LoadFiles(Base(), new ContentSource("x.json", "[]")), DiagnosticCode.UnexpectedJsonType);

        [Test]
        public void FormatVersionRequired() =>
            AssertErrors(LoadFiles(Base(), new ContentSource("x.json", "{ 'definitions': [] }")), DiagnosticCode.MissingRequiredField);

        [Test]
        public void FormatVersionMustBeSupported() =>
            AssertErrors(LoadFiles(Base(), new ContentSource("x.json", "{ 'format_version': 2, 'definitions': [] }")), DiagnosticCode.UnsupportedFormatVersion);

        [Test]
        public void DefinitionsRequired() =>
            AssertErrors(LoadFiles(Base(), new ContentSource("x.json", "{ 'format_version': 1 }")), DiagnosticCode.MissingRequiredField);

        [Test]
        public void UnknownRootField() =>
            AssertErrors(LoadFiles(Base(), new ContentSource("x.json", "{ 'format_version': 1, 'definitions': [], 'version': 1 }")), DiagnosticCode.UnknownField);

        [Test]
        public void DefinitionMustBeObject() => AssertErrors(Load("'item.key'"), DiagnosticCode.UnexpectedJsonType);

        [Test]
        public void IdRequired() => AssertErrors(Load("{ 'name': 'Ключ' }"), DiagnosticCode.MissingRequiredField);

        [Test]
        public void IdMustBeValid() => AssertErrors(Load("{ 'id': 'Item.Key' }"), DiagnosticCode.InvalidEntityId);

        [Test]
        public void UnknownDefinitionField() =>
            AssertErrors(Load("{ 'id': 'story.x', 'stats': {} }"), DiagnosticCode.UnknownField);

        [Test]
        public void DuplicateIdAcrossFiles() => AssertErrors(Load(Office), DiagnosticCode.DuplicateId);

        [Test]
        public void KindsNotImplementedYetAreRejected() =>
            AssertErrors(Load("{ 'id': 'event.intro' }"), DiagnosticCode.UnsupportedDefinitionKind);

        [Test]
        public void ErrorsAccumulateAcrossFiles()
        {
            var result = LoadFiles(
                Base(),
                new ContentSource("one.json", File("{ 'id': 'story.a', 'oops': 1 }")),
                new ContentSource("two.json", File("{ 'id': 'story.b', 'oops': 2 }")));

            AssertErrors(result, DiagnosticCode.UnknownField, DiagnosticCode.UnknownField);
            Assert.That(result.Diagnostics.Select(d => d.Location.Source), Is.EquivalentTo(new[] { "one.json", "two.json" }));
        }

        // ---------- game settings ----------

        [Test]
        public void GameSettingsRequired() =>
            AssertErrors(LoadFiles(new ContentSource("x.json", File(Hero, Office))), DiagnosticCode.MissingGameSettings);

        [Test]
        public void OnlyGameSettingsIdAllowed() =>
            AssertErrors(Load("{ 'id': 'game.options', 'player_character': 'character.paranoik' }"), DiagnosticCode.InvalidEntityId);

        [Test]
        public void PlayerCharacterMustExist() =>
            AssertErrors(
                LoadFiles(new ContentSource("x.json", File("{ 'id': 'game.settings', 'player_character': 'character.neo' }", Hero, Office))),
                DiagnosticCode.UnknownReference);

        [Test]
        public void PlayerCharacterMustBeCharacter() =>
            AssertErrors(
                LoadFiles(new ContentSource("x.json", File("{ 'id': 'game.settings', 'player_character': 'location.office' }", Hero, Office))),
                DiagnosticCode.InvalidReferenceKind);

        [Test]
        public void PlayerCharacterMustStartInLocation() =>
            AssertErrors(
                LoadFiles(new ContentSource("x.json", File(
                    Settings,
                    Office,
                    "{ 'id': 'character.paranoik', 'name': 'П', 'initial_location': 'none', 'primary_interaction': 'talk' }"))),
                DiagnosticCode.PlayerCharacterWithoutLocation);

        // ---------- entity fields ----------

        [Test]
        public void CharacterNameRequired() =>
            AssertErrors(Load("{ 'id': 'character.neo', 'initial_location': 'none', 'primary_interaction': 'talk' }"), DiagnosticCode.MissingRequiredField);

        [Test]
        public void TextMustNotBeEmpty() =>
            AssertErrors(Load("{ 'id': 'item.key', 'name': '  ', 'description': 'x', 'initial_holder': 'inventory' }"), DiagnosticCode.EmptyText);

        [TestCase("kick")]
        [TestCase("inspect")]
        [TestCase("apply_item")]
        [TestCase("combine_items")]
        public void PrimaryInteractionMustBeAllowed(string type) =>
            AssertErrors(Load("{ 'id': 'object.door', 'primary_interaction': '" + type + "' }"), DiagnosticCode.InvalidInteractionType);

        [Test]
        public void InitialLocationMustExist() =>
            AssertErrors(Load("{ 'id': 'character.neo', 'name': 'Нео', 'initial_location': 'location.roof', 'primary_interaction': 'talk' }"), DiagnosticCode.UnknownReference);

        [Test]
        public void InitialLocationMustBeLocation() =>
            AssertErrors(Load("{ 'id': 'character.neo', 'name': 'Нео', 'initial_location': 'character.paranoik', 'primary_interaction': 'talk' }"), DiagnosticCode.InvalidReferenceKind);

        [Test]
        public void InitialLocationRejectsItemPlaces() =>
            AssertErrors(Load("{ 'id': 'character.neo', 'name': 'Нео', 'initial_location': 'inventory', 'primary_interaction': 'talk' }"), DiagnosticCode.InvalidEntityId);

        [Test]
        public void InitialHolderMustExist() =>
            AssertErrors(Load("{ 'id': 'item.key', 'name': 'К', 'description': 'К.', 'initial_holder': 'character.neo' }"), DiagnosticCode.UnknownReference);

        [Test]
        public void InitialHolderMustBeLocationOrCharacter() =>
            AssertErrors(Load("{ 'id': 'item.key', 'name': 'К', 'description': 'К.', 'initial_holder': 'story.x' }"), DiagnosticCode.InvalidReferenceKind);

        [Test]
        public void LocationSceneRequired() => AssertErrors(Load("{ 'id': 'location.roof' }"), DiagnosticCode.MissingRequiredField);

        // ---------- state definitions ----------

        private static string State(string body) => Story("{ 'p': { " + body + " } }");

        private const string Tail = "'persistence': 'persistent', 'mutation_policy': 'scenario_only'";

        [Test]
        public void StatesMustBeObject() => AssertErrors(Load(Story("[]")), DiagnosticCode.UnexpectedJsonType);

        [Test]
        public void InvalidPropertyName() =>
            AssertErrors(Load(Story("{ 'Open': { 'type': 'boolean', 'default': false, " + Tail + " } }")), DiagnosticCode.InvalidPropertyName);

        [Test]
        public void BuiltInLocationCannotBeDeclared() =>
            AssertErrors(
                Load("{ 'id': 'character.neo', 'name': 'Нео', 'initial_location': 'none', 'primary_interaction': 'talk', " +
                     "'states': { 'location': { 'type': 'string', 'default': '', " + Tail + " } } }"),
                DiagnosticCode.BuiltInPropertyRedeclared);

        [Test]
        public void BuiltInHolderCannotBeDeclared() =>
            AssertErrors(
                Load("{ 'id': 'item.key', 'name': 'К', 'description': 'К.', 'initial_holder': 'inventory', " +
                     "'states': { 'holder': { 'type': 'string', 'default': '', " + Tail + " } } }"),
                DiagnosticCode.BuiltInPropertyRedeclared);

        [Test]
        public void UnknownStateField() =>
            AssertErrors(Load(State("'type': 'boolean', 'default': false, 'deafult': true, " + Tail)), DiagnosticCode.UnknownField);

        [Test]
        public void StateTypeRequired() => AssertErrors(Load(State("'default': false, " + Tail)), DiagnosticCode.MissingRequiredField);

        [TestCase("bool")]
        [TestCase("float")]
        [TestCase("Boolean")]
        public void InvalidStateType(string type) =>
            AssertErrors(Load(State("'type': '" + type + "', 'default': false, " + Tail)), DiagnosticCode.InvalidStateType);

        [Test]
        public void ReconstructibleIsNotSupported() =>
            AssertErrors(
                Load(State("'type': 'boolean', 'default': false, 'persistence': 'reconstructible', 'mutation_policy': 'scenario_only'")),
                DiagnosticCode.InvalidPersistence);

        [Test]
        public void InvalidMutationPolicy() =>
            AssertErrors(
                Load(State("'type': 'boolean', 'default': false, 'persistence': 'persistent', 'mutation_policy': 'anyone'")),
                DiagnosticCode.InvalidMutationPolicy);

        [Test]
        public void DefaultRequired() => AssertErrors(Load(State("'type': 'boolean', " + Tail)), DiagnosticCode.MissingRequiredField);

        [TestCase("'type': 'boolean', 'default': 0")]
        [TestCase("'type': 'boolean', 'default': 'false'")]
        [TestCase("'type': 'integer', 'default': '1'")]
        [TestCase("'type': 'integer', 'default': 1.5")]
        [TestCase("'type': 'integer', 'default': true")]
        [TestCase("'type': 'string', 'default': 5")]
        [TestCase("'type': 'string', 'default': null")]
        [TestCase("'type': 'enum', 'values': ['a'], 'default': 1")]
        public void DefaultMustMatchType(string body) =>
            AssertErrors(Load(State(body + ", " + Tail)), DiagnosticCode.InvalidDefaultValue);

        [TestCase("'min': 0, 'max': 3, 'default': 4")]
        [TestCase("'min': 0, 'default': -1")]
        [TestCase("'max': 10, 'default': 11")]
        public void DefaultMustBeInRange(string body) =>
            AssertErrors(Load(State("'type': 'integer', " + body + ", " + Tail)), DiagnosticCode.InvalidDefaultValue);

        [Test]
        public void MinGreaterThanMax() =>
            AssertErrors(Load(State("'type': 'integer', 'min': 5, 'max': 1, 'default': 3, " + Tail)), DiagnosticCode.InvalidRange);

        [TestCase("'type': 'boolean', 'min': 0, 'default': false")]
        [TestCase("'type': 'enum', 'values': ['a'], 'max': 3, 'default': 'a'")]
        [TestCase("'type': 'integer', 'values': ['a'], 'default': 1")]
        [TestCase("'type': 'string', 'values': ['a'], 'default': 'a'")]
        public void FieldNotApplicableToType(string body) =>
            AssertErrors(Load(State(body + ", " + Tail)), DiagnosticCode.FieldNotApplicable);

        [Test]
        public void EnumNeedsValues() =>
            AssertErrors(Load(State("'type': 'enum', 'default': 'a', " + Tail)), DiagnosticCode.MissingRequiredField);

        [Test]
        public void EnumValuesMustNotBeEmpty() =>
            AssertErrors(Load(State("'type': 'enum', 'values': [], 'default': 'a', " + Tail)), DiagnosticCode.EmptyEnumValues);

        [Test]
        public void EnumValuesMustBeUnique() =>
            AssertErrors(Load(State("'type': 'enum', 'values': ['a', 'a'], 'default': 'a', " + Tail)), DiagnosticCode.DuplicateEnumValue);

        [TestCase("Calm")]
        [TestCase("1st")]
        [TestCase("a-b")]
        [TestCase("place.x")]
        public void EnumValueFormat(string value) =>
            AssertErrors(Load(State("'type': 'enum', 'values': ['ok', '" + value + "'], 'default': 'ok', " + Tail)), DiagnosticCode.InvalidEnumValue);

        [Test]
        public void EnumDefaultMustBeListed() =>
            AssertErrors(Load(State("'type': 'enum', 'values': ['a', 'b'], 'default': 'c', " + Tail)), DiagnosticCode.InvalidDefaultValue);

        [Test]
        public void EnumReferenceValueMustExist() =>
            AssertErrors(
                Load(State("'type': 'enum', 'values': ['location.office', 'location.roof'], 'default': 'location.office', " + Tail)),
                DiagnosticCode.UnknownReference);

        // ---------- registry ----------

        [Test]
        public void RegistryLookups()
        {
            var registry = LoadValid("{ 'id': 'story.b' }", "{ 'id': 'story.a' }");
            var storyA = EntityId.Parse("story.a");

            Assert.That(registry.Exists(storyA), Is.True);
            Assert.That(registry.Exists(EntityId.Parse("story.c")), Is.False);
            Assert.That(registry.TryGet<StoryDefinition>(storyA, out _), Is.True);
            Assert.That(registry.TryGet<ItemDefinition>(storyA, out _), Is.False);
            Assert.Throws<KeyNotFoundException>(() => registry.Get<StoryDefinition>(EntityId.Parse("story.c")));
            Assert.That(registry.GetByKind(EntityKind.Story).Select(d => d.Id.Value), Is.EqualTo(new[] { "story.a", "story.b" }));
            Assert.That(registry.GetByKind(EntityKind.Event), Is.Empty);
        }

        [Test]
        public void StatesAreSortedByAddress()
        {
            var registry = LoadValid(Story(
                "{ 'z': { 'type': 'boolean', 'default': false, " + Tail + " }, 'a': { 'type': 'boolean', 'default': true, " + Tail + " } }"));

            var addresses = registry.States.Select(s => s.Address.ToString()).ToArray();
            Assert.That(addresses, Is.EqualTo(addresses.OrderBy(a => a, StringComparer.Ordinal).ToArray()));
            Assert.That(addresses, Does.Contain("story.test.a").And.Contain("story.test.z").And.Contain("character.paranoik.location"));
        }

        [Test]
        public void RegistryCollectionsAreReadOnly()
        {
            var registry = LoadValid();

            Assert.Throws<NotSupportedException>(() => ((IList<Definition>)registry.All).Add(null));
            Assert.Throws<NotSupportedException>(() => ((IList<StateDefinition>)registry.States).Clear());
            var hero = registry.Get<CharacterDefinition>(EntityId.Parse("character.paranoik"));
            Assert.Throws<NotSupportedException>(() => ((IList<StateDefinition>)hero.States).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<string>)hero.LocationState.EnumValues).Add("x"));
        }

        [Test]
        public void NoRegistryWhenAnyError()
        {
            var result = Load("{ 'id': 'story.ok' }", "{ 'id': 'story.bad', 'x': 1 }");
            Assert.That(result.Registry, Is.Null);
            Assert.That(result.Succeeded, Is.False);
        }
    }
}
