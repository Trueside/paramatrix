# Параматрица

2D-игра (вид сбоку) на Unity 6000.4, Windows 10+.

## Документы

- Архитектура: `A_0_3.md`, `A_0_3_1.md`, `A_0_3_2.md`, `A_0_4.md`, `A_0_4_Semantic_Addendum.md`.
- План реализации: `IP_0_2_Implementation_Plan.md` (действующий). Решения пользователя: `IP_0_1_Decisions.md`.
- Графика: визуальный стандарт `Paramatrix_Visual_Production_Master_Standard_0_3_RU.md`; правила и промпты генерации (персонажи, эмоции, новелла, фоны, предметы) — `Paramatrix_Generation_Prompts_RU.md`; масштаб и сборка сцен из кусков — `Paramatrix_Scene_Kit_0_1_RU.md`.
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

Сцена Bootstrap, спрайт-заглушка и `ContentManifest` генерируются кодом (`Game/Assets/Paramatrix/Editor/DemoSceneBuilder.cs`), Bootstrap руками не править. Сцены локаций собираются руками в редакторе (`Paramatrix_Scene_Kit_0_1_RU.md`): сборщик их не трогает, демо-локацию создаёт кодом, только если её файла нет; «Paramatrix → Rebuild Demo Locations (overwrite)» стирает ручную сборку. Build Settings — Bootstrap и все `Location_*.unity`. Меню «Paramatrix → Build Scenes» или пакетно:

```bash
unity run Game --timeout 900 -- -executeMethod Paramatrix.Unity.Editor.DemoSceneBuilder.BuildAll
```

Перегенерировать демо-локации кодом (стирает ручную сборку) — `-executeMethod Paramatrix.Unity.Editor.DemoSceneBuilder.RebuildDemoLocations`.

Новый JSON в `Game/Assets/Paramatrix/Content/` попадает в игру только после «Paramatrix → Collect Content» (или BuildAll). Тест `ProjectContentIsValid` проверяет весь реальный контент.

Проверка проекта целиком (контент + сцены: манифест, `LocationRoot`, точки входа, ID представлений, клипы анимаций, где должны быть видны персонажи и предметы) — меню «Paramatrix → Validate Project» или пакетно (код выхода 1 при ошибках):

```bash
unity run Game --timeout 900 -- -executeMethod Paramatrix.Unity.Editor.ProjectValidator.ValidateBatch
```

Сборка игры под Windows (64-бит, Mono) — по Build Profile `Assets/Settings/Build Profiles/Windows.asset` (настройки сборки — в нём): меню «Paramatrix → Build Windows» (и «… (Development)») или пакетно; сначала проходит Validate Project, при ошибках сборка не делается; папка `Game/Builds/Windows` очищается, результат — `Paramatrix.exe` в ней (папка не в git):

```bash
unity run Game --timeout 1800 -- -executeMethod Paramatrix.Unity.Editor.WindowsBuild.BuildBatch
```

Правила устройства сцены живут в одном месте — `SceneStructure` (им пользуются и загрузчик сцен в игре, и проверка в редакторе). Тесты редактора — `Game/Assets/Paramatrix/Tests/Editor` (только Unity, в `dotnet test` не входят).

## Unity-слой

- В Unity 6 есть свой тип `UnityEngine.EntityId`: в файлах с `using UnityEngine;` нужен `using EntityId = Paramatrix.Content.EntityId;`.
- NUnit в Unity — 3.5: сообщение в `Assert.That` только строкой, не массивом.
- Ресурсы TextMesh Pro (`Game/Assets/TextMesh Pro`) лежат в репозитории. `AssetDatabase.ImportPackage` в пакетном режиме не успевает выполниться до выхода Unity; если ресурсы понадобится обновить — меню «Paramatrix → Import TMP Essentials» в открытом редакторе.
- Шрифт текста — системный Segoe UI через динамический TMP-ассет (`UiFonts`): в проект шрифт не копируется. Финальный шрифт — решение по оформлению.
- Мировые часы: разговор (обычный режим) ставит `Time.timeScale = 0` (`WorldPause`), всё мировое движение — на scaled time, UI и Scenario от времени не зависят.
- Диалоги (Р10 в `IP_0_1_Decisions.md`): монолог — облако над говорящим без аватара, управление у игрока, гаснет через 7 с (`DialogueView.MonologueSeconds` — будущая настройка), ПКМ по пустому месту переводит мысль героя к следующей заготовленной фразе или закрывает её; обычный режим (`"mode": "normal"`) — облако и аватар рядом на линии разговора локации (`LocationRoot.dialogueLine`), промежуток между ними над говорящим: облако со стороны взгляда, аватар за головой, текст печатается по буквам. Выборы (Р12): шаг `{ "choice": [...] }`, варианты «1. [Да]» в облаке героя (обычный режим) или в панели новеллы, ответ — цифрой 1–9 или кликом (TMP-ссылки, `DialogueView.ChoiceAt`); проход по веткам — `DialogueSystem` (`Choose`, вариант продолжает, `end` или `next` в другой диалог). Новелла (`"mode": "novel"`, `partner`) — `NovelScreen`: фон локации без фигур, снятый мировой камерой и размытый (`Dream`), изображение собеседника по эмоции (`Art/Characters/<имя>/novel/<эмоция>.png`, 630×880, в манифесте рядом с аватарами), панель с именем; мир на паузе; собеседник выходит из тени при входе и уходит в тень при выходе; при смене эмоции новая картинка плавно проявляется поверх старой (`NovelScreen.CrossfadeSeconds`, 0,5 с) (шаг анимации за кадр не больше 1/30 с: долгий кадр снятия фона её не съедает). Облака и аватар строит в коде `DialogueView`, рисунок облаков — `BubbleArt` (9-slice; под облаком речи — хвостик, под мыслью героя — след из двух кружков). Монологи ставятся после камеры (`DefaultExecutionOrder(1100)` > `WorldCamera` 1000) над точкой ног говорящего: по границам спрайта нельзя — кадры ходьбы и разворот их меняют.
- Аватары: `Art/Characters/<имя>/emotions/<эмоция>.png` для `character.<имя>`, картинки в экранных пикселях (не на сетке мира: 1 пиксель картинки = 1 пиксель Full HD, импорт со сглаживанием и mipmaps — `PixelArtImport.IsAvatar`), все ровно 115×135 (`DialogueView.AvatarWidth/AvatarHeight`: половина стандартного выреза карточки 229×270; другой размер или кадрирование — ошибка скрипта и Validate Project), смотрят вправо (по взгляду фигуры отзеркаливаются в игре). Подгонка исходников (Ланцош в линейном свете; источник — `avatars.source` в `tools/art/characters.json`, файлы `<эмоция>.png` прямо в папке) — `tools/art/prepare-avatars.ps1`, затем «Collect Content» (аватары — в `ContentManifest`). Словарь эмоций (английское имя файла ↔ русское название) — `Content/emotions.json`; персонаж перечисляет свои эмоции из словаря в контенте (`"emotions"`); Validate Project сверяет их с картинками. У Параноика `default` — настоящий аватар, у остальных — заглушки, вырезанные из фигур (подогнаны `-Pad`).
- Пиксельная сетка мира — `PixelGrid` (960×540, 48 пикселей на единицу, в Full HD ×2; стандарт `Paramatrix_Visual_Production_Master_Standard_0_3_RU.md`, §6). Мир рисует `PixelPerfectCamera` (Upscale Render Texture + Stretch Fill + RetroAA: целый множитель, мягкий остаток, поля). Текстуры в `Assets/Paramatrix/Art/` импортируются по правилу `PixelArtImport`; валидатор проверяет камеру, спрайты всех сцен и отсутствие 2D-света. Размеры в `DemoSceneBuilder` — в пикселях сетки.
- Камера мира — `WorldCamera`: `LocationRoot` задаёт видимые края локации (`viewMinX/viewMaxX`). Шириной в один экран — неподвижный экран, шире — герой толкает экран: в середине мёртвая зона, камера стоит и сдвигается, только чтобы держать героя не ближе `WorldCamera.PushMargin` (360 пикселей сетки, 3/8 экрана) от края; за края локации не заходит. Дальние и ближние планы — `ParallaxLayer` (доля движения камеры: 0 — плоскость пола, 0..1 — фон, < 0 — передний план), на них нет кликабельных сущностей. Высота линии пола над низом экрана — у каждой локации своя (`LocationRoot.floorDepth`, по нарисованному полу задника); камера ставит её по ней. Демо-мир — 10 локаций на сгенерированных задниках (офис, комната Морфеуса, бар, мастерская, пять улиц); двери нарисованы в задниках, выходы — невидимые зоны клика, по улице — зоны у краёв. Карта переходов — в `Content/Demo/city.json` и `office.json`, проходит её `WorldMapPlayTests`. Линия разговора (`dialogueLine`) — одна высота во всех локациях (`DemoSceneBuilder.DialogueLine`, 282); правится у отдельной локации руками, если её картинке нужно (бар — 300: над головой Красавицы).
- Куда смотрит фигура — `Walker` (`Facing`). Все спрайты фигур нарисованы лицом вправо: исходник, нарисованный влево, отзеркаливается при подготовке спрайта. Точка входа обязана задавать взгляд героя (`EntryPoint.facing`, спиной к двери). Взаимодействие (Р11): по клику герой никуда не идёт; ЛКМ срабатывает только в радиусе героя (`HeroController.InteractionRadius`, 108 пикселей сетки до ближнего края нарисованной цели), иначе ничего; ПКМ — с любого расстояния. Сцена останавливает героя на месте (`Walker.Halt`) и поворачивает его к цели, персонаж — к нему. Точка подхода представления — только для действия сцены `move`. PlayMode-тесты ставят героя к цели `PlayReach.StandBy`.
- Задник (непрозрачная картинка из генератора) уменьшается до размера в сетке `tools/art/reduce-backdrop.ps1 -Width 960 -Height 540` (один коэффициент, цвета только из исходника); фигуры и слои с прозрачностью — `tools/art/reduce-to-grid.ps1`. Стоящие персонажи делаются из чистых вырезов `tools/art/prepare-characters.ps1` по списку `tools/art/characters.json`: свой рост у персонажа, если задан, иначе общий `default_height`; `"method": "nearest"` — ближайшие соседи (лицо из подробного исходника чёткое, но с рябью); `"steps"` — уменьшение по шагам, у каждого свой метод и высота (`bilinear`/`lanczos`/`area` в линейном свете, последним `nearest` с целым делением: плавные шаги доводят фигуру ровно до 2× или 3× итогового роста); `"sharpen"`, `"colors"`, `"despeckle"` — резкость исходника, сведение палитры и чистка одиночных пикселей (резкость усиливает кайму выреза с зелёного, палитра на всей фигуре съедает цвета лица); `"stepwise"` — уменьшение во много мягких шагов `tools/art/reduce-stepwise.ps1` (ни один шаг не уменьшает больше чем на `max_step`, число шагов вычисляется, каждый шаг — в целые пиксели, последний — `last`); выбранный вариант (D) — `{ "max_step": 0.05, "pattern": "lanczos", "last": "sharp", "space": "srgb" }` у Красавицы (рост 196, источник — чистый вырез `_img/beautiful_girl/beautiful_girl_3q_key.png`) и Фем Параноики; `sharp` — бикубический Catmull-Rom, приближение «Адаптивного (чёткого)» paint.net, чей алгоритм не опубликован; `-Before` сохраняет картинку перед последним шагом, чтобы сделать его руками; чистый вырез с зелёного без каймы — `tools/art/key-green.ps1`; `"final"` — спрайт, доведённый руками (например, лицо), берётся как есть и не перегенерируется; тест `CharacterSizesTests` сверяет спрайты со списком.
- Физики в игре нет. Клик по объекту — попадание в границы спрайтов представления (`EntityView.Contains`), без коллайдеров. Модуль `physics2d` удалён; модули `physics` и `terrain` остаются только как зависимости URP, авто-симуляция 3D-физики выключена (`DynamicsManager`).
- Частота кадров — ровно 60 (`FramePacing`, вызывается в `GameBootstrap.Awake`): на мониторе 60/120/240 Гц — vSync каждый n-й кадр, на остальных (144 Гц) — vSync выключен и ограничитель 60. Движение (`Walker`) идёт тактами 60 Гц в целых пикселях сетки (на ходу 3 px за такт), с плавным разгоном (`Walker.StartTicks`, 0,4 с) и коротким торможением (`StopTicks`, 0,15 с). `PlayFromBootstrap` при выходе из Play возвращает vSync и ограничитель как были.
- В `Packages/manifest.json` — только то, чем проект пользуется: `audio` и `animation` оставлены под звук и анимации из Architecture 0.3. Новый пакет или модуль — по согласию пользователя.

## Правила кода

- Тесты — NUnit, совместимый с NUnit 3.5 из Unity Test Framework (`Assert.That`, `TestCase`, `Assert.Throws`).
- Без статического изменяемого состояния, без `async/await` и корутин в логике, однопоточно.
- Ошибки контента и исполнения — `Diagnostic` с кодом `DiagnosticCode`; тесты проверяют коды, а не тексты.
- Поведение не зависит от порядка перебора `Dictionary`/`HashSet`: только отсортированные коллекции.
