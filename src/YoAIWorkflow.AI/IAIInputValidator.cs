using YoAIWorkflow.Core;

namespace YoAIWorkflow.AI;

/// <summary>Checks proposed input against business rules and the current workflow state.</summary>
public interface IAIInputValidator<TState, TInput>
{
    WorkflowInputValidation Validate(TState state, TInput input);
}
