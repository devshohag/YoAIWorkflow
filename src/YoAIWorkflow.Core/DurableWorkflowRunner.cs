using System.Security.Cryptography;
using System.Text;
using YoAIWorkflow.Abstractions;

namespace YoAIWorkflow.Core;

/// <summary>
/// Saves decisions before dispatch and checkpoints completed actions. Delivery is at least once;
/// handlers must deduplicate effects using the supplied action ID.
/// </summary>
public sealed class DurableWorkflowRunner<TState, TInput>
{
    private readonly WorkflowEngine<TState, TInput> _engine;
    private readonly IWorkflowSessionStore<TState> _store;
    private readonly IReadOnlyDictionary<string, IIdempotentWorkflowActionHandler<TState>> _handlers;

    public DurableWorkflowRunner(
        IWorkflow<TState, TInput> workflow,
        IWorkflowSessionStore<TState> store,
        IEnumerable<IIdempotentWorkflowActionHandler<TState>> handlers)
    {
        _engine = new WorkflowEngine<TState, TInput>(workflow);
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(handlers);

        var registry = new Dictionary<string, IIdempotentWorkflowActionHandler<TState>>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentException.ThrowIfNullOrWhiteSpace(handler.ActionName);
            if (!registry.TryAdd(handler.ActionName, handler))
            {
                throw new ArgumentException($"Duplicate action handler: {handler.ActionName}", nameof(handlers));
            }
        }

        _handlers = registry;
    }

    public async Task<WorkflowSession<TState>> StartAsync(
        string workflowId, TState initialState, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        ArgumentNullException.ThrowIfNull(initialState);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = await _store.LoadAsync(workflowId, cancellationToken).ConfigureAwait(false);
            if (existing is not null) return existing;

            var initial = new WorkflowSession<TState>(workflowId, initialState, 0, [], []);
            if (await _store.TrySaveAsync(initial, -1, cancellationToken).ConfigureAwait(false)) return initial;
        }
    }

    public async Task<WorkflowSession<TState>> ProcessAsync(
        string workflowId, string inputId, TInput input, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputId);
        ArgumentNullException.ThrowIfNull(input);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await _store.LoadAsync(workflowId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Start the workflow before processing input.");
            if (current.ProcessedInputIds.Contains(inputId, StringComparer.Ordinal)) return current;
            if (current.HasPendingActions)
                throw new InvalidOperationException("Dispatch pending actions before processing another input.");

            var transition = _engine.Process(current.State, input);
            ArgumentNullException.ThrowIfNull(transition.Actions);
            var actions = transition.Actions.Select((action, index) =>
            {
                if (action is null || string.IsNullOrWhiteSpace(action.Name))
                    throw new InvalidOperationException("Workflow returned an invalid action.");
                return new PendingWorkflowAction(MakeActionId(workflowId, inputId, index), action, false);
            }).ToArray();
            var next = current with
            {
                State = transition.State,
                Revision = checked(current.Revision + 1),
                ProcessedInputIds = current.ProcessedInputIds.Append(inputId).ToArray(),
                Actions = actions
            };
            if (await _store.TrySaveAsync(next, current.Revision, cancellationToken).ConfigureAwait(false))
                return next;
        }
    }

    public async Task<WorkflowActionExecutionResult> DispatchAsync(
        string workflowId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await _store.LoadAsync(workflowId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Start the workflow before dispatching actions.");

        // Refuse the complete batch when any pending action has no handler.
        for (var index = 0; index < snapshot.Actions.Count; index++)
        {
            var pending = snapshot.Actions[index];
            if (!pending.Completed && !_handlers.ContainsKey(pending.Action.Name))
                return new WorkflowActionExecutionResult(0,
                    new ActionExecutionFailure(index, pending.Action, ActionFailureKind.MissingHandler));
        }

        var completed = 0;
        for (var index = 0; index < snapshot.Actions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pending = snapshot.Actions[index];
            if (pending.Completed) continue;

            var before = await _store.LoadAsync(workflowId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The workflow session disappeared.");
            if (!before.Actions.Any(a => a.ActionId == pending.ActionId && !a.Completed)) continue;

            try
            {
                await _handlers[pending.Action.Name]
                    .HandleAsync(pending.ActionId, pending.Action, snapshot.State, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                return new WorkflowActionExecutionResult(completed,
                    new ActionExecutionFailure(index, pending.Action, ActionFailureKind.HandlerThrew, exception));
            }

            // Another dispatcher may have completed the same action. Handlers still need to
            // enforce their own idempotency for concurrent dispatch and the crash window.
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var latest = await _store.LoadAsync(workflowId, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The workflow session disappeared.");
                var position = latest.Actions.ToList().FindIndex(a => a.ActionId == pending.ActionId);
                // A different dispatcher may have completed the batch and a subsequent
                // input may already have replaced its action list.
                if (position < 0) break;
                if (latest.Actions[position].Completed) break;
                var updated = latest.Actions.ToArray();
                updated[position] = updated[position] with { Completed = true };
                var next = latest with { Revision = checked(latest.Revision + 1), Actions = updated };
                if (await _store.TrySaveAsync(next, latest.Revision, cancellationToken).ConfigureAwait(false)) break;
            }

            completed++;
        }

        return new WorkflowActionExecutionResult(completed, null);
    }

    private static string MakeActionId(string workflowId, string inputId, int index)
    {
        var value = $"{workflowId.Length}:{workflowId}{inputId.Length}:{inputId}{index}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
