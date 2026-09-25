using OrderConfirmation;
using Xunit;
using YoAIWorkflow.Abstractions;
using YoAIWorkflow.Core;

namespace YoAIWorkflow.Tests;

public sealed class DurableWorkflowTests
{
    [Fact]
    public async Task Restart_restores_decision_and_pending_action_without_reapplying_input()
    {
        var directory = NewDirectory();
        try
        {
            var effects = new HashSet<string>();
            var handler = new DeduplicatingHandler("order.confirmed", effects);
            var first = OrderRunner(new FileWorkflowSessionStore<OrderState>(directory), handler);
            await first.StartAsync("order-1", new OrderState("order-1", OrderStatus.Pending));
            var decision = await first.ProcessAsync("order-1", "reply-1", CustomerReply.Confirm);

            var second = OrderRunner(new FileWorkflowSessionStore<OrderState>(directory), handler);
            var repeated = await second.ProcessAsync("order-1", "reply-1", CustomerReply.Confirm);
            Assert.Equal(decision.Revision, repeated.Revision);
            Assert.Equal(decision.Actions[0].ActionId, repeated.Actions[0].ActionId);
            Assert.True(repeated.HasPendingActions);

            Assert.True((await second.DispatchAsync("order-1")).Succeeded);
            var third = OrderRunner(new FileWorkflowSessionStore<OrderState>(directory), handler);
            Assert.Equal(0, (await third.DispatchAsync("order-1")).CompletedCount);
            Assert.Single(effects);
            Assert.False((await new FileWorkflowSessionStore<OrderState>(directory)
                .LoadAsync("order-1"))!.HasPendingActions);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Failure_after_external_effect_reuses_same_id_on_retry()
    {
        var directory = NewDirectory();
        try
        {
            var effects = new HashSet<string>();
            var handler = new DeduplicatingHandler("order.confirmed", effects) { FailAfterEffectOnce = true };
            var runner = OrderRunner(new FileWorkflowSessionStore<OrderState>(directory), handler);
            await runner.StartAsync("order-2", new OrderState("order-2", OrderStatus.Pending));
            var decision = await runner.ProcessAsync("order-2", "reply-2", CustomerReply.Confirm);

            var failed = await runner.DispatchAsync("order-2");
            Assert.Equal(ActionFailureKind.HandlerThrew, failed.Failure?.Kind);
            var resumed = OrderRunner(new FileWorkflowSessionStore<OrderState>(directory), handler);
            Assert.True((await resumed.DispatchAsync("order-2")).Succeeded);
            Assert.Equal(new[] { decision.Actions[0].ActionId, decision.Actions[0].ActionId }, handler.ReceivedIds);
            Assert.Single(effects);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Pending_action_blocks_next_input_and_missing_handler_does_not_run_any_action()
    {
        var directory = NewDirectory();
        try
        {
            var effects = new HashSet<string>();
            var runner = OrderRunner(new FileWorkflowSessionStore<OrderState>(directory),
                new DeduplicatingHandler("other", effects));
            await runner.StartAsync("order-3", new OrderState("order-3", OrderStatus.Pending));
            await runner.ProcessAsync("order-3", "first", CustomerReply.Confirm);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                runner.ProcessAsync("order-3", "second", CustomerReply.Decline));
            var report = await runner.DispatchAsync("order-3");
            Assert.Equal(ActionFailureKind.MissingHandler, report.Failure?.Kind);
            Assert.Empty(effects);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Stale_revision_is_rejected_and_duplicate_input_is_serialized_across_runners()
    {
        var directory = NewDirectory();
        try
        {
            var store1 = new FileWorkflowSessionStore<OrderState>(directory);
            var store2 = new FileWorkflowSessionStore<OrderState>(directory);
            var first = OrderRunner(store1);
            var second = OrderRunner(store2);
            await first.StartAsync("order-4", new OrderState("order-4", OrderStatus.Pending));
            var results = await Task.WhenAll(
                first.ProcessAsync("order-4", "reply", CustomerReply.Confirm),
                second.ProcessAsync("order-4", "reply", CustomerReply.Confirm));
            Assert.Equal(results[0].Actions[0].ActionId, results[1].Actions[0].ActionId);
            Assert.Equal(1, (await store1.LoadAsync("order-4"))!.Revision);
            Assert.False(await store2.TrySaveAsync(results[0], 0));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Restart_skips_actions_checkpointed_before_a_later_failure()
    {
        var directory = NewDirectory();
        try
        {
            var firstCalls = 0;
            var secondCalls = 0;
            var firstHandler = new StringHandler("first", _ => { firstCalls++; return Task.CompletedTask; });
            var secondHandler = new StringHandler("second", _ =>
            {
                secondCalls++;
                return secondCalls == 1
                    ? Task.FromException(new InvalidOperationException("retry"))
                    : Task.CompletedTask;
            });
            DurableWorkflowRunner<string, string> CreateRunner() => new(
                new TwoActionWorkflow(), new FileWorkflowSessionStore<string>(directory),
                [firstHandler, secondHandler]);

            var runner = CreateRunner();
            await runner.StartAsync("two-actions", "initial");
            await runner.ProcessAsync("two-actions", "input-1", "go");
            var failed = await runner.DispatchAsync("two-actions");
            Assert.Equal(1, failed.CompletedCount);
            Assert.Equal(1, failed.Failure?.ActionIndex);

            var restarted = CreateRunner();
            var result = await restarted.DispatchAsync("two-actions");
            Assert.True(result.Succeeded);
            Assert.Equal(1, firstCalls);
            Assert.Equal(2, secondCalls);
            Assert.Equal(1, result.CompletedCount);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static DurableWorkflowRunner<OrderState, CustomerReply> OrderRunner(
        FileWorkflowSessionStore<OrderState> store,
        params IIdempotentWorkflowActionHandler<OrderState>[] handlers) =>
        new(new OrderConfirmationWorkflow(), store, handlers);

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "yoaiworkflow-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class DeduplicatingHandler(string actionName, HashSet<string> effects)
        : IIdempotentWorkflowActionHandler<OrderState>
    {
        public string ActionName => actionName;
        public bool FailAfterEffectOnce { get; set; }
        public List<string> ReceivedIds { get; } = [];

        public Task HandleAsync(string actionId, WorkflowAction action, OrderState state,
            CancellationToken cancellationToken = default)
        {
            ReceivedIds.Add(actionId);
            effects.Add(actionId);
            if (FailAfterEffectOnce)
            {
                FailAfterEffectOnce = false;
                throw new InvalidOperationException("Simulated crash after an external effect");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class TwoActionWorkflow : IWorkflow<string, string>
    {
        public WorkflowTransition<string> Apply(string state, string input) => new("decided",
            [new WorkflowAction("first"), new WorkflowAction("second")]);
    }

    private sealed class StringHandler(string name, Func<string, Task> handle)
        : IIdempotentWorkflowActionHandler<string>
    {
        public string ActionName => name;
        public Task HandleAsync(string actionId, WorkflowAction action, string state,
            CancellationToken cancellationToken = default) => handle(actionId);
    }
}
