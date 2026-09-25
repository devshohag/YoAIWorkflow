namespace YoAIWorkflow.Abstractions;

/// <summary>Defines one state transition for a particular workflow and input type.</summary>
public interface IWorkflow<TState, TInput>
{
    WorkflowTransition<TState> Apply(TState state, TInput input);
}

