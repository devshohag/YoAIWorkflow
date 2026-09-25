using YoAIWorkflow.Abstractions;

namespace YoAIWorkflow.Core;

/// <summary>Dispatches action intents after a workflow has produced a transition.</summary>
public sealed class WorkflowActionExecutor<TState>
{
    private readonly IReadOnlyDictionary<string, IWorkflowActionHandler<TState>> _handlers;

    public WorkflowActionExecutor(IEnumerable<IWorkflowActionHandler<TState>> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        var registry = new Dictionary<string, IWorkflowActionHandler<TState>>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentException.ThrowIfNullOrWhiteSpace(handler.ActionName);

            if (!registry.TryAdd(handler.ActionName, handler))
            {
                throw new ArgumentException(
                    $"More than one handler was registered for '{handler.ActionName}'.",
                    nameof(handlers));
            }
        }

        _handlers = registry;
    }

    public async Task<WorkflowActionExecutionResult> ExecuteAsync(
        WorkflowTransition<TState> transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(transition.Actions);
        cancellationToken.ThrowIfCancellationRequested();

        // Validate the complete batch before starting any external work.
        var actions = transition.Actions.ToArray();
        for (var index = 0; index < actions.Length; index++)
        {
            var action = actions[index];
            if (action is null || string.IsNullOrWhiteSpace(action.Name))
            {
                throw new ArgumentException("Every action must have a nonempty name.", nameof(transition));
            }

            if (!_handlers.ContainsKey(action.Name))
            {
                return new WorkflowActionExecutionResult(
                    0,
                    new ActionExecutionFailure(index, action, ActionFailureKind.MissingHandler));
            }
        }

        for (var index = 0; index < actions.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = actions[index];

            try
            {
                await _handlers[action.Name]
                    .HandleAsync(action, transition.State, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new WorkflowActionExecutionResult(
                    index,
                    new ActionExecutionFailure(index, action, ActionFailureKind.HandlerThrew, exception));
            }
        }

        return new WorkflowActionExecutionResult(actions.Length, null);
    }
}

