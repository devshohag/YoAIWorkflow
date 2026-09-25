using OrderConfirmation;
using Xunit;
using YoAIWorkflow.Abstractions;
using YoAIWorkflow.Channels;
using YoAIWorkflow.Core;

namespace YoAIWorkflow.Tests;

public sealed class WorkflowInputRouterTests
{
    [Fact]
    public async Task Two_external_event_types_share_a_workflow_and_keep_dispatch_explicit()
    {
        var directory = NewDirectory();
        try
        {
            var handled = 0;
            var runner = Runner(directory, () => handled++);
            await runner.StartAsync("order-text", new OrderState("order-text", OrderStatus.Pending));
            await runner.StartAsync("order-key", new OrderState("order-key", OrderStatus.Pending));
            var textRouter = new WorkflowInputRouter<TextEvent, OrderState, CustomerReply>(
                new TextMapper(), runner, Guard);
            var keyRouter = new WorkflowInputRouter<KeyEvent, OrderState, CustomerReply>(
                new KeyMapper(), runner, Guard);

            var text = await textRouter.RouteAsync(new TextEvent("order-text", "reply-1", "confirm"));
            var key = await keyRouter.RouteAsync(new KeyEvent("order-key", "reply-2", "1"));
            Assert.True(text.Accepted);
            Assert.True(key.Accepted);
            Assert.Equal(OrderStatus.Confirmed, text.Session!.State.Status);
            Assert.Equal(OrderStatus.Confirmed, key.Session!.State.Status);
            Assert.Equal(0, handled);

            Assert.True((await runner.DispatchAsync("order-text")).Succeeded);
            Assert.True((await runner.DispatchAsync("order-key")).Succeeded);
            Assert.Equal(2, handled);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Repeated_event_id_from_another_channel_does_not_create_another_decision()
    {
        var directory = NewDirectory();
        try
        {
            var runner = Runner(directory);
            await runner.StartAsync("order-1", new OrderState("order-1", OrderStatus.Pending));
            var textRouter = new WorkflowInputRouter<TextEvent, OrderState, CustomerReply>(
                new TextMapper(), runner, Guard);
            var keyRouter = new WorkflowInputRouter<KeyEvent, OrderState, CustomerReply>(
                new KeyMapper(), runner, Guard);

            var first = await textRouter.RouteAsync(new TextEvent("order-1", "shared-event", "confirm"));
            var repeated = await keyRouter.RouteAsync(new KeyEvent("order-1", "shared-event", "1"));

            Assert.True(repeated.Accepted);
            Assert.Equal(first.Session!.Revision, repeated.Session!.Revision);
            Assert.Equal(first.Session.Actions[0].ActionId, repeated.Session.Actions[0].ActionId);
            Assert.Single(repeated.Session.ProcessedInputIds);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Unmapped_malformed_and_disallowed_events_cannot_create_actions()
    {
        var directory = NewDirectory();
        try
        {
            var runner = Runner(directory);
            await runner.StartAsync("order-2", new OrderState("order-2", OrderStatus.Pending));
            var router = new WorkflowInputRouter<TextEvent, OrderState, CustomerReply>(
                new TextMapper(), runner, Guard);

            Assert.Equal(WorkflowInputRejection.UnmappedEvent,
                (await router.RouteAsync(new TextEvent("order-2", "event-1", "unrecognized"))).Rejection);
            Assert.Equal(WorkflowInputRejection.InvalidEvent,
                (await router.RouteAsync(new TextEvent("order-2", "", "confirm"))).Rejection);

            await runner.StartAsync("closed-order", new OrderState("closed-order", OrderStatus.Cancelled));
            Assert.Equal(WorkflowInputRejection.BusinessRuleRejected,
                (await router.RouteAsync(new TextEvent("closed-order", "event-2", "confirm"))).Rejection);
            var unchanged = (await new FileWorkflowSessionStore<OrderState>(directory)
                .LoadAsync("order-2"))!;
            Assert.Equal(0, unchanged.Revision);
            Assert.Empty(unchanged.Actions);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static WorkflowInputValidation Guard(OrderState state, CustomerReply input) =>
        state.Status == OrderStatus.Pending && Enum.IsDefined(input)
            ? WorkflowInputValidation.Allow()
            : WorkflowInputValidation.Reject("Order cannot accept that reply.");

    private static DurableWorkflowRunner<OrderState, CustomerReply> Runner(
        string directory, Action? onHandled = null) => new(
        new OrderConfirmationWorkflow(), new FileWorkflowSessionStore<OrderState>(directory),
        [new CountHandler(onHandled ?? (() => { }))]);

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "yoaiworkflow-channel-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed record TextEvent(string WorkflowId, string InputId, string Value);
    private sealed record KeyEvent(string WorkflowId, string InputId, string Value);

    private sealed class TextMapper : IWorkflowEventMapper<TextEvent, CustomerReply>
    {
        public Task<WorkflowInboundEvent<CustomerReply>?> MapAsync(
            TextEvent externalEvent, CancellationToken cancellationToken = default)
        {
            WorkflowInboundEvent<CustomerReply>? mapped = externalEvent.Value == "confirm"
                ? new(externalEvent.WorkflowId, externalEvent.InputId, CustomerReply.Confirm)
                : null;
            return Task.FromResult(mapped);
        }
    }

    private sealed class KeyMapper : IWorkflowEventMapper<KeyEvent, CustomerReply>
    {
        public Task<WorkflowInboundEvent<CustomerReply>?> MapAsync(
            KeyEvent externalEvent, CancellationToken cancellationToken = default)
        {
            WorkflowInboundEvent<CustomerReply>? mapped = externalEvent.Value == "1"
                ? new(externalEvent.WorkflowId, externalEvent.InputId, CustomerReply.Confirm)
                : null;
            return Task.FromResult(mapped);
        }
    }

    private sealed class CountHandler(Action onHandled) : IIdempotentWorkflowActionHandler<OrderState>
    {
        public string ActionName => "order.confirmed";
        public Task HandleAsync(string actionId, WorkflowAction action, OrderState state,
            CancellationToken cancellationToken = default)
        {
            onHandled();
            return Task.CompletedTask;
        }
    }
}
