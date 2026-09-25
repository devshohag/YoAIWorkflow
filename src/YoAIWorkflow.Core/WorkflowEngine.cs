using YoAIWorkflow.Abstractions;

namespace YoAIWorkflow.Core;

/// <summary>Runs a typed workflow decision without executing external actions.</summary>
public sealed class WorkflowEngine<TState, TInput>
{
    private readonly IWorkflow<TState, TInput> _workflow;

    public WorkflowEngine(IWorkflow<TState, TInput> workflow) =>
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));

    public WorkflowTransition<TState> Process(TState state, TInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);

        return _workflow.Apply(state, input)
            ?? throw new InvalidOperationException("A workflow must return a transition.");
    }
}

