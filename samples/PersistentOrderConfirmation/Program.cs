using OrderConfirmation;
using YoAIWorkflow.Abstractions;
using YoAIWorkflow.Core;

if (args.Length is < 2 or > 3)
{
    Console.WriteLine("Usage: start|confirm|cancel|dispatch|show <order-id> [input-id]");
    return;
}

var store = new FileWorkflowSessionStore<OrderState>(
    Path.Combine(Environment.CurrentDirectory, "workflow-data"));
var runner = new DurableWorkflowRunner<OrderState, CustomerReply>(
    new OrderConfirmationWorkflow(), store,
    [
        new PrintHandler("order.confirmed"),
        new PrintHandler("order.cancelled"),
        new PrintHandler("human.review-requested")
    ]);

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "start":
            await runner.StartAsync(args[1], new OrderState(args[1], OrderStatus.Pending));
            break;
        case "confirm" or "cancel" when args.Length == 3:
            var reply = args[0].Equals("confirm", StringComparison.OrdinalIgnoreCase)
                ? CustomerReply.Confirm : CustomerReply.Decline;
            await runner.ProcessAsync(args[1], args[2], reply);
            break;
        case "dispatch":
            var report = await runner.DispatchAsync(args[1]);
            Console.WriteLine($"Actions completed: {report.CompletedCount}; failure: {report.Failure?.Kind.ToString() ?? "none"}");
            break;
        case "show":
            break;
        default:
            Console.WriteLine("Usage: start|confirm|cancel|dispatch|show <order-id> [input-id]");
            return;
    }

    var session = await store.LoadAsync(args[1]);
    Console.WriteLine($"Order state: {session?.State.Status}; pending actions: {session?.Actions.Count(a => !a.Completed)}");
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 1;
}

// Console output is illustrative only; production handlers must atomically deduplicate
// their real side effects using actionId (for example in the same database transaction).
sealed class PrintHandler(string actionName) : IIdempotentWorkflowActionHandler<OrderState>
{
    public string ActionName => actionName;

    public Task HandleAsync(string actionId, WorkflowAction action, OrderState state,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Handled {action.Name} for {state.OrderId}; action ID: {actionId}");
        return Task.CompletedTask;
    }
}
