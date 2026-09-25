using YoAIWorkflow.Core;

namespace YoAIWorkflow.Channels;

/// <summary>
/// Routes an external event through a host mapper and a business guard into a durable
/// workflow decision. The SDK does not collect events or execute resulting actions.
/// </summary>
public sealed class WorkflowInputRouter<TExternalEvent, TState, TInput>
{
    private readonly IWorkflowEventMapper<TExternalEvent, TInput> _mapper;
    private readonly DurableWorkflowRunner<TState, TInput> _runner;
    private readonly Func<TState, TInput, WorkflowInputValidation> _guard;

    public WorkflowInputRouter(
        IWorkflowEventMapper<TExternalEvent, TInput> mapper,
        DurableWorkflowRunner<TState, TInput> runner,
        Func<TState, TInput, WorkflowInputValidation> guard)
    {
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    public async Task<WorkflowInputRouteResult<TState>> RouteAsync(
        TExternalEvent externalEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(externalEvent);
        cancellationToken.ThrowIfCancellationRequested();
        var mapped = await _mapper.MapAsync(externalEvent, cancellationToken).ConfigureAwait(false);
        if (mapped is null)
            return new WorkflowInputRouteResult<TState>(null, WorkflowInputRejection.UnmappedEvent);
        if (string.IsNullOrWhiteSpace(mapped.WorkflowId) ||
            string.IsNullOrWhiteSpace(mapped.InputId) || mapped.Input is null)
            return new WorkflowInputRouteResult<TState>(null, WorkflowInputRejection.InvalidEvent);

        var decision = await _runner.ProcessValidatedAsync(
            mapped.WorkflowId, mapped.InputId, mapped.Input, _guard, cancellationToken)
            .ConfigureAwait(false);
        return decision.Accepted
            ? new WorkflowInputRouteResult<TState>(decision.Session, null)
            : new WorkflowInputRouteResult<TState>(null,
                WorkflowInputRejection.BusinessRuleRejected, decision.RejectionReason);
    }
}
