using YoAIWorkflow.Abstractions;

namespace YoAIWorkflow.Core;

/// <summary>Identifies the action where execution stopped.</summary>
public sealed record ActionExecutionFailure(
    int ActionIndex,
    WorkflowAction Action,
    ActionFailureKind Kind,
    Exception? Exception = null);

