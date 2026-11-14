# Параматрица — Implementation Plan 0.2

**Статус:** проект на ревью, реализация не начата  
**Заменяет:** Implementation Plan 0.1  
**Основано на:** Architecture 0.3 (`A_0_3.md`), 0.3.1, 0.3.2, 0.4, 0.4 Semantic Addendum  
**Решения пользователя:** `IP_0_1_Decisions.md` (Р0–Р9) — далее ссылки вида «Р3»

Платформа — Windows 10+. Вид — 2D сбоку. Unity 6000.4.

Сквозной принцип **Р0**: всё, что не работает, — явная ошибка. Никаких пропусков, повторов и поведения «по умолчанию» вместо ошибки.

---

# Часть I. Решения

## I.1. Согласованные решения (кратко)

| № | Решение |
|---|---|
| Р0 | Явная ошибка вместо подстраховки |
| Р1 | Условия проверяют только факты в State; сцены ставят факты явно; факт — у сущности, к которой относится по смыслу; постоянные факты не удаляются. История сцен — внутреннее состояние Scenario, свой снимок в Save |
| Р2 | New Game: после инициализации всех систем — обычный вход в стартовую локацию. Load: техническая загрузка, без `LocationEntered` и нового Visit |
| Р3 | Failed-сцена заблокирована до загрузки сохранения; статус Failed не сохраняется |
| Р4 | CALL/START недоступной сцены (Running, Failed, Repeat Policy, Launch Conditions = False) — Failure вызывающей |
| Р5 | Newtonsoft.Json разрешён; только Windows |
| Р6 | Простые реакции — `response.*`, уровни specific → contextual → generic → fallback; сцены приоритетнее реакций |
| Р7 | Взять предмет и выйти из локации — только сценой |
| Р8 | 2D, вид сбоку |
| Р9 | После Load герой — на точке входа локации по умолчанию |
| A5 | Exclusive → START Exclusive: в очередь |
| A6 | Background-сцена мгновенна, ничего не ждёт |
| A7/A8 | Асинхронные Actions всегда ждут завершения; конец списка = успешное завершение |
| A9 | `reconstructible` убран |
| A10 | Действие игрока: две подходящие сцены — Error. Автоматический trigger: кандидаты в порядке Event ID |
| A15 | Save не совпадает с контентом — Load отклоняется целиком |
| A16 | Ссылка на сущность в State — Enum со значениями-ID |

## I.2. Технические решения (на ревью)

Из плана 0.1 остаются в силе T1–T11 (обработка уведомлений между Actions, мониторы, предохранитель каскадов, Visit как счётчик, очередь без дублей, Save только в покое, аварийное прерывание, Enum только `equals`/`not_equals`, публичные источники мутаций `Scenario`/`System`, перепроверка условий через `if`, сигнал `Restored`). Изменение: T2, T3 — с Error вместо Warning (Р0).

*Уточнение T1 при реализации этапа 6.* Уведомление State System только кладётся во входящую очередь Scenario и никогда не запускает `Pump` само. Изменение State, сделанное вне Scenario (игровой системой), обрабатывается следующим `Pump` — Unity вызывает его каждый кадр; `Submit`, `CompleteOperation` и `BeginLocationVisit` запускают `Pump` сразу. Причина: запуск `Pump` из обработчика уведомления происходит, пока State System ещё рассылает это уведомление, и следующие уведомления откладываются до конца `Pump`. Одна цепочка реакций распадается на множество отдельных `Pump`, и предохранитель каскада (T3) перестаёт работать — это обнаружил тест бесконечной цепочки двух фоновых сцен.

Новые, по Architecture 0.3. Каждое не меняет согласованную семантику, но влияет на контент — возражения до начала этапа, в котором решение реализуется.

**T12. Что значит левый клик по объекту.** У каждой интерактивной сущности в JSON поле `primary_interaction` — тип взаимодействия для левого клика: `talk` у персонажа, `take` у предмета, `exit` у двери-выхода, `use` у устройства, `primary` в остальных случаях. Правый клик — всегда `inspect`.
*Пример:* левый клик по Тринити → `talk`; по ключу на столе → `take`; по двери коридора → `exit`.

**T13. Подход к объекту.** ~~Перед взаимодействием герой идёт к точке подхода.~~ Отменено решением Р11 (`IP_0_1_Decisions.md`): игрок водит героя сам, ЛКМ срабатывает только в радиусе героя, ПКМ — с любого расстояния; точка подхода — только для действия `move`.
*Пример:* клик по двери в другом конце офиса → герой идёт → дошёл → сцена «выход». Нажал «влево» по дороге → герой остановился, ничего не произошло.

**T14. Встроенные свойства «где находится».**
- У каждого персонажа — `location`: ID локации или `none`. Начальное значение — обязательное поле `initial_location`. Героя перемещает Location System при переходе; NPC — сцена через `set_state`. Представление персонажа в сцене видно только при `location` = эта локация.
- У каждого предмета — `holder`: ID локации (лежит там в мире) | `inventory` | `consumed` | ID персонажа (у NPC). Начальное значение — обязательное поле `initial_holder`. Меняет только Inventory System по команде сцены.
- Это уточнение модели 0.3 §4.2: значение `world` заменено на ID локации, потому что «где в мире» — тоже логический факт (иначе после перехода нельзя показать предмет в нужной сцене). Одно свойство вместо двух («место» + «NPC-владелец») — изменение атомарно, промежуточных несогласованных состояний нет.
*Пример условия:* `item.key.holder == inventory`; `character.trinity.location == location.office`.

**T15. Один trigger для всех действий игрока.** `Interaction` с полями `interaction` (тип из 0.3 §5.2), `target`, `item`. Trigger types `ItemUsed` и `ItemCombined` из 0.4 не вводятся — они дублируют `Interaction` с `use` / `apply_item` / `combine_items`. `ItemAcquired` остаётся: его публикует Inventory System, когда предмет попадает в инвентарь. Для `combine_items` пара предметов сравнивается без учёта порядка.
*Пример:* применить ключ к двери = `Interaction(apply_item, target=object.office_door, item=item.key)`.

**T16. Модель Game Flow.** Три независимых измерения вместо одного списка режимов (0.3 §1.2 сам называет Exclusive Event и Dialogue независимыми):
- владелец управления: `Player` | `Scenario` | `System`;
- показ диалога: нет | Minimalistic | VN;
- идёт переход между локациями: да | нет.

Режимы 0.3 — их сочетания: Free Roam = Player; Exclusive Event = Scenario; Transition = переход идёт; Loading/Recovery = System. Разрешения игрока (двигаться, взаимодействовать, инвентарь, меню сохранения) вычисляются из этих измерений; глобального `isInputLocked` нет. VN-диалог ставит мир на паузу, Minimalistic — нет.

**T17. Показ реакции.** Выбранная реакция показывается как однострочный Minimalistic-диалог. На время показа Scenario берёт Exclusive control, как для сцены; у реакции нет Repeat Policy и статуса.

**T18. Порядок предметов в инвентаре** — порядок объявления в контенте (файлы — по пути, внутри — по позиции). Порядок получения не хранится: это не логический факт, а сохранять его ради UI — лишнее свойство. Если важен порядок получения — скажите, это одно integer-свойство.

**T19. Движение в 2D сбоку.** Герой ходит влево-вправо по линии пола локации; границы задаёт сцена. Препятствий и поиска пути в MVP нет, поэтому подход к объекту не может «застрять» — Movement не возвращает Failure от игровых помех (вывод из Р3). Глубина сцены — после MVP.

**T20. Показ ошибок.** Все Error-диагностики времени выполнения — в Unity Console и в оверлей поверх игры (в Editor и Development Build). Для релизной сборки — отдельное решение позже.

**T21. Save.** JSON-файл в `%USERPROFILE%\AppData\LocalLow\<company>\<product>\saves\<slot>.json` (`Application.persistentDataPath`). Содержит `format_version`, снимок State, снимок Scenario. Проверка при Load — по содержимому (Р0, A15), а не по хешу контента: правка текста реплики не ломает сохранения, а удаление свойства — ломает. Сохранять можно только при владельце `Player` и `ScenarioRuntime.CanSave`. Слоты и автосохранение — игровой дизайн, API это поддерживает, UI — после MVP.

**T22. Unity-технологии.** URP с 2D Renderer (шаблон Unity 2D), Input System (0.3 §5.3), uGUI + TextMeshPro для всего UI, включая реплики над персонажами (world-space canvas). Пакеты: `com.unity.inputsystem`, `com.unity.nuget.newtonsoft-json`, `com.unity.test-framework`; URP и 2D — из шаблона.

## I.3. Позиция героя после загрузки (Р9)

После Load герой появляется на точке входа локации по умолчанию. Позиция героя не сохраняется. *Пример:* сохранился у окна офиса → после загрузки стоит у двери. При необходимости позже можно добавить свойство позиции без переделки остального.

Открытых вопросов нет.

---

# Часть II. План

## II.1. Сборки

```text
Paramatrix.Content      ID, значения, definitions, diagnostics, парсер, Validator, Registry
    ↑
Paramatrix.State        StateSystem
    ↑
Paramatrix.Conditions   ConditionEvaluator (видит только IStateReader)
    ↑
Paramatrix.Scenario     ScenarioRuntime, реакции, порты систем
    ↑
Paramatrix.Gameplay     GameFlow, Inventory, Location, Interaction, Save — логика без Unity
    ↑
Paramatrix.Unity        bootstrap, загрузка сцен, ввод, представления, движение, UI, файлы
Paramatrix.Unity.Editor валидация контента и сцен, drawer для ID
```

- Все сборки, кроме `Paramatrix.Unity*`, — `noEngineReferences: true`. Логика всех систем, включая Game Flow, Inventory, Location и Interaction, тестируется без Unity.
- Те же исходники собираются в `tools/Paramatrix.Tests.csproj` (`netstandard2.1`, `LangVersion 9`) для `dotnet test`; в Unity — через Test Framework (EditMode).
- Однопоточно, без `async/await` и корутин в логике, без статического изменяемого состояния, перебор, влияющий на поведение, — только по отсортированным коллекциям.
- Ошибки контента и исполнения — `Diagnostic`; исключение — только нарушение контракта кодом.

## II.2. Контент

### Виды сущностей

| Префикс | Что | State | Обязательные поля |
|---|---|---|---|
| `game` | настройки игры (ровно одна: `game.settings`) | нет | `player_character` |
| `character` | персонаж | да + встроенное `location` | `name`, `initial_location`, `primary_interaction` |
| `item` | предмет | да + встроенное `holder` | `name`, `description`, `initial_holder` |
| `object` | интерактивный объект | да | `primary_interaction` |
| `location` | локация | да | `scene` (имя Unity-сцены) |
| `story` | сюжетная веха | да | — |
| `condition` | именованное условие | нет | `condition` |
| `event` | сцена | нет | `execution`, `repeat`, `triggers`, `actions` |
| `dialogue` | разговор (обычный режим, Р10) | нет | `mode`, `lines` |
| `response` | реакция | нет | `tier`, `interaction`, текст или `none` |

Тип сущности — из префикса ID, отдельного поля нет. Стартовая локация = `initial_location` героя.

### Файл и примеры

```json
{ "format_version": 1, "definitions": [ ... ] }
```

```json
{ "id": "game.settings", "player_character": "character.paranoik" }

{ "id": "character.paranoik", "name": "Параноик",
  "initial_location": "location.office", "primary_interaction": "talk",
  "states": {
    "met_morpheus": { "type": "boolean", "default": false, "persistence": "persistent", "mutation_policy": "scenario_only" }
  } }

{ "id": "item.key", "name": "Ключ", "description": "Маленький латунный ключ.",
  "initial_holder": "location.office" }

{ "id": "object.office_door", "primary_interaction": "exit",
  "states": {
    "locked": { "type": "boolean", "default": true, "persistence": "persistent", "mutation_policy": "scenario_only" }
  } }

{ "id": "location.office", "scene": "Location_Office" }

{ "id": "condition.door_is_locked",
  "condition": { "target": "object.office_door", "property": "locked", "operator": "equals", "value": true } }

{ "id": "dialogue.morpheus_intro", "mode": "normal",
  "lines": [
    { "speaker": "character.morpheus", "text": "Я ждал тебя." },
    { "speaker": "character.paranoik", "text": "Кто вы?", "emotion": "smirk" }
  ] }

{ "id": "event.office_exit", "execution": "exclusive", "repeat": "repeatable",
  "triggers": [ { "type": "interaction", "interaction": "exit", "target": "object.office_door" } ],
  "launch_condition": { "not": { "ref": "condition.door_is_locked" } },
  "actions": [
    { "action": "animation", "target": "character.paranoik", "clip": "open_door" },
    { "action": "transition", "location": "location.corridor", "entry": "from_office" }
  ] }

{ "id": "event.take_key", "execution": "exclusive", "repeat": "once",
  "triggers": [ { "type": "interaction", "interaction": "take", "target": "item.key" } ],
  "actions": [
    { "action": "animation", "target": "character.paranoik", "clip": "bend_down" },
    { "action": "inventory", "item": "item.key", "to": "inventory" }
  ] }

{ "id": "response.door_locked", "tier": "specific", "interaction": "exit", "target": "object.office_door",
  "condition": { "ref": "condition.door_is_locked" }, "text": "Заперто." }

{ "id": "response.generic_primary_1", "tier": "generic", "interaction": "primary", "text": "Хм." }
{ "id": "response.generic_primary_2", "tier": "generic", "interaction": "primary", "text": "Не поддаётся." }

{ "id": "response.fallback_take", "tier": "fallback", "interaction": "take", "text": "Не могу это взять." }
{ "id": "response.fallback_talk", "tier": "fallback", "interaction": "talk", "none": true }
```

Condition-узел — ровно одна форма: atomic (`target`, `property`, `operator`, `value`), `all`, `any`, `not`, `ref`.

Actions:

| `action` | Поля | Ждёт | В Background |
|---|---|---|---|
| `set_state` | `target`, `property`, `value` | нет | да |
| `if` | `condition`, `then`, `else` | нет | да |
| `end` | — | нет | да |
| `call` | `event` | завершения сцены | только Background-цель |
| `start` | `event` | нет | только Background-цель |
| `inventory` | `item`, `to` (`inventory`/`consumed`/ID локации/ID персонажа) | нет | да |
| `clear_selection` | — | нет | нет |
| `dialogue` | `dialogue` | завершения | нет |
| `move` | `character`, `to` (ID сущности — к её точке подхода) | прибытия | нет |
| `animation` | `target`, `clip` | конца клипа | нет |
| `transition` | `location`, `entry` (точка входа; без него — точка по умолчанию) | конца перехода | нет |

Trigger types: `interaction`, `item_acquired`, `state_changed`, `location_entered`, `dialogue_completed`, `event_completed`, `conditions_met`.

Реакция (`response`): `tier`; `interaction`; `target` (обязателен для `specific`, необязателен для `generic` — свой пул заглушек цели, запрещён для остальных); `condition` (необязателен; у `fallback` запрещён); `item` (для `apply_item`/`combine_items`); `speaker` (по умолчанию — герой; для `talk` по умолчанию — цель); ровно одно из: `text` (говорится каждый раз), `lines` (заготовленный монолог, только `specific`/`contextual`: по фразе за действие, каждая один раз, прогресс в сохранении), `none: true` (только у `fallback`). Реакция показывается монологом (Р10).

Эмоция словаря (`emotion.<имя>`): `name` — русское название; имя ID — имя файла аватара. Персонаж: `emotions` — эмоции аватаров (необязательно; если есть — уникальные, из словаря, среди них `default`). Реплика разговора: `emotion` (по умолчанию `default`; говорящий обязан объявить `emotions`). Действие `say`: `speaker`, `text` — монолог из сцены, сцена его не ждёт (в background запрещено).

### Validator

Всё из плана 0.1 (структура, ID, State, Condition, Event, Actions, execution type), ужесточённое по Р0: START-цикл — Error. Плюс:

*Исправление при реализации этапа 5.* Из группы «Конфликты» плана 0.1 убраны две проверки: «пересекающиеся trigger matchers двух Exclusive» и «Event с общим trigger пишет State, читаемый Launch Conditions другого». Как Error они запретили бы штатный контент, на котором держатся A10 и Р6: две сцены одной двери с взаимоисключающими условиями («открыть, если не заперта» / «отпереть ключом») и несколько сцен на один вход в локацию (вступление и радио). Неоднозначность выявляется там, где она реальна, — во время исполнения: две допустимые сцены на действие игрока — Error (A10).

| Группа | Правило |
|---|---|
| Настройки | ровно одна `game.settings`; `player_character` — существующий персонаж |
| Встроенные | `initial_location` — существующая локация или `none`; `initial_holder` — допустимое значение `holder`; встроенные свойства нельзя объявить в `states` |
| Interaction | `primary_interaction` из списка 0.3 §5.2; trigger `interaction` с типом, требующим предмет, без `item` — ошибка |
| Dialogue | `mode` = `normal` или `novel` (`minimal`/`vn` — ошибка, Р10, Р12); `lines` непуст; шаг — реплика или `choice` (2–9 вариантов: `label`, необязательные `lines`, `end: true` или `next` — существующий диалог); `speaker` — существующий персонаж с `emotions`; `emotion` объявлена у говорящего; новелла — `partner` (персонаж, не герой), говорят только герой и `partner`; у обычного диалога `partner` нет |
| Responses | правила полей по `tier`; ровно один `fallback` на каждый тип взаимодействия; пул `generic` из одной фразы допустим (Р10); две реакции `specific` с одинаковыми `interaction`+`target` и без `condition` — ошибка |
| Actions | `inventory.to` — допустимое значение; `move.character` — персонаж; `transition.location` — локация |
| Location | `scene` непуст |

Проверки, требующие Unity (сцена есть в Build Settings, у представления есть клип `clip`, точка входа `entry` есть в сцене, у объекта есть представление с точкой подхода), — в Editor-валидаторе (этап 12) и как Error во время исполнения.

## II.3. State System

Без изменений относительно плана 0.1 (II.3): `IStateReader`, `StateSystem.Set`, `Changed` после commit, `Restored`, `ExportPersistent`, `Restore` с проверкой снимка целиком; любое несовпадение со схемой контента — отказ (A15). Встроенные свойства `location` и `holder` — обычные State-свойства, сгенерированные Loader'ом из обязательных полей: `persistent`; `location` — `scenario_or_system`, `holder` — `system_only`.

## II.4. Condition System

Без изменений относительно плана 0.1 (II.4).

## II.5. Scenario System

Контракт, runtime-структуры и цикл `Pump` — как в плане 0.1 (II.5), с изменениями:

- **Р3:** `Failed` блокирует сцену до Load: trigger, CALL, START её не запускают (для CALL/START — Failure вызывающей, Р4). В `ScenarioSnapshot` статус Failed не попадает.
- **Р4:** CALL и START проверяют цель одинаково; любая проверка не пройдена — Failure вызывающей.
- **A10 / Р6 — разрешение действия игрока** (`Interaction`, origin Player):

```text
1. Отобрать сцены, чей trigger совпал.
   Среди них есть Failed, чьи Launch Conditions = True → Error EventBlockedByFailure, стоп (Р3, Р0)
   Оставить допустимые: не Running, Repeat Policy, Launch Conditions = True
   (Error условия → Error, стоп)
2. одна допустимая  → запустить
   две и более      → Error AmbiguousPlayerAction, ничего не запускать
   ни одной         → реакции:
       specific   (совпали interaction+target[+item], condition = True): одна → показать; больше → Error
       contextual (то же без target):                                     одна → показать; больше → Error
       generic    (пул по interaction): следующая по кругу, не та, что была последней
       fallback   (ровно одна по interaction): показать или «нет реакции», если none
```

  Последняя показанная реакция пула — transient (после загрузки круг начинается заново).
- **T17:** показ реакции = Exclusive control + Minimalistic-диалог из одной реплики.
- **Применение предмета** (0.3 §4.5): сцена найдена — выбор предмета остаётся, пока сцена не выполнит `clear_selection`; сработала реакция — выбор остаётся.
- **Отмена диалога** (0.3 §2.4) — Failure ожидающей сцены. При корректной работе возникает только при Recovery: переход и загрузка не могут начаться, пока сцена ждёт свой диалог.

Порты, которые вызывает Scenario (реализуются в `Paramatrix.Gameplay` и `Paramatrix.Unity`):

```csharp
public interface IGameFlowPort       { bool TryAcquireScenarioControl(out ControlToken t); void ReleaseScenarioControl(ControlToken t, ReleaseReason r); }
public interface IDialoguePort       { OperationStart Show(EntityId dialogueId); OperationStart ShowLine(EntityId speaker, string text); }
public interface IMovementPort       { OperationStart MoveTo(EntityId character, EntityId target); }
public interface IAnimationPort      { OperationStart Play(EntityId target, string clip); }
public interface ILocationPort       { OperationStart Transition(EntityId location, string entry); }
public interface IInventoryPort      { OperationResult MoveItem(EntityId item, HolderValue to); void ClearSelection(); }
```

Каждая начатая операция завершается ровно одним `CompleteOperation` (`Completed` | `Failed` | `Cancelled`).

## II.6. Gameplay Systems (`Paramatrix.Gameplay`, без Unity)

### Game Flow

```csharp
public sealed class GameFlow : IGameFlowPort
{
    public ControlOwner Owner { get; }               // Player | Scenario | System
    public DialoguePresentation Presentation { get; } // None | Minimal | VN
    public bool InTransition { get; }
    public PlayerPermissions Permissions { get; }    // Move, Interact, Inventory, SaveMenu — вычисляются
    public event Action Changed;                     // для слоя ввода и UI

    public bool TryAcquireScenarioControl(out ControlToken t);   // только из Owner = Player
    public void ReleaseScenarioControl(ControlToken t, ReleaseReason r);
    public SystemToken BeginSystemOperation(SystemOperation op);  // New Game, Load, Recovery
    public void EndSystemOperation(SystemToken t);
    public void SetPresentation(DialoguePresentation p);          // вызывает Dialogue
    public void SetTransition(bool active);                       // вызывает Location
    public void EnterRecovery(Diagnostic cause);                  // Abort цепочки, очистка очереди, возврат к Player
}
```

Повторный или чужой `Release` — Error (контракт). Recovery: `ScenarioRuntime.AbortActiveChain`, `ClearPendingRequests`, отмена диалога, Owner = Player, ошибка — в оверлей (T20).

### Interaction

Получает от Unity-слоя «клик по сущности X кнопкой Y» (и выбранный предмет из Inventory) → определяет тип (T12) → ЛКМ: цель вне радиуса героя — ничего (Р11); сцена — герой останавливается и поворачивается к цели → `ScenarioRuntime.Submit(Interaction)` → `Pump()`. Проверяет `Permissions.Interact`; клик без разрешения игнорируется (ввод заблокирован владельцем — это не ошибка).

### Inventory

- Источник истины — `item.*.holder` в State. Список инвентаря = предметы с `holder = inventory` в порядке контента (T18).
- `MoveItem` → `StateSystem.Set(holder, System)`; при переходе в `inventory` — `Submit(ItemAcquired)`.
- Выбранный предмет — состояние UI (не State, не сохраняется). ПКМ по пустому месту — снять выбор (0.3 §5.3).

### Location

- `Transition(location, entry)`: `SetTransition(true)` → `Set(hero.location)` → через порт сцен Unity: выгрузить текущую сцену, загрузить новую, дождаться регистрации представлений → поставить героя в точку `entry` → `SetTransition(false)` → `ScenarioRuntime.BeginLocationVisit(location)` (visit++, `LocationEntered`) → `CompleteOperation`.
- `LoadTechnical(location)` (Load Game): то же без `Set`, без Visit и trigger; героя — в точку входа по умолчанию (Р9).
- `EnterInitial()` (New Game): `Transition` в `initial_location` героя под владельцем `System` (Р2).

### Save

```csharp
public sealed class SaveService
{
    public bool CanSave { get; }                          // Owner = Player && ScenarioRuntime.CanSave
    public SaveResult Save(string slot);                  // State + Scenario → JSON → ISaveStorage
    public LoadResult Load(string slot);                  // чтение → полная проверка → Restore State, Scenario → LoadTechnical
}
```

Load: проверка обоих снимков до применения; любое несовпадение — отказ, мир не меняется (A15). `ISaveStorage` — порт к файлам (Unity-слой).

### Порядок запуска (Р2)

```text
New Game:  Load Content → Validate → Registry → State (defaults) → Conditions → Scenario.Start
           → Gameplay → Bootstrap-сцена готова → Location.EnterInitial (обычный вход)
           → Owner = Player → Pump (вступление стартует по LocationEntered)
Load Game: … → Scenario.Start → SaveService.Load → Location.LoadTechnical → baseline мониторов → Owner = Player
```

## II.7. Unity-слой

| Компонент | Ответственность |
|---|---|
| `Bootstrap`-сцена, `GameBootstrap` | composition root; `DontDestroyOnLoad`; `Update` → `Pump()` |
| `ContentManifest` (ScriptableObject) | список `TextAsset` контента; кнопка «собрать из `Assets/Paramatrix/Content/`» |
| `SceneLoader` | аддитивная загрузка/выгрузка сцен локаций для `Location` |
| `EntityView` и наследники `CharacterView`, `ItemView`, `ObjectView` | `[SerializeField] entityId`; hitbox; точка подхода; клипы анимаций; видимость по `location`/`holder` |
| `EntryPoint` | именованная точка входа в сцене локации, одна помечена «по умолчанию» |
| `RepresentationRegistry` | ID → представления; неизвестный ID при регистрации — Error; обновление по `Changed`, перестройка по `Restored` и после загрузки сцены |
| `HeroController` | ходьба по линии пола (клавиатура, T19), радиус взаимодействия (Р11), `MoveTo` для сцен |
| `InputRouter` | Input System → клики и движение → `Interaction`/`HeroController` с учётом `Permissions` |
| `DialogueView` | Minimalistic — реплика над говорящим (world-space), VN — панель с именем; продвижение кликом/Space; без пропуска всего диалога |
| `InventoryView` | иконки, панель описания по наведению, закреплённый выбор |
| `DiagnosticsOverlay` | Error-диагностики поверх игры (T20) |
| `FileSaveStorage` | `ISaveStorage` на `persistentDataPath` |

Говорящий в Minimalistic-диалоге должен быть в текущей локации; иначе — Failure операции.

## II.8. Этапы

Этапы 0–8 проверяются `dotnet test` (и тем же набором в Unity EditMode), 9–12 — Unity Test Runner и ручной проверкой.

| № | Этап | Зависит | Критерий завершения |
|---|---|---|---|
| 0 | Каркас: Unity 2D (URP), пакеты (T22), asmdef'ы, `tools/*.csproj`, git | — | пустой тест зелёный в обеих средах |
| 1 | Основа контента: ID, значения, diagnostics, JSON-ридер с путями | 0 | тесты форматов |
| 2 | Сущности, State-definitions, встроенные свойства, Validator, Registry | 1 | негативный fixture на каждое правило |
| 3 | State System | 2 | тесты 0.3.1 §11 |
| 4 | Condition System | 2, 3 | тесты 0.3.2 §11 (кроме Integration) |
| 5 | Сцены, диалоги, реакции, настройки: definitions, компиляция, Validator | 2, 4 | негативные fixtures на каждое правило |
| 6 | Scenario: ядро — Pump, matching, lifecycle, очередь, Background, синхронные Actions, CALL/START, Failure, разрешение действия игрока и реакции | 5 | тесты 0.4 §18 на фейковых портах |
| 7 | Scenario: асинхронные операции, Visits, мониторы, снимок | 6 | тесты 0.3.2 §11 Integration, Addendum §1/§4 |
| 8 | Gameplay: Game Flow, Interaction, Inventory, Location, Save, порядок запуска | 7 | сквозные сценарии без Unity (II.9 п.4) |
| 9 | Unity-основа: bootstrap, manifest, SceneLoader, представления, `HeroController`, `InputRouter`, оверлей ошибок, файлы | 8 | PlayMode: старт, ходьба, клик по объекту доходит до Scenario |
| 10 | Unity-представление: `DialogueView` (оба режима), `InventoryView`, показ реакций | 9 | ручная проверка по чек-листу + PlayMode-тест завершения диалога |
| 11 | Вертикальный срез: офис + коридор, Морфеус, ключ, дверь; Save/Load | 10 | игра проходится от вступления до коридора; Load не повторяет вступление |
| 12 | Инструменты автора: Editor-валидация контента и сцен | 5, 9 | ошибки видны в Console с файлом и путём; неизвестный `entityId`, клип, точка входа в сцене — ошибки |

```text
0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11
                    5, 9 ─────────────→ 12
```

Порядок: вся семантика проверяется без Unity (0–8), Unity подключается к уже проверенной логике.

## II.9. Тестирование

1. **Юнит-тесты** логики — один набор, две среды.
2. **Content fixtures** — invalid-файл на каждое правило Validator с ожидаемым `(code, json path)`.
3. **Сценарный harness** — Core + Gameplay из JSON-строк, фейковые Unity-порты с ручным завершением операций, журнал вызовов портов; детерминизм — сравнение журналов двух прогонов и прогона с переставленными файлами.
4. **Сквозные сценарии без Unity** (этап 8):
   - New Game → вступление по `LocationEntered` → управление у игрока;
   - клик по запертой двери → реакция specific; две реакции specific → Error; нет реакции → fallback;
   - взять ключ → `holder = inventory`, `ItemAcquired`; применить к двери → сцена → `clear_selection`;
   - выход в коридор → Visit+1, `LocationEntered`, `once_per_location_visit` снова доступна;
   - Save → Load: State, статусы, Visit, герой на точке входа по умолчанию (Р9), нет `LocationEntered`, нет `StateChanged`, мониторы без ложных срабатываний;
   - Load с удалённым из контента свойством → отказ, мир не изменён;
   - сцена упала → Failed; повторный клик → Error `EventBlockedByFailure`, реакция вместо неё не показывается; после Load сцена работает;
   - Recovery во время диалога → диалог отменён, сцена Failed, управление у игрока, ошибка в оверлее.
5. **PlayMode** — немного: bootstrap, ходьба и подход, переход между сценами, диалог.
6. **Трассировка** — таблица «Acceptance Criteria 0.3.1/0.3.2/0.4 → тесты» в репозитории.

**Уточнение к Р3 + Р6, решено по Р0:** если под действие игрока подходит сцена, заблокированная статусом Failed (и её Launch Conditions истинны), это **не** «ни одной сцены»: Error `EventBlockedByFailure`, реакция не подставляется. Иначе ошибка спряталась бы за fallback «Ничего не происходит».

## II.10. Риски

| Риск | Мера |
|---|---|
| Зависшая асинхронная операция блокирует игру | контракт «ровно одно завершение»; Recovery через Game Flow |
| Выгрузка сцены во время сцены-Event | постоянная Bootstrap-сцена; логика не хранит ссылок на GameObject |
| Отключённый Domain Reload | нет статического изменяемого состояния |
| Опечатки в ID в Unity-сценах | Error при регистрации; Editor-проверка (этап 12) |
| Расхождение C# Unity и `dotnet` | `LangVersion 9`, `netstandard2.1`; этап 0 проверяет обе среды |
| IL2CPP stripping | ручной разбор `JToken`, без reflection |
| Кириллица в TextMeshPro | font asset с кириллическим диапазоном в этапе 10 |
| Ручное написание JSON | точные пути в ошибках; Editor-валидация; редактор сценариев — по факту объёма |
| Строгость Р0/Р3 при ошибке контента в релизе | игрок застревает до загрузки; поведение релизной сборки решить до первого внешнего тестирования |
| Нет git | `git init` в этапе 0 |

## II.11. Вне плана

Выборы в диалогах, автономное поведение NPC, глубина 2D-сцены и поиск пути, звук, слоты и автосохранение в UI, локализация, редактор сценариев, миграции Save, всё из «Out of Scope» 0.3.1 §12, 0.3.2 §12, 0.4 §19.
