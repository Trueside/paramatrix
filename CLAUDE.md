# Параматрица

2D-игра (вид сбоку) на Unity 6000.4, Windows 10+.

## Документы

- Архитектура: `A_0_3.md`, `A_0_3_1.md`, `A_0_3_2.md`, `A_0_4.md`, `A_0_4_Semantic_Addendum.md`.
- План реализации: `IP_0_2_Implementation_Plan.md` (действующий). Решения пользователя: `IP_0_1_Decisions.md`.
- Сквозной принцип (Р0): всё, что не работает, — явная ошибка; никаких пропусков, повторов и «умолчаний» вместо ошибки.

## Устройство

- `Game/` — Unity-проект. Код: `Game/Assets/Paramatrix/Runtime/<Сборка>/`, тесты: `Game/Assets/Paramatrix/Tests/EditMode/`.
- Сборки логики (`Paramatrix.Content`, далее State, Conditions, Scenario, Gameplay) — asmdef с `noEngineReferences: true`, без UnityEngine.
- `tools/` — консольная сборка тех же исходников: один csproj на один asmdef (границы сборок совпадают с Unity), `LangVersion 9`, `netstandard2.1`, предупреждения = ошибки.
- Новый asmdef логики → добавить зеркальный csproj в `tools/` и ссылку в `tools/Paramatrix.Tests/Paramatrix.Tests.csproj`, а сборку — в `references` у `Paramatrix.Tests.asmdef`.

## Команды

Быстрые тесты (без Unity):

```bash
dotnet test tools/Paramatrix.Tests/Paramatrix.Tests.csproj
```

Те же тесты в Unity (EditMode, пакетный режим; не работает, если проект открыт в Editor):

```bash
unity test Game --mode EditMode --output <путь>/editmode.xml --timeout 1200
```

PlayMode-тесты (настоящие сцены, Bootstrap → офис → коридор):

```bash
unity test Game --mode PlayMode --output <путь>/playmode.xml --timeout 1200
```

Сцены, спрайт-заглушка и `ContentManifest` генерируются кодом (`Game/Assets/Paramatrix/Editor/DemoSceneBuilder.cs`), руками их не править — перезапускать сборщик (меню «Paramatrix → Build Demo Scenes» или пакетно):

```bash
unity run Game --timeout 900 -- -executeMethod Paramatrix.Unity.Editor.DemoSceneBuilder.BuildAll
```

Новый JSON в `Game/Assets/Paramatrix/Content/` попадает в игру только после «Paramatrix → Collect Content» (или BuildAll). Тест `ProjectContentIsValid` проверяет весь реальный контент.

## Unity-слой

- В Unity 6 есть свой тип `UnityEngine.EntityId`: в файлах с `using UnityEngine;` нужен `using EntityId = Paramatrix.Content.EntityId;`.
- NUnit в Unity — 3.5: сообщение в `Assert.That` только строкой, не массивом.
- Ресурсы TextMesh Pro (`Game/Assets/TextMesh Pro`) лежат в репозитории. `AssetDatabase.ImportPackage` в пакетном режиме не успевает выполниться до выхода Unity; если ресурсы понадобится обновить — меню «Paramatrix → Import TMP Essentials» в открытом редакторе.
- Шрифт текста — системный Segoe UI через динамический TMP-ассет (`UiFonts`): в проект шрифт не копируется. Финальный шрифт — решение по оформлению.
- Мировые часы: VN-диалог ставит `Time.timeScale = 0` (`WorldPause`), всё мировое движение — на scaled time, UI и Scenario от времени не зависят.

## Правила кода

- Тесты — NUnit, совместимый с NUnit 3.5 из Unity Test Framework (`Assert.That`, `TestCase`, `Assert.Throws`).
- Без статического изменяемого состояния, без `async/await` и корутин в логике, однопоточно.
- Ошибки контента и исполнения — `Diagnostic` с кодом `DiagnosticCode`; тесты проверяют коды, а не тексты.
- Поведение не зависит от порядка перебора `Dictionary`/`HashSet`: только отсортированные коллекции.
