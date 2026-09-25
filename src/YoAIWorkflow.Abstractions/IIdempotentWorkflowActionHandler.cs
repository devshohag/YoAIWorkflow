namespace YoAIWorkflow.Abstractions;

/// <summary>
/// Handles an action using its stable ID to deduplicate external effects across retries.
/// An implementation must persist its idempotency decision with its business effect.
/// </summary>
public interface IIdempotentWorkflowActionHandler<TState>
{
    string ActionName { get; }

    Task HandleAsync(
        string actionId,
        WorkflowAction action,
        TState state,
        CancellationToken cancellationToken = default);
}
