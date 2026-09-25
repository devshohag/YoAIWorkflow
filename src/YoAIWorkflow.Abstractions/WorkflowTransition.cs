namespace YoAIWorkflow.Abstractions;

/// <summary>The next state and the business actions requested by a workflow.</summary>
public sealed record WorkflowTransition<TState>(TState State, IReadOnlyList<WorkflowAction> Actions)
{
    public static WorkflowTransition<TState> WithoutActions(TState state) =>
        new(state, Array.Empty<WorkflowAction>());
}

