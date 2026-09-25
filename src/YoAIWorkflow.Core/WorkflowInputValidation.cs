namespace YoAIWorkflow.Core;

/// <summary>Result of validating an input against the current persisted state.</summary>
public sealed record WorkflowInputValidation(bool IsValid, string? Reason)
{
    public static WorkflowInputValidation Allow() => new(true, null);

    public static WorkflowInputValidation Reject(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new WorkflowInputValidation(false, reason);
    }
}
