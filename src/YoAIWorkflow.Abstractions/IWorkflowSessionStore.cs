namespace YoAIWorkflow.Abstractions;

/// <summary>Stores complete session snapshots with an atomic revision comparison.</summary>
public interface IWorkflowSessionStore<TState>
{
    Task<WorkflowSession<TState>?> LoadAsync(
        string workflowId,
        CancellationToken cancellationToken = default);

    /// <summary>For a new session expectedRevision is -1; otherwise it is the last loaded revision.</summary>
    Task<bool> TrySaveAsync(
        WorkflowSession<TState> session,
        long expectedRevision,
        CancellationToken cancellationToken = default);
}
