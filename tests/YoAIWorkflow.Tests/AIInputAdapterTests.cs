using OrderConfirmation;
using Xunit;
using YoAIWorkflow.Abstractions;
using YoAIWorkflow.AI;
using YoAIWorkflow.Core;

namespace YoAIWorkflow.Tests;

public sealed class AIInputAdapterTests
{
    [Theory]
    [InlineData(null, AIProposalRejection.InvalidProposal)]
    [InlineData(-0.1, AIProposalRejection.InvalidProposal)]
    [InlineData(1.1, AIProposalRejection.InvalidProposal)]
    [InlineData(0.79, AIProposalRejection.LowConfidence)]
    public async Task Missing_out_of_range_or_low_confidence_proposals_never_change_state(
        double? confidence, AIProposalRejection expected)
    {
        var directory = NewDirectory();
        try
        {
            var store = new FileWorkflowSessionStore<OrderState>(directory);
            var effects = 0;
            var runner = CreateRunner(store, () => effects++);
            await runner.StartAsync("order-1", new OrderState("order-1", OrderStatus.Pending));
            var proposal = confidence is null ? null : new AIInputProposal<CustomerReply>(
                CustomerReply.Confirm, (decimal)confidence.Value);
            var adapter = CreateAdapter(runner, proposal);

            var result = await adapter.ProcessAsync("order-1", "reply-1", "voice text");

            Assert.Equal(expected, result.Rejection);
            Assert.Null(result.Session);
            Assert.Equal(OrderStatus.Pending, (await store.LoadAsync("order-1"))!.State.Status);
            Assert.Equal(0, (await store.LoadAsync("order-1"))!.Revision);
            Assert.Equal(0, (await runner.DispatchAsync("order-1")).CompletedCount);
            Assert.Equal(0, effects);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Invalid_enum_and_terminal_state_are_rejected_before_decision()
    {
        var directory = NewDirectory();
        try
        {
            var store = new FileWorkflowSessionStore<OrderState>(directory);
            var runner = CreateRunner(store);
            await runner.StartAsync("order-2", new OrderState("order-2", OrderStatus.Pending));
            var invalid = CreateAdapter(runner,
                new AIInputProposal<CustomerReply>((CustomerReply)999, 0.99m));
            Assert.Equal(AIProposalRejection.BusinessRuleRejected,
                (await invalid.ProcessAsync("order-2", "invalid", "text")).Rejection);

            await runner.StartAsync("order-3", new OrderState("order-3", OrderStatus.Confirmed));
            var terminal = CreateAdapter(runner, new AIInputProposal<CustomerReply>(
                CustomerReply.Decline, 0.99m));
            Assert.Equal(AIProposalRejection.BusinessRuleRejected,
                (await terminal.ProcessAsync("order-3", "terminal", "text")).Rejection);
            Assert.Empty((await store.LoadAsync("order-3"))!.ProcessedInputIds);
            Assert.Empty((await store.LoadAsync("order-3"))!.Actions);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Accepted_proposal_saves_action_but_dispatch_requires_a_separate_call()
    {
        var directory = NewDirectory();
        try
        {
            var effects = 0;
            var store = new FileWorkflowSessionStore<OrderState>(directory);
            var runner = CreateRunner(store, () => effects++);
            await runner.StartAsync("order-4", new OrderState("order-4", OrderStatus.Pending));
            var adapter = CreateAdapter(runner,
                new AIInputProposal<CustomerReply>(CustomerReply.Confirm, 0.95m));

            var accepted = await adapter.ProcessAsync("order-4", "reply-1", "confirm");
            Assert.True(accepted.Accepted);
            Assert.Equal(OrderStatus.Confirmed, accepted.Session!.State.Status);
            Assert.True(accepted.Session.HasPendingActions);
            Assert.Equal(0, effects);

            Assert.True((await runner.DispatchAsync("order-4")).Succeeded);
            Assert.Equal(1, effects);
            var repeated = await adapter.ProcessAsync("order-4", "reply-1", "confirm");
            Assert.True(repeated.Accepted);
            Assert.Equal(1, effects);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Revision_conflict_revalidates_against_new_state_before_saving()
    {
        var directory = NewDirectory();
        try
        {
            var persisted = new FileWorkflowSessionStore<OrderState>(directory);
            var runner = CreateRunner(new ConcurrentUpdateStore(persisted));
            await runner.StartAsync("order-5", new OrderState("order-5", OrderStatus.Pending));
            var adapter = CreateAdapter(runner,
                new AIInputProposal<CustomerReply>(CustomerReply.Confirm, 0.99m));

            var result = await adapter.ProcessAsync("order-5", "ai-1", "confirm");

            Assert.Equal(AIProposalRejection.BusinessRuleRejected, result.Rejection);
            var latest = (await persisted.LoadAsync("order-5"))!;
            Assert.Equal(OrderStatus.Confirmed, latest.State.Status);
            Assert.DoesNotContain("ai-1", latest.ProcessedInputIds);
            Assert.Empty(latest.Actions);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static AIWorkflowInputAdapter<OrderState, string, CustomerReply> CreateAdapter(
        DurableWorkflowRunner<OrderState, CustomerReply> runner,
        AIInputProposal<CustomerReply>? proposal) =>
        new(new FixedSource(proposal), new OrderValidator(), runner);

    private static DurableWorkflowRunner<OrderState, CustomerReply> CreateRunner(
        IWorkflowSessionStore<OrderState> store, Action? onEffect = null) =>
        new(new OrderConfirmationWorkflow(), store,
            [new CountingHandler(onEffect ?? (() => { }))]);

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "yoaiworkflow-ai-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FixedSource(AIInputProposal<CustomerReply>? proposal)
        : IAIInputSource<string, CustomerReply>
    {
        public Task<AIInputProposal<CustomerReply>?> ProposeAsync(
            string context, CancellationToken cancellationToken = default) => Task.FromResult(proposal);
    }

    private sealed class OrderValidator : IAIInputValidator<OrderState, CustomerReply>
    {
        public WorkflowInputValidation Validate(OrderState state, CustomerReply input) =>
            state.Status == OrderStatus.Pending && Enum.IsDefined(input)
                ? WorkflowInputValidation.Allow()
                : WorkflowInputValidation.Reject("Order is closed or reply is unsupported.");
    }

    private sealed class CountingHandler(Action effect) : IIdempotentWorkflowActionHandler<OrderState>
    {
        public string ActionName => "order.confirmed";
        public Task HandleAsync(string actionId, WorkflowAction action, OrderState state,
            CancellationToken cancellationToken = default)
        {
            effect();
            return Task.CompletedTask;
        }
    }

    private sealed class ConcurrentUpdateStore(FileWorkflowSessionStore<OrderState> inner)
        : IWorkflowSessionStore<OrderState>
    {
        private bool _intercepted;

        public Task<WorkflowSession<OrderState>?> LoadAsync(string workflowId,
            CancellationToken cancellationToken = default) => inner.LoadAsync(workflowId, cancellationToken);

        public async Task<bool> TrySaveAsync(WorkflowSession<OrderState> session,
            long expectedRevision, CancellationToken cancellationToken = default)
        {
            if (!_intercepted && expectedRevision == 0 && session.ProcessedInputIds.Contains("ai-1"))
            {
                _intercepted = true;
                var previous = (await inner.LoadAsync(session.WorkflowId, cancellationToken))!;
                var updated = previous with
                {
                    Revision = 1,
                    State = previous.State with { Status = OrderStatus.Confirmed },
                    ProcessedInputIds = ["external-input"]
                };
                Assert.True(await inner.TrySaveAsync(updated, 0, cancellationToken));
            }
            return await inner.TrySaveAsync(session, expectedRevision, cancellationToken);
        }
    }
}
