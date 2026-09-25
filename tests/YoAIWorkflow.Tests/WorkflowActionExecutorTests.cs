using OrderConfirmation;
using Xunit;
using YoAIWorkflow.Abstractions;
using YoAIWorkflow.Core;

namespace YoAIWorkflow.Tests;

public sealed class WorkflowActionExecutorTests
{
    [Fact]
    public async Task Decision_does_not_execute_an_action_until_dispatch_is_requested()
    {
        var called = false;
        var state = new OrderState("ORDER-1001", OrderStatus.Pending);
        var executor = new WorkflowActionExecutor<OrderState>(
            [new Handler<OrderState>("order.confirmed", (_, _, _) =>
            {
                called = true;
                return Task.CompletedTask;
            })]);

        var transition = new WorkflowEngine<OrderState, CustomerReply>(
            new OrderConfirmationWorkflow()).Process(state, CustomerReply.Confirm);

        Assert.False(called);
        var report = await executor.ExecuteAsync(transition);
        Assert.True(called);
        Assert.True(report.Succeeded);
        Assert.Equal(1, report.CompletedCount);
        Assert.Null(report.Failure);
    }

    [Fact]
    public async Task Handler_receives_the_decided_state_and_requested_action()
    {
        OrderState? receivedState = null;
        WorkflowAction? receivedAction = null;
        var executor = new WorkflowActionExecutor<OrderState>(
            [new Handler<OrderState>("order.confirmed", (action, state, _) =>
            {
                receivedState = state;
                receivedAction = action;
                return Task.CompletedTask;
            })]);
        var transition = new WorkflowEngine<OrderState, CustomerReply>(
            new OrderConfirmationWorkflow()).Process(
                new OrderState("ORDER-1001", OrderStatus.Pending),
                CustomerReply.Confirm);

        await executor.ExecuteAsync(transition);

        Assert.Same(transition.State, receivedState);
        Assert.Same(Assert.Single(transition.Actions), receivedAction);
    }

    [Fact]
    public async Task Missing_handler_prevents_every_action_from_starting()
    {
        var called = false;
        var executor = new WorkflowActionExecutor<string>(
            [new Handler<string>("known", (_, _, _) =>
            {
                called = true;
                return Task.CompletedTask;
            })]);
        var transition = new WorkflowTransition<string>("state",
            [new WorkflowAction("known"), new WorkflowAction("missing")]);

        var report = await executor.ExecuteAsync(transition);

        Assert.False(called);
        Assert.False(report.Succeeded);
        Assert.Equal(0, report.CompletedCount);
        Assert.Equal(1, report.Failure?.ActionIndex);
        Assert.Equal(ActionFailureKind.MissingHandler, report.Failure?.Kind);
    }

    [Fact]
    public async Task Handler_failure_reports_completed_count_and_stops_later_actions()
    {
        var calls = new List<string>();
        var error = new InvalidOperationException("simulated failure");
        var executor = new WorkflowActionExecutor<string>(
        [
            new Handler<string>("first", (_, _, _) =>
            {
                calls.Add("first");
                return Task.CompletedTask;
            }),
            new Handler<string>("second", (_, _, _) =>
            {
                calls.Add("second");
                return Task.FromException(error);
            }),
            new Handler<string>("third", (_, _, _) =>
            {
                calls.Add("third");
                return Task.CompletedTask;
            })
        ]);
        var transition = new WorkflowTransition<string>("state",
            [new WorkflowAction("first"), new WorkflowAction("second"), new WorkflowAction("third")]);

        var report = await executor.ExecuteAsync(transition);

        Assert.Equal(new[] { "first", "second" }, calls);
        Assert.False(report.Succeeded);
        Assert.Equal(1, report.CompletedCount);
        Assert.Equal(1, report.Failure?.ActionIndex);
        Assert.Equal(ActionFailureKind.HandlerThrew, report.Failure?.Kind);
        Assert.Same(error, report.Failure?.Exception);
    }

    [Fact]
    public void Duplicate_action_handlers_are_rejected()
    {
        var one = new Handler<string>("duplicate", (_, _, _) => Task.CompletedTask);
        var two = new Handler<string>("duplicate", (_, _, _) => Task.CompletedTask);

        Assert.Throws<ArgumentException>(() => new WorkflowActionExecutor<string>([one, two]));
    }

    [Fact]
    public async Task Cancellation_is_propagated_without_running_a_handler()
    {
        var called = false;
        var executor = new WorkflowActionExecutor<string>(
            [new Handler<string>("known", (_, _, _) =>
            {
                called = true;
                return Task.CompletedTask;
            })]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAsync(
            new WorkflowTransition<string>("state", [new WorkflowAction("known")]),
            cancellation.Token));
        Assert.False(called);
    }

    [Fact]
    public async Task Cancellation_during_a_handler_does_not_run_later_actions()
    {
        using var cancellation = new CancellationTokenSource();
        var nextCalled = false;
        var executor = new WorkflowActionExecutor<string>(
        [
            new Handler<string>("cancel", (_, _, _) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled(cancellation.Token);
            }),
            new Handler<string>("next", (_, _, _) =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            })
        ]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAsync(
            new WorkflowTransition<string>("state",
                [new WorkflowAction("cancel"), new WorkflowAction("next")]),
            cancellation.Token));
        Assert.False(nextCalled);
    }

    private sealed class Handler<TState>(
        string actionName,
        Func<WorkflowAction, TState, CancellationToken, Task> handle) : IWorkflowActionHandler<TState>
    {
        public string ActionName { get; } = actionName;

        public Task HandleAsync(
            WorkflowAction action,
            TState state,
            CancellationToken cancellationToken = default) => handle(action, state, cancellationToken);
    }
}
