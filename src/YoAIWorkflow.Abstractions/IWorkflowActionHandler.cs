namespace YoAIWorkflow.Abstractions;

/// <summary>Executes one named business action using the decided workflow state.</summary>
public interface IWorkflowActionHandler<TState>
{
    string ActionName { get; }

    Task HandleAsync(
        WorkflowAction action,
        TState state,
        CancellationToken cancellationToken = default);
}

