namespace Paramatrix.Content
{
    /// <summary>
    /// Stable diagnostic codes. Tests and tooling match on these, never on message text.
    /// New codes are appended; existing names are not renamed.
    /// </summary>
    public enum DiagnosticCode
    {
        // JSON structure
        InvalidJson = 1,
        UnexpectedJsonType,
        MissingRequiredField,
        UnknownField,
        UnsupportedFormatVersion,

        // Identifiers and references
        InvalidEntityId,
        InvalidPropertyName,
        DuplicateId,
        UnknownReference,
        InvalidReferenceKind,

        // Definitions
        UnsupportedDefinitionKind,
        EmptyText,
        InvalidInteractionType,
        MissingGameSettings,
        PlayerCharacterWithoutLocation,

        // State definitions
        InvalidStateType,
        InvalidPersistence,
        InvalidMutationPolicy,
        InvalidDefaultValue,
        InvalidRange,
        FieldNotApplicable,
        EmptyEnumValues,
        DuplicateEnumValue,
        InvalidEnumValue,
        BuiltInPropertyRedeclared,

        // State System runtime
        StateSubscriberFailed,
        SnapshotUnknownEntity,
        SnapshotUnknownProperty,
        SnapshotNotPersistent,
        SnapshotDuplicateEntry,
        SnapshotMissingEntry,
        SnapshotInvalidValue,

        // Conditions
        InvalidConditionForm,
        EmptyCompoundCondition,
        UnknownStateProperty,
        InvalidOperator,
        ConditionValueTypeMismatch,
        InvalidComparisonValue,
        ConditionCycle,

        // Events, actions, dialogues, responses
        InvalidExecutionType,
        InvalidRepeatPolicy,
        InvalidTriggerType,
        FilterNotApplicable,
        EmptyActions,
        InvalidActionType,
        UnreachableAction,
        ActionMutationNotAllowed,
        InvalidHolderValue,
        SelfCall,
        CallCycle,
        StartCycle,
        BackgroundActionNotAllowed,
        BackgroundCallsExclusive,
        InvalidDialogueMode,
        EmptyDialogue,
        InvalidResponseTier,
        InvalidResponseText,
        MissingFallbackResponse,
        DuplicateFallbackResponse,
        GenericPoolTooSmall,
        DuplicateResponse,
        InvalidValue,

        // Scenario runtime
        MutationRejected,
        ConditionEvaluationFailed,
        OperationFailed,
        OperationCancelled,
        OperationContractViolation,
        InventoryOperationFailed,
        CallUnavailable,
        StartUnavailable,
        EventBlockedByFailure,
        AmbiguousPlayerAction,
        AmbiguousResponse,
        PlayerActionRejected,
        CascadeLimitExceeded,
        CallDepthExceeded,
        ChainAborted,

        // Gameplay systems
        ApproachFailed,
        SceneLoadFailed,
        RecoveryEntered,
        SaveFailed,
        SaveFileInvalid,

        // Unity layer
        StartupFailed,
    }
}
