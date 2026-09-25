using YoAIWorkflow.Core;

namespace YoAIWorkflow.AI;

/// <summary>
/// Converts an untrusted proposal into a validated, persisted workflow input.
/// Dispatch remains an explicit host operation after an accepted decision.
/// </summary>
public sealed class AIWorkflowInputAdapter<TState, TContext, TInput>
{
    private readonly IAIInputSource<TContext, TInput> _source;
    private readonly IAIInputValidator<TState, TInput> _validator;
    private readonly DurableWorkflowRunner<TState, TInput> _runner;
    private readonly decimal _minimumConfidence;

    public AIWorkflowInputAdapter(
        IAIInputSource<TContext, TInput> source,
        IAIInputValidator<TState, TInput> validator,
        DurableWorkflowRunner<TState, TInput> runner,
        decimal minimumConfidence = 0.8m)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        if (minimumConfidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(minimumConfidence));
        _minimumConfidence = minimumConfidence;
    }

    public async Task<AIProposalResult<TState>> ProcessAsync(
        string workflowId, string inputId, TContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputId);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var proposal = await _source.ProposeAsync(context, cancellationToken).ConfigureAwait(false);
        if (proposal is null)
            return new AIProposalResult<TState>(null, AIProposalRejection.InvalidProposal);
        var input = proposal.Input;
        if (input is null || proposal.Confidence is < 0 or > 1)
            return new AIProposalResult<TState>(null, AIProposalRejection.InvalidProposal);
        if (proposal.Confidence < _minimumConfidence)
            return new AIProposalResult<TState>(null, AIProposalRejection.LowConfidence);

        // The runner reloads the current state and repeats validation after revision conflicts.
        var decision = await _runner.ProcessValidatedAsync(workflowId, inputId, input,
            _validator.Validate, cancellationToken).ConfigureAwait(false);
        return decision.Accepted
            ? new AIProposalResult<TState>(decision.Session, null)
            : new AIProposalResult<TState>(null, AIProposalRejection.BusinessRuleRejected,
                decision.RejectionReason);
    }
}
