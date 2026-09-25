using YoAIWorkflow.Abstractions;

namespace YoAIWorkflow.Channels;

public enum WorkflowInputRejection
{
    UnmappedEvent,
    InvalidEvent,
    BusinessRuleRejected
}

/// <summary>Acceptance persists a decision; it never dispatches an action.</summary>
public sealed record WorkflowInputRouteResult<TState>(
    WorkflowSession<TState>? Session,
    WorkflowInputRejection? Rejection,
    string? Detail = null)
{
    public bool Accepted => Session is not null;
}
