# Параматрица — Architecture 0.4
## Semantic Addendum

**Status:** Architectural clarification  
**Purpose:** уточнение семантики Scenario System перед разработкой Implementation Plan.

Этот документ дополняет Architecture 0.4. При противоречии с базовой архитектурой перечисленные уточнения имеют приоритет.

Addendum фиксирует поведение системы, но не предписывает конкретную C# реализацию, структуру классов или интерфейсов.

---

## 1. Event Execution Status vs Completion Eligibility

Execution status и completion history — разные понятия.

Execution status описывает текущее или последнее выполнение Event:

- Idle
- Running
- Completed
- Failed

Completion eligibility определяет, разрешено ли Event запуститься повторно с учётом Repeat Policy.

### Repeat Policies

**Once**
- Event может успешно завершиться только один раз за playthrough.
- После Completed последующие попытки запуска отклоняются.
- Failed execution не считается завершением.

**Repeatable**
- Event может запускаться повторно после Completed.
- Completed не запрещает последующие запуски.
- Одновременно может существовать только одна execution instance данного Event.

**Once Per Location Visit**
- Event может успешно завершиться один раз в рамках текущего Location Visit.
- При новом обычном входе в локацию начинается новый Visit.
- Restore не создаёт новый Visit.
- Completion history должна сохраняться и восстанавливаться так, чтобы Restore не позволял повторно запускать Event в рамках того же Visit.

Failed execution не считается Completed и не расходует completion eligibility.

---

## 2. CALL Semantics

CALL — синхронное вложенное выполнение Event.

- Caller приостанавливается до завершения Called Event.
- После успешного завершения Called Event Caller продолжает выполнение.
- Если Called Event завершается с Failed, failure передаётся Caller.
- CALL не создаёт независимую параллельную execution chain.
- CALL не должен приводить к повторному захвату Exclusive control уже активной Exclusive chain.
- Self-call и CALL Event, который уже Running, запрещены.
- Ограничение максимальной глубины CALL обязательно.

CALL не эквивалентен START.

### Execution Type Compatibility

- Exclusive → CALL Exclusive: разрешено, вложенное выполнение в рамках той же Exclusive chain.
- Exclusive → CALL Background: допустимо, если соблюдаются ограничения Background.
- Background → CALL Background: допустимо.
- Background → CALL Exclusive: запрещено.

Конкретный механизм реализации nested execution определяется Implementation Plan.

---

## 3. Event Failure and State Mutations

Scenario System не предоставляет универсальную транзакционность или rollback.

Если Event завершается Failed:

- выполнение Event прекращается;
- уже успешно применённые State mutations сохраняются;
- последующие Actions не выполняются;
- Exclusive control освобождается;
- Game Flow возвращается в безопасное состояние;
- failure фиксируется в diagnostics.

Не предполагается автоматический retry.

Failed execution не считается Completed и не расходует completion eligibility.

Recovery и повторный запуск могут происходить только через явно предусмотренную логику.

---

## 4. ConditionsMet Monitoring

ConditionsMet реагирует на переход условия:

`False → True`

- Scenario System хранит предыдущий результат мониторинга.
- При регистрации monitor baseline определяется текущим результатом Condition.
- Если условие уже True при регистрации, Event немедленно не запускается.
- Restore не должен создавать искусственный переход False → True.
- Condition System не хранит состояние мониторинга и не запускает Events самостоятельно.

---

## 5. Multiple Events Matching One Trigger

Архитектура не должна предполагать, что несколько Events, совпавших с одним Trigger, выполняются параллельно или в произвольном порядке.

- Exclusive Events сериализуются согласно правилам Exclusive arbitration.
- Background Events могут выполняться независимо в пределах своих ограничений.
- Сценарий не должен зависеть от случайного порядка обработки Events.
- Если требуется определённая последовательность, она должна быть явно выражена через CALL/START или другую предусмотренную сценарную конструкцию.

Implementation Plan должен предложить детерминированную обработку таких случаев и указать, какие конфликты можно выявить Validator.

---

## 6. Scope of This Addendum

Документ фиксирует семантику, а не реализационные решения.

Claude Code должен:
- учитывать эти требования при разработке Implementation Plan;
- выявлять противоречия с Architecture 0.3.1, 0.3.2 и 0.4;
- обозначать технические вопросы, которые остаются открытыми;
- не менять семантику без явного согласования.

Этот документ не является разрешением на немедленную реализацию.
