# Параматрица — Implementation Plan 0.1
## Content Infrastructure, State, Conditions, Scenario

**Статус:** заменён Implementation Plan 0.2 (`IP_0_2_Implementation_Plan.md`); оставлен для истории  
**Основано на:** Architecture 0.3.1, 0.3.2, 0.4, 0.4 Semantic Addendum  
**Не основано на (не передано):** Architecture 0.3 — Game Flow, Dialogue, Location, Inventory, Interaction, Movement

Документ состоит из двух частей:

- **Часть I — архитектурный анализ:** противоречия, неоднозначности, недостающие контракты. Каждый пункт помечен:
  **[Р]** — требует решения пользователя (меняет или уточняет согласованную семантику);
  **[Т]** — решается технически, решение предложено, ревью желательно.
- **Часть II — implementation plan:** устройство, C#-контракты, JSON-формат, этапы, тесты, риски.

Часть II написана в предположении, что рекомендованные варианты Части I приняты. Где решение меняет план — указано.

---

# Часть I. Архитектурный анализ

## A1. [Р] Где живёт runtime-состояние Scenario System и как оно сохраняется

**Проблема.** Три утверждения не сходятся:

1. 0.3.1 §3: владелец «технического статуса сценарного Event» — *Scenario runtime*; но §3.2 записывает его как адрес State: `event.morpheus_intro.completed`.
2. Addendum §1: completion history (особенно `once_per_location_visit`) **обязана** сохраняться и восстанавливаться.
3. 0.3.1 §9: Save System получает данные **только** через `ExportPersistentState()` State System и «не собирает State из отдельных Gameplay Systems».

Если история Events — данные Scenario System, то по п.3 её некому сохранить. Если она в State System — противоречит п.1 (владелец) и требует, чтобы Scenario писала свои технические флаги через `SetState`, порождая `StateChanged` → новые triggers о собственном исполнении.

Сюда же: 0.4 §10.1 говорит «CALL completed-once Event — ошибка, *если сценарист явно не проверил доступность заранее*». Проверить заранее можно только Condition, а Condition читает только State (0.3.2 §6). Значит, при хранении истории вне State такая проверка невозможна.

**Влияние.** Формат Save, API Restore, возможности Conditions, связь Story State и Event State.

**Варианты.**

- **B (рекомендую).** Scenario System владеет своим runtime-состоянием (статусы, completion, номер текущего Visit) и экспортирует собственный `ScenarioSnapshot`. Save = `{ StateSnapshot, ScenarioSnapshot }`; Save System получает обе части через два явных экспортёра. Conditions **не видят** статусы Events; если сюжету нужен факт «встреча произошла», Event явно ставит Story State (`story.morpheus_meeting.occurred`) — ровно то разделение, которое описывает 0.3.1 §3.2. CALL недоступного Event — всегда Failure.
  *Цена:* правка формулировки 0.3.1 §9 («Save получает persistent State от State System и Scenario runtime state от Scenario System; от прочих систем — ничего»). Адрес `event.X.completed` в §3.2 становится иллюстрацией, а не State-адресом.
- **A.** Каждый Event неявно получает State-свойства (`event.X.completed: boolean`, `event.X.completed_visit: integer`, плюс глобальный счётчик Visit), mutation policy — новая, только для Scenario runtime. Save идёт одним путём, Conditions могут читать статус Event.
  *Цена:* технические флаги смешиваются с фактами мира (инвариант 1 0.3.1), появляется синтетическая генерация definitions, и каждое завершение Event публикует `StateChanged`, который сам может запускать Events — дублирование с trigger `EventCompleted`.

## A2. [Р] Как новая игра получает первый `LocationEntered`

**Проблема.** 0.3.1 §7: шаг 9 «Load initial Location», шаг 12 «Initialize Scenario System». Если загрузка начальной локации — обычный вход, его `LocationEntered` теряется (Scenario ещё не готова) и Visit не начинается. Если технический — первый Visit не начинается никогда, а вступительный Event нечем запустить: Initialization по правилам не порождает triggers, а trigger «начало игры» в списке нет.

**Рекомендация.** Разделить New Game и Load Game:

- **New Game:** шаги 1–12, затем Game Flow выполняет **обычный вход** в стартовую локацию → начинается Visit #1 → `LocationEntered` → вступительный Event (через `LocationEntered` + Launch Conditions).
- **Load Game:** шаги 1–8, Restore (State + Scenario), загрузка локации из восстановленного State как **техническая**, reconstruct, Scenario восстанавливает baseline мониторов; Visit не создаётся, triggers нет.

Это меняет порядок шагов §7 — нужно согласие.

## A3. [Р] Может ли Failed Event снова запуститься от нового trigger

**Проблема.** 0.4 §5.1: «Failed Event не запускается автоматически повторно. Повторный запуск допустим только через явный сценарный механизм или после recovery». Addendum §1, §3: Failed не расходует eligibility, «не предполагается автоматический retry». Не ясно, является ли **новый** trigger (игрок снова нажал на дверь) «автоматическим повтором». «Явный сценарный механизм» сброса не определён ни одним Action.

**Варианты.**
- **Рекомендую:** новый trigger — новая попытка; «без автоматического retry» означает, что Scenario не перезапускает упавшее исполнение сама и удаляет из очереди его ожидающие requests. Failed — это только статус последнего исполнения.
- Альтернатива: Failed блокирует Event до явного сброса. Нужен новый Action `reset_event` или API recovery в Game Flow. Риск: soft-lock при контентной ошибке.

## A4. [Р] Семантика CALL и START при недоступном Event

Документы фиксируют только часть случаев. Предлагаемая полная таблица:

| Ситуация | CALL | START |
|---|---|---|
| self / цель уже Running | Failure caller (Addendum §2) | игнорируется, diagnostic Warning (0.4 §10: «повторный запрос Running игнорируется») |
| `once` уже Completed | Failure caller (0.4 §10.1) | не запускается, Warning, caller продолжает |
| `once_per_location_visit` уже Completed в этом Visit | Failure caller | не запускается, Warning |
| Launch Conditions = False | **Failure caller** | не запускается, caller продолжает |
| Launch Conditions = Error | Failure caller | не запускается, Error diagnostic, caller продолжает |
| Background → CALL/START Exclusive | ошибка Validator | ошибка Validator |

Вопрос пользователю: (1) проверяет ли CALL Launch Conditions цели (рекомендую — да, Launch Conditions — часть определения Event, а не trigger); (2) согласна ли асимметрия «CALL недоступного = Failure, START недоступного = не Failure» (рекомендую — да: START по смыслу не ждёт результата).

## A5. [Р] Exclusive → START Exclusive

Не определено (0.4 §9.4 говорит только о START Background). Активный Exclusive может быть только один.

- **Рекомендую:** разрешить; такой START становится **автоматическим** request и встаёт в FIFO-очередь, запустится после завершения текущей Exclusive chain.
- Альтернатива: запретить Validator'ом (последовательность выражается CALL в конце Event).

## A6. [Р] Background Event исполняется мгновенно

**Проблема.** 0.4 §6.2 перечисляет запреты для Background, но не говорит прямо, может ли Background ждать (`Pending`): анимацию, CALL другого Background с ожиданием и т.п. Если Background может приостанавливаться, появляется настоящее чередование с Exclusive Event, и §6.3 (конфликты мутаций без locks) становится реальной проблемой.

**Рекомендация.** Background Event в MVP **не имеет Pending-действий**: исполняется от начала до конца за один шаг обработки. Разрешённые Actions: `set_state`, `if`, `call`/`start` Background, `end`, инвентарные операции, если они синхронны (см. A12). Всё асинхронное — ошибка Validator.

*Следствие:* Background никогда не бывает Running в момент сохранения и не чередуется с действиями Exclusive Event — между двумя Actions Exclusive Event может выполниться целый Background Event, но не половина. §6.3 сводится к правилу «Background видит State между Actions Exclusive Event».

Это сужение, а не изменение заявленной семантики, но если нужны фоновые NPC-анимации с ожиданием — скажите сейчас: это другой runtime.

## A7. [Р] WAIT — свойство асинхронного Action, а не отдельный Action

**Проблема.** WAIT как отдельный Action должен знать, *какую* операцию ждать (какой Dialogue из запущенных?), т.е. нужен handle от предыдущего Action. Кроме того, неожидаемый асинхронный Action (запустили Dialogue и пошли дальше) создаёт случаи «Event завершился, а его Dialogue ещё открыт; control уже отдан».

**Рекомендация.** В MVP каждый асинхронный Action (`dialogue`, `move`, `animation`, `transition`) **всегда ждёт** своего завершения. `WAIT EventCompleted` = `call`. Отдельного Action `wait` нет. Пять видов ожиданий из 0.4 §9.2 покрываются полностью.

Альтернатива — `"wait": false` у асинхронных Actions плюс правило «Event не завершается, пока его операции не закончены». Больше сценарной выразительности (реплика на ходу), заметно больше runtime-случаев. Предлагаю отложить до конкретной сцены.

## A8. [Р] END неявный в конце последовательности

**Проблема.** 0.4 §8.1 перечисляет `If, Else, End` рядом — похоже на плоский формат, где END закрывает блок; §9.5 говорит, что END завершает Event. Validator §17 требует проверять «отсутствующие END».

**Рекомендация.** JSON-последовательность вложенная (`if` с `then`/`else`), поэтому блоки закрывать не нужно. Достижение конца списка Actions = успешный END. Явный `end` — досрочное успешное завершение внутри ветки. Проверка «отсутствует END» отпадает; проверка «недостижимые Actions» = Actions после `end` в том же списке.

## A9. [Р] Persistence `reconstructible` без контракта

**Проблема.** Не сказано, кто и когда реконструирует такие значения. Если владеющая система после Restore делает `SetState(source: System)`, публикуется `StateChanged` → triggers при загрузке, что запрещено (0.3.1 инв. 9). Иначе нужен ещё один «тихий» источник мутаций.

**Рекомендация.** Убрать `reconstructible` из MVP до первого конкретного свойства (speculative generalization). Остаются `persistent` и `transient`; transient после Restore равны default. Альтернатива: оставить, но тогда нужен источник `Reconstruct`, не публикующий уведомления, и правило, кто его вызывает.

## A10. [Р] Детерминированный порядок, когда один trigger совпал с несколькими Events

Addendum §5 просит предложить порядок.

**Рекомендация.**
- Кандидаты обрабатываются в порядке **ординального сравнения Event ID** (не порядка файлов — он зависит от файловой системы).
- Launch Conditions каждого кандидата вычисляются непосредственно перед его запуском (Background, выполненный раньше, может повлиять на условия следующего — детерминированно).
- **Автоматический** trigger: Background-кандидаты исполняются сразу; Exclusive-кандидаты — первый допустимый стартует (если нет активного), остальные встают в очередь в этом же порядке.
- **Player-initiated** trigger: стартует первый допустимый Exclusive; остальные допустимые Exclusive **отклоняются** с Warning `AmbiguousPlayerTrigger` (0.4 §7.3 запрещает их очередь).
- Validator выдаёт Warning, если два Event имеют пересекающийся trigger matcher и (а) оба Exclusive, или (б) один пишет State, который читают Launch Conditions другого. Это статически вычислимо: Conditions и Actions не содержат выражений.

Минус: переименование Event ID меняет порядок. Поэтому Warning, а сценарий, зависящий от порядка, должен выразить его через CALL/START (Addendum §5).

## A11. [Р] Набор Trigger types для MVP

| Trigger | Проблема |
|---|---|
| `PlayerAction` | payload и отличие от `Interaction` не определены |
| `DialogueStarted` | Dialogue запускает только Scenario — trigger сообщает Scenario о её собственном действии |
| `AnimationCompleted` | ожидание анимации уже покрыто WAIT; кто ещё запускает анимации — не определено |

**Рекомендация:** MVP-набор — `Interaction`, `ItemAcquired`, `ItemUsed`, `ItemCombined`, `StateChanged`, `LocationEntered`, `DialogueCompleted`, `EventCompleted`, `ConditionsMet`. Три перечисленных выше добавлять при появлении конкретного сценария. Добавление trigger type — локальное изменение (новый matcher + publisher).

## A12. [Р] Не переданы контракты Gameplay Systems и Game Flow (Architecture 0.3)

Scenario System вызывает Dialogue, Movement, Animation, Inventory, Interaction, Location Transition и запрашивает control у Game Flow, но их поведение в переданных документах не описано: режимы Game Flow (Free Roam, Loading, Recovery — упоминаются), два вида Dialogue, формат dialogue-контента, синхронны ли операции инвентаря, что такое «InteractionOperation».

**Рекомендация.** В этот план входят только **порты** (C#-интерфейсы, которые Scenario вызывает), их тестовые двойники и минимальные Unity-заглушки для проверки сквозного пути. Сами системы — отдельный план после передачи Architecture 0.3. Если 0.3 существует — прошу передать: порты в разделе II.4 нужно сверить с ним до кодирования.

## A13. [Р] Новая зависимость: JSON-парсер

`JsonUtility` Unity не поддерживает словари, полиморфные узлы (Condition, Action) и не сообщает о неизвестных полях — а их обнаружение обязательно (0.3.1 §10, 0.3.2 §10, 0.4 §17).

**Рекомендация:** `com.unity.nuget.newtonsoft-json` (официальный пакет Unity, Newtonsoft.Json 13.x); для внеюнитевого тестового проекта — NuGet `Newtonsoft.Json` той же версии. Используется только `JToken`-уровень с **ручным** отображением в definitions: без reflection-десериализации — значит, без проблем с IL2CPP stripping, с точными JSON-путями в diagnostics и полным контролем неизвестных полей.
Альтернатива: `System.Text.Json` через NuGet-DLL в Unity — работает, но не поставляется Unity официально.

По правилам проекта новая зависимость — по согласию.

## A14. [Р] Целевые платформы

Влияет на загрузку контента: на десктопе можно читать файлы из `StreamingAssets` синхронно; на WebGL/Android — только асинхронно. **Рекомендация:** контент — `TextAsset` (.json) внутри проекта, собранный через `ContentManifest` (ScriptableObject); Core получает пары `(имя источника, текст)` и от платформы не зависит. Вопрос: какие платформы в планах? (Если только Windows — вопрос снимается.)

## A15. [Р] Политика совместимости Save при изменении контента

0.3.1 §8: Restore «проверяет snapshot; восстанавливает допустимые State values» — «допустимые» читается как частичное восстановление, что может дать несогласованный мир.

**Рекомендация:** снимок проверяется целиком до применения. Неизвестная сущность/свойство (удалены из контента) — пропуск с Warning. Ошибка типа, диапазона, enum — отказ всего Restore, State не меняется. Отсутствующее в снимке свойство — default. Миграции Save — вне MVP. Альтернатива: строгий режим (любое расхождение — отказ).

## A16. [Р] Ссылки на сущности в State

Где-то должны храниться текущая локация героя и подобные ссылки. Типа «ссылка на сущность» нет. **Рекомендация для MVP:** Enum со значениями-ID (`"values": ["location.office", "location.corridor"]`), Validator дополнительно проверяет, что значения enum, похожие на Entity ID, существуют (Warning). Если таких свойств окажется много — отдельное решение о типе `EntityRef`.

---

## Технические решения, принятые самостоятельно [Т]

Не меняют согласованную семантику, но заметно влияют на поведение — вынесены для ревью.

**T1. Когда обрабатываются уведомления.** `StateSystem` вызывает подписчиков синхронно после commit; обработчик Scenario только кладёт элемент во **входящую очередь (inbox)**. Inbox разбирается циклом `Pump()` между Actions: после каждого Action активной цепочки inbox разбирается полностью, затем следующий Action. `Pump()` нереентерабелен. Отсюда: 0.3.1 §6.2 выполнен (Scenario не вызывается внутри `SetState`), а мониторы и Launch Conditions видят State **на момент разбора**, а не на момент уведомления. Промежуточное значение внутри одного Action (например, если операция инвентаря меняет два свойства) мониторы не видят.

**T2. Мониторы ConditionsMet.** На каждый trigger `ConditionsMet` — запись `{event, condition, previous: True|False|Error}`. Множество State-адресов, от которых зависит условие, вычисляется статически при загрузке (с раскрытием ссылок); индекс «адрес → мониторы» отбирает, что перевычислять на `StateChanged`. Срабатывание — только `False → True`. `Error` записывается как baseline с diagnostic; `Error → True` **не** срабатывает (буквальное чтение «false → true»). Baseline выставляется при старте Scenario и после Restore.

**T3. Предохранитель цепочек.** «Максимальное количество автоматических Events в одной цепочке» (0.4 §10) — счётчик запусков Events за один **каскад**: каскад начинается с внешнего входа (trigger извне, завершение операции) и заканчивается, когда `Pump()` нечего делать. Превышение → Error diagnostic, автоматические requests каскада отбрасываются. Глубина CALL — отдельный предел. Значения — константы в конфигурации (стартовые: CALL depth 8, событий за каскад 64).

**T4. Visit.** Глобальный монотонный счётчик `visitSeq`, увеличивается **до** публикации `LocationEntered`. Event запоминает `visitSeq` в момент запуска; при успешном завершении `once_per_location_visit` записывает `lastCompletedVisit = visitSeq запуска`. Eligibility: `lastCompletedVisit != текущий visitSeq`. Если внутри Event происходит переход в новую локацию — completion относится к Visit, в котором Event был запущен.

**T5. Очередь Exclusive.** FIFO без дублей: Event, уже стоящий в очереди, повторно не добавляется. При извлечении — повторная проверка Running/Repeat Policy/Launch Conditions (0.4 §7.2). Если Game Flow отказывает в control — request остаётся в очереди; очистку при Loading/Recovery выполняет явный вызов от Game Flow.

**T6. Save — только в состоянии покоя.** `CanSave` = нет активной Exclusive chain, inbox и очередь пусты. Активная цепочка не сохраняется (0.4 §19), поэтому сохранение во время Event невозможно, а не «сохраняется частично». Разрешать ли Save пользователю — решает Game Flow.

**T7. Аварийное прерывание.** Если асинхронная операция не завершится (уничтожен объект, баг), Exclusive chain зависнет навсегда. Контракт портов: каждая начатая операция завершается **ровно один раз** (Success/Failure). Плюс `ScenarioRuntime.AbortActiveChain(reason)` для Game Flow Recovery: цепочка → Failed, control освобождён.

**T8. Сравнение Enum** — только `equals`/`not_equals` (0.3.2 §3 разрешает numeric только Integer). Integer — `int` (32 бита), min/max включительно.

**T9. Источники мутаций в API.** Публичный `SetState` принимает только `Scenario` и `System`. `Initialization` и `Restore` — внутренние пути State System, не значения параметра (0.3.1 §4.4: «специальные операции, не обычные mutations»).

**T10. Перепроверка условий внутри Event.** 0.4 §6.3 «Conditions перепроверяются перед значимыми Actions» трактуется как обязанность сценариста (через `if`), а не как автоматическая перепроверка Launch Conditions перед каждым Action.

**T11. Restore и визуал.** Restore не публикует `StateChanged`, но публикует отдельный сигнал `Restored`, по которому Unity Representations перестраиваются целиком. Scenario на `Restored` не реагирует триггерами — только пересчитывает baseline.

---

# Часть II. Implementation Plan

## II.1. Устройство решения

### Сборки

```text
Paramatrix.Content      ← ID, значения, definitions, diagnostics, парсер, validator, registry
    ↑
Paramatrix.State        ← StateSystem
    ↑
Paramatrix.Conditions   ← ConditionEvaluator (видит только IStateReader)
    ↑
Paramatrix.Scenario     ← ScenarioRuntime, порты Gameplay Systems / Game Flow
    ↑
Paramatrix.Unity        ← bootstrap, загрузка TextAsset, Representations, адаптеры портов
Paramatrix.Unity.Editor ← валидация контента и сцен из меню, drawer для ID
```

- Четыре сборки Core — `asmdef` с `noEngineReferences: true`: ни одна не видит UnityEngine.
- Направление зависимостей проверяется компилятором: Conditions физически не может сослаться на Scenario, а получает `IStateReader` без метода записи — инвариант «Conditions не изменяют State» обеспечен типами.
- **Двойная сборка Core.** Те же исходники подключаются в `tools/Paramatrix.Core.Tests.csproj` (`netstandard2.1`, `LangVersion 9`) для `dotnet test` из консоли — быстрый цикл и прогон без Unity. В Unity те же тесты идут через Unity Test Framework (EditMode). `LangVersion 9` закреплён, чтобы не протащить синтаксис, который Unity 6000.4 не компилирует.

### Правила для всего кода

- Однопоточно: всё на главном потоке Unity, без locks, `async/await` и корутин в Core.
- Никакого статического изменяемого состояния (совместимость с отключённым Domain Reload; тестируемость). Сервисы собираются в composition root.
- Definitions — `sealed class` с get-only свойствами и `IReadOnlyList`/`IReadOnlyDictionary`, коллекции копируются при создании.
- Перебор коллекций, влияющий на поведение, — только по отсортированным массивам, не по `Dictionary`.
- Ошибки контента и сценариев — `Diagnostic`, не исключения. Исключение — только нарушение контракта кодом (баг).

## II.2. Content Infrastructure

### Идентификаторы и значения

```csharp
public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
{
    public string Value { get; }          // "object.office_door"
    public EntityKind Kind { get; }       // из префикса
    public static bool TryParse(string s, out EntityId id, out string error);
}
// ^[a-z][a-z0-9_]*\.[a-z0-9_]+$ ; префикс — из закрытого списка

public enum EntityKind { Character, Item, Object, Location, Story, Condition, Event, Dialogue }

public readonly struct StateAddress : IEquatable<StateAddress>
{
    public EntityId Entity { get; }
    public string Property { get; }       // ^[a-z][a-z0-9_]*$
    public override string ToString();    // "object.office_door.open" — только для diagnostics
}

public enum StateType { Boolean, Integer, String, Enum }

public readonly struct StateValue : IEquatable<StateValue>
{
    public StateType Type { get; }
    public bool AsBoolean { get; }        // throws при несовпадении типа
    public int AsInteger { get; }
    public string AsString { get; }       // String и Enum
    public static StateValue Boolean(bool v); /* Integer, String, Enum */
}
```

- Тип сущности выводится из префикса ID — отдельного поля `type` в JSON нет (нечему расходиться).
- Хранить состояние (`states`) могут только `Character, Item, Object, Location, Story`.
- `Dialogue` — только ссылочная сущность: в этом плане регистрируются её ID, чтобы Validator проверял ссылки; формат содержимого диалогов — за Dialogue System (A12).

### Definitions

```csharp
public sealed class StateDefinition
{
    public StateAddress Address { get; }
    public StateType Type { get; }
    public StateValue Default { get; }
    public Persistence Persistence { get; }      // Persistent | Transient
    public MutationPolicy MutationPolicy { get; } // ScenarioOnly | SystemOnly | ScenarioOrSystem
    public int? Min { get; } public int? Max { get; }
    public IReadOnlyList<string> EnumValues { get; }
}

public sealed class EntityDefinition
{
    public EntityId Id { get; }
    public IReadOnlyDictionary<string, StateDefinition> States { get; }
    public SourceLocation Source { get; }        // файл + JSON path для diagnostics
}

public abstract class ConditionNode { public SourceLocation Source { get; } }
public sealed class AtomicCondition : ConditionNode { StateAddress Target; ComparisonOperator Op; StateValue Value; }
public sealed class AllCondition    : ConditionNode { IReadOnlyList<ConditionNode> Children; }
public sealed class AnyCondition    : ConditionNode { IReadOnlyList<ConditionNode> Children; }
public sealed class NotCondition    : ConditionNode { ConditionNode Child; }
public sealed class ConditionRef    : ConditionNode { EntityId ConditionId; }

public sealed class NamedConditionDefinition { EntityId Id; ConditionNode Root; }

public sealed class EventDefinition
{
    public EntityId Id { get; }
    public ExecutionType ExecutionType { get; }       // Exclusive | Background
    public RepeatPolicy RepeatPolicy { get; }         // Once | Repeatable | OncePerLocationVisit
    public IReadOnlyList<TriggerDefinition> Triggers { get; }
    public ConditionNode LaunchCondition { get; }     // null = всегда True
    public IReadOnlyList<ActionDefinition> Actions { get; }
}
```

`TriggerDefinition` — тип + необязательные фильтры (`target`, `interaction`, `item`, `location`, `dialogue`, `event`, State-адрес и `to` для `StateChanged`, условие для `ConditionsMet`). Отсутствующий фильтр = «любое значение».

`ActionDefinition` — закрытая иерархия: `SetStateAction`, `IfAction(Condition, Then, Else)`, `DialogueAction`, `MoveAction`, `AnimationAction`, `InventoryAction`, `TransitionAction`, `CallAction`, `StartAction`, `EndAction`. Параметры `Move/Animation/Inventory` — заглушечные до A12.

### Registry

```csharp
public sealed class ContentRegistry   // строится только Loader'ом, после успешной валидации
{
    public bool TryGetEntity(EntityId id, out EntityDefinition def);
    public bool TryGetState(StateAddress a, out StateDefinition def);
    public bool TryGetCondition(EntityId id, out NamedConditionDefinition def);
    public bool TryGetEvent(EntityId id, out EventDefinition def);
    public bool Exists(EntityId id);
    public IReadOnlyList<EntityDefinition> GetEntitiesByKind(EntityKind kind); // отсортировано по ID
    public IReadOnlyList<EventDefinition> Events { get; }                      // отсортировано по ID
}
```

Отдельный «State Definitions Registry» (0.3.1 §7 шаг 5) — индекс `StateAddress → StateDefinition` внутри того же registry, а не отдельный объект.

### Конвейер загрузки

```csharp
public readonly struct ContentSource { public string Name; public string Text; }

public static class ContentLoader
{
    public static ContentLoadResult Load(IReadOnlyList<ContentSource> sources);
}
public sealed class ContentLoadResult
{
    public ContentRegistry Registry { get; }        // null, если есть Error
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
}
public sealed class Diagnostic { DiagnosticCode Code; Severity Severity; string Message; SourceLocation Source; }
```

Шаги: парсинг JSON (`JToken`) → структурная проверка и построение definitions (неизвестные/обязательные поля, типы JSON, формат ID) → семантическая валидация по всему набору → registry. Ошибки накапливаются, загрузка не останавливается на первой. Любой `Error` — registry не создаётся, запуск блокируется; `Warning` — нет.

### JSON-формат

Файл:

```json
{
  "format_version": 1,
  "definitions": [ ... ]
}
```

Файлов любое число, раскладка по папкам — на усмотрение автора. `format_version` нужен для будущих миграций; сейчас допустимо только `1`.

Сущность со State:

```json
{
  "id": "object.office_door",
  "states": {
    "open":   { "type": "boolean", "default": false, "persistence": "persistent", "mutation_policy": "scenario_or_system" },
    "locked": { "type": "boolean", "default": true,  "persistence": "persistent", "mutation_policy": "scenario_only" },
    "charge": { "type": "integer", "default": 0, "min": 0, "max": 3, "persistence": "persistent", "mutation_policy": "system_only" },
    "mood":   { "type": "enum", "values": ["calm", "alarmed"], "default": "calm", "persistence": "transient", "mutation_policy": "scenario_only" }
  }
}
```

Condition-узел — ровно одна из форм:

```json
{ "target": "object.office_door", "property": "open", "operator": "equals", "value": true }
{ "all": [ <node>, ... ] }
{ "any": [ <node>, ... ] }
{ "not": <node> }
{ "ref": "condition.door_is_locked" }
```

Named Condition:

```json
{ "id": "condition.door_is_locked",
  "condition": { "target": "object.office_door", "property": "locked", "operator": "equals", "value": true } }
```

Event:

```json
{
  "id": "event.office_door_use",
  "execution": "exclusive",
  "repeat": "repeatable",
  "triggers": [ { "type": "interaction", "target": "object.office_door", "interaction": "primary" } ],
  "launch_condition": { "ref": "condition.office_power_on" },
  "actions": [
    { "action": "if", "condition": { "ref": "condition.door_is_locked" },
      "then": [ { "action": "dialogue", "dialogue": "dialogue.door_locked" } ],
      "else": [
        { "action": "set_state", "target": "object.office_door", "property": "open", "value": true },
        { "action": "transition", "location": "location.corridor" }
      ] },
    { "action": "start", "event": "event.office_background_reaction" }
  ]
}
```

Решение о формальных файлах JSON Schema: **источник истины — C# Validator**. `.schema.json` для автодополнения в редакторе — необязательный поздний этап (II.7, этап 10), генерируется или пишется по готовому формату; если появится, он не валидирует, а только подсказывает.

### Правила Validator (сводно по всем трём документам)

| Группа | Правила | Severity |
|---|---|---|
| Структура | JSON валиден; `format_version`; неизвестные поля; обязательные поля; JSON-типы полей | Error |
| ID | формат; известный префикс; уникальность во всём наборе; уникальность property | Error |
| State | допустимый type; default соответствует типу; min ≤ default ≤ max; min ≤ max; enum непустой, без дублей, default ∈ values; min/max только у integer; values только у enum; persistence; mutation_policy; `states` только у State-сущностей | Error |
| Condition | target/property существуют; оператор допустим для типа; value того же типа; пустые `all`/`any`; ровно одна форма узла; ref существует и указывает на condition; цикл ссылок | Error |
| Event | trigger type известен; фильтры ссылаются на существующие сущности нужного вида; значение `to` у `StateChanged` совпадает по типу; ссылки condition/dialogue/location/event | Error |
| Actions | `set_state`: свойство существует, тип/диапазон/enum значения, mutation_policy допускает Scenario; Actions после `end` в том же списке (недостижимы); CALL self; CALL-цикл | Error |
| Execution type | Background содержит асинхронный Action; Background CALL/START Exclusive | Error |
| Конфликты | пересекающиеся trigger matchers двух Exclusive; один из Events с общим trigger пишет State, читаемый Launch Conditions другого; START-цикл | Warning |

Неиспользуемые свойства State Validator не ищет: мутации Gameplay Systems ему не видны, и такие предупреждения были бы ложными.

## II.3. State System

```csharp
public interface IStateReader
{
    StateReadStatus TryGet(StateAddress address, out StateValue value); // Ok | UnknownEntity | UnknownProperty
}

public enum MutationSource { Scenario, System }
public enum MutationStatus { Applied, Unchanged, Rejected }
public enum MutationError { None, UnknownEntity, UnknownProperty, TypeMismatch, OutOfRange, InvalidEnumValue, MutationNotAllowed }
public readonly struct MutationResult { public MutationStatus Status; public MutationError Error; }

public readonly struct StateChange { public StateAddress Address; public StateValue Old, New; public MutationSource Source; }

public sealed class StateSystem : IStateReader
{
    public StateSystem(ContentRegistry registry);                 // инициализация default-значениями, без уведомлений
    public MutationResult Set(StateAddress address, StateValue value, MutationSource source);
    public event Action<StateChange> Changed;                     // только после commit Applied
    public event Action Restored;
    public StateSnapshot ExportPersistent();
    public RestoreResult Restore(StateSnapshot snapshot);         // проверка целиком, затем применение (A15)
}
```

- Хранилище: плотный массив `StateValue[]`, индекс по `StateAddress` строится при старте. Без копий в других системах.
- `Set`: проверки в порядке 0.3.1 §5.7 → сравнение со старым → запись → `Changed`. Исключение подписчика не откатывает commit (значение уже записано); оно логируется и не прерывает рассылку остальным.
- Повторный вход: `Set` из обработчика `Changed` допустим (Unity-представление так делать не должно, Scenario так делать не может — она лишь кладёт в inbox); порядок уведомлений сохраняется.
- `StateSnapshot` — `IReadOnlyList<(string address, JSON-значение)>` только persistent-свойств; сериализуется в JSON Save-слоем.

## II.4. Condition System

```csharp
public enum ConditionOutcome { True, False, Error }
public enum ConditionError { None, UnknownCondition, UnknownEntity, UnknownProperty, TypeMismatch, InvalidOperator, InvalidDefinition, ConditionCycle }
public readonly struct ConditionResult { public ConditionOutcome Outcome; public ConditionError Error; public string Detail; }

public sealed class ConditionEvaluator
{
    public ConditionEvaluator(ContentRegistry registry, IStateReader state);
    public ConditionResult Evaluate(EntityId conditionId);
    public ConditionResult Evaluate(ConditionNode node);
    public IReadOnlyCollection<StateAddress> GetDependencies(ConditionNode node); // для мониторов (T2)
}
```

- Без short-circuit: все дети вычисляются; любой `Error` → `Error` (0.3.2 §4.4).
- Циклы отсекает Validator; в runtime — защитный стек раскрываемых ссылок, нарушение → `ConditionCycle` (на случай кода, построившего узлы в обход Loader'а, например в тестах).
- Без кэша результатов и без подписок.

## II.5. Scenario System

### Публичный контракт

```csharp
public sealed class ScenarioRuntime
{
    public ScenarioRuntime(ContentRegistry registry, StateSystem state, ConditionEvaluator conditions,
                           IGameFlowPort gameFlow, ScenarioPorts ports, ScenarioLimits limits, IDiagnosticsSink diagnostics);

    public void Start();                                  // индексы, мониторы, baseline; triggers не порождает
    public void Submit(in Trigger trigger);               // от Gameplay Systems; только кладёт в inbox
    public void BeginLocationVisit(EntityId location);    // обычный вход: visitSeq++, затем LocationEntered
    public void CompleteOperation(OperationHandle h, OperationOutcome outcome); // от портов
    public void Pump();                                    // вызывается из Update и после внешних вызовов

    public bool CanSave { get; }                          // T6
    public ScenarioSnapshot Export();
    public void Restore(ScenarioSnapshot snapshot);       // статусы/visit; пересчёт baseline; без triggers
    public void ClearPendingRequests();                   // Game Flow: Loading / Recovery
    public void AbortActiveChain(string reason);          // Game Flow Recovery (T7)
}
```

`Trigger` — readonly struct: `TriggerType`, `TriggerOrigin` (Player | Automatic), и плоские поля `Target`, `InteractionType`, `Item`, `SecondItem`, `Location`, `Dialogue`, `Event`, `StateChange`. Плоская структура вместо иерархии: типов мало, matching механический.

### Порты (сверить с Architecture 0.3 — A12)

```csharp
public interface IGameFlowPort
{
    bool TryAcquireExclusiveControl(EntityId eventId, out ControlToken token);
    void ReleaseExclusiveControl(ControlToken token, ReleaseReason reason); // Completed | Failed | Aborted
}

public readonly struct OperationStart   // результат вызова порта
{
    public OperationStartStatus Status;  // Started(handle) | CompletedImmediately | Failed
    public OperationHandle Handle; public string FailureReason;
}

public interface IDialoguePort            { OperationStart StartDialogue(EntityId dialogueId); }
public interface IMovementPort            { OperationStart Move(/* параметры по A12 */); }
public interface IAnimationPort           { OperationStart Play(/* параметры по A12 */); }
public interface ILocationTransitionPort  { OperationStart Transition(EntityId location); }
public interface IInventoryPort           { OperationResult Apply(/* по A12 */); } // синхронный
```

Контракт портов: операция со статусом `Started` обязана завершиться ровно одним `CompleteOperation`. Нарушение (повтор, неизвестный handle) — Error diagnostic.

### Runtime-структуры

```text
EventRuntime[]  (индекс = позиция Event в отсортированном списке)
    Status:              Idle | Running | Completed | Failed   — последнее исполнение
    EverCompleted:       bool                                 — для once
    LastCompletedVisit:  int (−1 = никогда)                    — для once_per_location_visit

CompiledProgram (на Event, строится при Start из Actions)
    Instruction[]: SetState | Dialogue | Move | Animate | Inventory | Transition
                 | Call | StartEvent | JumpIfFalse(cond, target) | Jump(target) | End

ExecutionChain                         — одна Exclusive (максимум) + временные Background
    Kind, ControlToken?, Stack<Frame>, PendingOperation?
    Frame: EventIndex, ProgramCounter, VisitSeqAtStart

Inbox:          Queue<InboxItem>        — Trigger | StateChange | OperationCompleted
ExclusiveQueue: List<int> + HashSet<int> (FIFO без дублей, T5)
TriggerIndex:   Dictionary<TriggerType, int[]>   (EventIndex по возрастанию ID)
Monitors:       Monitor[] {EventIndex, ConditionNode, Previous}; Dictionary<StateAddress, int[]>
VisitSeq:       int
CascadeCounter: int (T3)
```

Вложенный `if` компилируется в плоские инструкции с переходами: runner — простой цикл по счётчику команд, CALL — новый Frame в той же цепочке (не повторный захват control, Addendum §2), Background — синхронный прогон цепочки до конца.

### Цикл Pump (детерминированный)

```text
Pump():
  если уже внутри Pump — выход
  повторять:
    1. если активная цепочка не ждёт операцию → выполнить ОДНУ инструкцию; перейти к 2
    2. разобрать inbox полностью:
         StateChange       → мониторы по индексу адреса → ConditionsMet-кандидаты;
                             StateChanged-matchers → кандидаты
         Trigger           → matchers → кандидаты
         OperationCompleted → разбудить цепочку (Success → PC+1, Failure → Event Failed)
       кандидаты обрабатываются по правилу A10:
         Background → проверка eligibility + Launch Conditions → синхронный прогон
         Exclusive  → старт (если нет активной и Game Flow даёт control) / в очередь / отказ (Player)
    3. если нет активной цепочки и очередь не пуста → извлечь, перепроверить, запустить
    4. если ни 1, ни 2, ни 3 ничего не сделали → выход; каскад завершён, CascadeCounter = 0
```

### Завершение и ошибки

- Успех (конец программы или `end`) корневого Event → статус Completed, обновить `EverCompleted`/`LastCompletedVisit`, освободить control, `Submit(EventCompleted)`.
- Успех вложенного (CALL) → снять Frame, продолжить caller.
- Failure любого Action → весь стек цепочки разматывается: каждый Event в стеке → Failed (caller получает failure вызываемого, 0.4 §9.3), control освобождается с `ReleaseReason.Failed`, diagnostic с путём `event → action index`. Применённые мутации остаются (Addendum §3).
- `set_state` с `Rejected` — Failure (0.4 §11.1).
- `if` с `Error` — Failure, не ветка `else` (0.4 §9.1).

## II.6. Unity Integration

| Компонент | Ответственность |
|---|---|
| `Boot`-сцена + `GameBootstrap : MonoBehaviour` | composition root: собирает Content → State → Conditions → Scenario и адаптеры; держит их в обычном C#-объекте `GameServices`; `DontDestroyOnLoad`; в `Update` вызывает `ScenarioRuntime.Pump()` |
| `ContentManifest : ScriptableObject` | список `TextAsset` (.json) контента; Editor-кнопка «собрать из папки» `Assets/Paramatrix/Content/` |
| `EntityRepresentation : MonoBehaviour` | `[SerializeField] string entityId`; регистрируется в `RepresentationRegistry` при `OnEnable`; читает State, не хранит его |
| `RepresentationRegistry` | ID → представления; на `StateSystem.Changed` обновляет представления нужной сущности, на `Restored` и после загрузки локации — перестраивает все |
| Адаптеры портов | MonoBehaviour-обёртки над будущими Dialogue/Movement/Location; в этом плане — заглушки, достаточные для сквозного теста |
| `Paramatrix.Unity.Editor` | меню «Paramatrix → Validate Content» (тот же `ContentLoader`, вывод в Console с путями); проверка открытых сцен: каждый `entityId` существует; PropertyDrawer с подсветкой неизвестного ID |

Риски Unity — в II.9. Ключевые договорённости:

- Логика сценариев не живёт в MonoBehaviour: компоненты только переводят ввод в `Trigger` и отображают State (0.4 инв. 6).
- Имена GameObject нигде не используются как ID (0.3.1 §2.3).
- Runner переживает выгрузку сцен локаций (Boot-сцена постоянна, локации грузятся аддитивно) — иначе `transition` внутри Event уничтожает исполнителя.

## II.7. Этапы

Каждый этап заканчивается зелёными тестами в `dotnet test` (этапы 1–7) или в Unity Test Runner (8–10).

| № | Этап | Зависит от | Результат | Критерий завершения |
|---|---|---|---|---|
| 0 | Каркас | — | Unity-проект 6000.4, asmdef'ы, `tools/*.csproj`, git-репозиторий, пакет Newtonsoft (A13) | пустой тест проходит и в `dotnet test`, и в Unity EditMode |
| 1 | Основа Content | 0 | `EntityId`, `StateAddress`, `StateValue`, `Diagnostic`, `SourceLocation`, JSON-ридер с путями | юнит-тесты форматов ID и значений |
| 2 | Entity/State definitions + Validator + Registry | 1 | загрузка сущностей со State, правила групп «Структура/ID/State» | каждая строка правил покрыта негативным fixture с ожидаемым кодом; registry неизменяем |
| 3 | State System | 2 | `StateSystem` целиком, snapshot/restore | тесты 0.3.1 §11 «State System» |
| 4 | Condition System | 2, 3 | узлы, named conditions, evaluator, зависимости, правила Validator «Condition» | тесты 0.3.2 §11 «Atomic/Compound/Named» |
| 5 | Scenario definitions + Validator | 2, 4 | Events, Triggers, Actions, компиляция программ, правила «Event/Actions/Execution type/Конфликты» | негативные fixtures на каждое правило; CALL-цикл, Background-нарушения |
| 6 | Scenario runtime: ядро | 5 | inbox, Pump, matching, lifecycle, repeat policies, Exclusive-очередь, Background, `set_state`, `if`, `end`, CALL, START, failure | тесты 0.4 §18 «Trigger System/Event Lifecycle/Execution» на фейковых портах |
| 7 | Scenario runtime: асинхронность и сохранение | 6 | порты, операции, `CompleteOperation`, Visits, мониторы ConditionsMet, Export/Restore Scenario, `CanSave`, Abort | тесты 0.3.2 §11 «Integration», Addendum §1/§4, round-trip Save |
| 8 | Unity: bootstrap и представления | 3, 7 | `GameBootstrap`, `ContentManifest`, `RepresentationRegistry`, `EntityRepresentation` | PlayMode: старт, изменение State меняет визуал; Restore перестраивает визуал |
| 9 | Unity: сквозной срез | 8 | демо-контент «дверь офиса» + заглушки Interaction/Dialogue/Transition/Game Flow | PlayMode: клик → Event → диалог → переход → `LocationEntered`; Save → Load без повторных triggers |
| 10 | Инструменты автора | 2, 5, 8 | Editor-валидация контента и сцен; при желании `.schema.json` для автодополнения | ошибка в JSON видна в Console с файлом и путём; неизвестный `entityId` в сцене подсвечен |

```text
0 → 1 → 2 ─┬→ 3 ─┐
           └→ 4 ←┘ → 5 → 6 → 7 → 8 → 9
                 2,5,8 → 10
```

Этапы 3 и 4 частично параллельны (4 нужен только `IStateReader` из 3). Этап 10 можно начать после 5 в части валидации контента.

Порядок обоснован: каждый этап проверяем без следующего; Unity появляется только после того, как вся семантика проверена без Unity.

## II.8. Стратегия тестирования

1. **Юнит-тесты Core** (NUnit; один набор, две среды — `dotnet test` и Unity EditMode).
2. **Content fixtures.** `Tests/Fixtures/valid/*.json` и `invalid/*.json`; для каждого invalid-файла — ожидаемый список `(code, json path)`. Покрытие = каждая строка таблицы правил Validator.
3. **Сценарный harness.** `ScenarioHarness` поднимает Core из JSON-строк с фейковыми портами (`FakeDialoguePort` с ручным `Complete`, `FakeGameFlow` с переключаемым отказом) и даёт сценарный DSL теста:
   `harness.Submit(Interaction("object.office_door")); harness.Pump(); harness.CompleteDialogue(); Assert.State("object.office_door.open", true); Assert.EventStatus(...); Assert.Diagnostics(...)`.
   Порядок вызовов портов записывается в журнал — детерминизм проверяется сравнением журналов двух прогонов.
4. **Save round-trip.** Сыграть → Export → новый экземпляр → Restore → проверить State, статусы, visit, baseline мониторов, отсутствие triggers и `StateChanged`.
5. **PlayMode smoke** (немного): bootstrap, представления, сквозной срез.
6. **Трассировка критериев приёмки.** Таблица «пункт Acceptance Criteria (0.3.1 §13, 0.3.2 §13, 0.4 §20) → тесты» ведётся в репозитории; этап не закрыт, пока его пункты не покрыты.

Обязательные «острые» тесты сверх списков в документах:

- `StateChanged` не вызывается при `Unchanged` и `Rejected`; подписчик, бросивший исключение, не откатывает commit.
- `Set` внутри `Changed`-обработчика не обрабатывается Scenario до выхода из `Set` (inbox).
- Монитор: True при регистрации → не срабатывает; False→True → срабатывает один раз; True→True → нет; True→False→True → снова; Restore с True → нет.
- `once`: Failed → новый trigger запускает (по A3); Completed → не запускает; CALL completed-once → Failure caller.
- `once_per_location_visit`: Completed → новый trigger в том же Visit отклонён; после `BeginLocationVisit` — разрешён; после Save/Load в том же Visit — отклонён.
- Exclusive во время Exclusive: автоматический — в очереди, player — отклонён; Launch Conditions стали False до извлечения — request снят.
- Failure во вложенном CALL: все Events стека Failed, control освобождён ровно один раз.
- Предохранитель каскада: два Background, запускающих друг друга через State, — останавливаются с diagnostic, а не зависают.
- Несколько Events на один trigger — порядок по ID, одинаковый в разных прогонах и при перестановке файлов.

## II.9. Риски

| Риск | Влияние | Мера |
|---|---|---|
| Не передана Architecture 0.3 | порты Scenario могут не совпасть с реальными Dialogue/Location/Game Flow | порты минимальны; этапы 0–7 от них не зависят; сверка до этапа 7 |
| Зависшая асинхронная операция | Exclusive chain навсегда блокирует игру | контракт «ровно одно завершение», `AbortActiveChain`, diagnostic при уничтожении представления с активной операцией |
| Выгрузка сцены во время Event | уничтожение runner'а или целей операций | постоянная Boot-сцена; Core не хранит ссылок на GameObject |
| Отключённый Domain Reload | состояние прошлого Play Mode протекает в следующий | запрет статического изменяемого состояния; всё в `GameServices` |
| Опечатки в строковых ID в сценах | тихо неработающие объекты | Editor-проверка сцен (этап 10), Error при регистрации неизвестного ID в runtime |
| Расхождение C# версий Unity и `dotnet` | код компилируется в консоли, но не в Unity | `LangVersion 9`, `netstandard2.1`; этап 0 проверяет обе среды |
| IL2CPP stripping | падение десериализации в сборке | ручной разбор `JToken`, без reflection |
| Ручное написание больших JSON-сценариев | ошибки, низкая скорость авторинга | точные пути в diagnostics; schema для автодополнения; редактор сценариев — вне MVP, решать по факту объёма |
| Порядок по Event ID | переименование меняет поведение | Warning Validator'а о пересечениях; явный порядок через CALL/START |
| Семантика без решений A1–A11 | переделка Scenario runtime | этапы 0–4 от A1–A11 почти не зависят (кроме A9, A15, A16); Scenario начинать после согласования |
| Нет git-репозитория | нет истории и отката | `git init` в этапе 0 |

## II.10. Точки интеграции

| Откуда | Куда | Механизм |
|---|---|---|
| Unity `ContentManifest` | `ContentLoader` | `ContentSource[]` |
| `StateSystem.Changed` | Scenario (inbox), `RepresentationRegistry` | C#-событие после commit |
| `StateSystem.Restored` | `RepresentationRegistry`; Scenario (только baseline) | C#-событие |
| Interaction / Inventory / Location / Dialogue | Scenario | `Submit(Trigger)`, `BeginLocationVisit` |
| Scenario | Gameplay Systems | порты, `OperationStart` |
| Gameplay Systems | Scenario | `CompleteOperation` |
| Scenario ↔ Game Flow | control | `IGameFlowPort`; `ClearPendingRequests`, `AbortActiveChain` |
| Save System | State, Scenario | `ExportPersistent`/`Export`, `Restore`/`Restore`, `CanSave` (по A1-B) |
| Unity `Update` | Scenario | `Pump()` |

## II.11. Вне этого плана

Реализация Dialogue, Movement, Animation, Inventory, Interaction, Location, Game Flow, Save на диск (кроме заглушек этапа 9); UI; локализация; редактор сценариев; миграции Save; hot reload контента; всё перечисленное в «Out of Scope» 0.3.1 §12, 0.3.2 §12, 0.4 §19.

---

## Вопросы на согласование (сводка)

| # | Вопрос | Рекомендация | Блокирует этап |
|---|---|---|---|
| A1 | Хранение и сохранение runtime-состояния Scenario | B: свой `ScenarioSnapshot`, правка 0.3.1 §9 | 7 |
| A2 | Первый `LocationEntered` новой игры | New Game делает обычный вход после инициализации Scenario | 7, 9 |
| A3 | Failed Event и новый trigger | новый trigger запускает снова | 6 |
| A4 | CALL/START недоступного Event | таблица A4 | 6 |
| A5 | Exclusive → START Exclusive | в очередь | 5 |
| A6 | Background без ожиданий | да | 5 |
| A7 | WAIT неявный в асинхронных Actions | да | 5 |
| A8 | END неявный | да | 5 |
| A9 | `reconstructible` | убрать из MVP | 2 |
| A10 | Порядок при нескольких совпадениях | по Event ID, правила A10 | 6 |
| A11 | Trigger types MVP | 9 типов, 3 отложить | 5 |
| A12 | Architecture 0.3 / контракты систем | передать документ или подтвердить «только порты» | 7 |
| A13 | Зависимость Newtonsoft.Json | `com.unity.nuget.newtonsoft-json` | 0 |
| A14 | Целевые платформы | TextAsset + manifest | 8 |
| A15 | Совместимость Save | пропуск удалённого, отказ при ошибке типа | 3 |
| A16 | Ссылки на сущности в State | Enum со значениями-ID | 2 |
