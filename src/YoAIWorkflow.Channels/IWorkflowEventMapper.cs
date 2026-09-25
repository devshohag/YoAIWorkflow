namespace YoAIWorkflow.Channels;

/// <summary>Implemented by a host to translate any external event into a typed workflow input.</summary>
public interface IWorkflowEventMapper<TExternalEvent, TInput>
{
    /// <summary>Return null when the event cannot be mapped; do not call external actions here.</summary>
    Task<WorkflowInboundEvent<TInput>?> MapAsync(
        TExternalEvent externalEvent, CancellationToken cancellationToken = default);
}

/// <summary>IDs are supplied by the host and must be stable across delivery retries.</summary>
public sealed record WorkflowInboundEvent<TInput>(
    string WorkflowId,
    string InputId,
    TInput Input);
