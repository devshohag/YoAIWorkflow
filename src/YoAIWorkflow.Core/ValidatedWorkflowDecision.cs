using YoAIWorkflow.Abstractions;

namespace YoAIWorkflow.Core;

/// <summary>Either a persisted decision or an input rejected before any transition.</summary>
public sealed record ValidatedWorkflowDecision<TState>(
    WorkflowSession<TState>? Session,
    string? RejectionReason)
{
    public bool Accepted => Session is not null;
}
