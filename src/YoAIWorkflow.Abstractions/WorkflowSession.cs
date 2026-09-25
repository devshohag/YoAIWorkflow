namespace YoAIWorkflow.Abstractions;

/// <summary>A persisted decision and its action completion checkpoints.</summary>
public sealed record WorkflowSession<TState>(
    string WorkflowId,
    TState State,
    long Revision,
    IReadOnlyList<string> ProcessedInputIds,
    IReadOnlyList<PendingWorkflowAction> Actions)
{
    public bool HasPendingActions => Actions.Any(action => !action.Completed);
}

public sealed record PendingWorkflowAction(
    string ActionId,
    WorkflowAction Action,
    bool Completed);
