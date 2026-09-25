using YoAIWorkflow.Abstractions;

namespace YoAIWorkflow.AI;

public enum AIProposalRejection
{
    InvalidProposal,
    LowConfidence,
    BusinessRuleRejected
}

/// <summary>Rejected proposals have no persisted decision or action intents.</summary>
public sealed record AIProposalResult<TState>(
    WorkflowSession<TState>? Session,
    AIProposalRejection? Rejection,
    string? Detail = null)
{
    public bool Accepted => Session is not null;
}
