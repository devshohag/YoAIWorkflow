namespace YoAIWorkflow.Core;

/// <summary>Counts actions completed before the first failure.</summary>
public sealed record WorkflowActionExecutionResult(
    int CompletedCount,
    ActionExecutionFailure? Failure)
{
    public bool Succeeded => Failure is null;
}

