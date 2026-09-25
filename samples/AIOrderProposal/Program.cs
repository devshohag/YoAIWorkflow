using OrderConfirmation;
using YoAIWorkflow.Abstractions;
using YoAIWorkflow.AI;
using YoAIWorkflow.Core;

// This sample simulates a model proposal. It does not call an AI provider.
if (args.Length != 1)
{
    Console.WriteLine("Usage: dotnet run --project samples/AIOrderProposal -- confirm|cancel|review|maybe|unknown");
    return;
}

var orderId = "AI-ORDER-" + Guid.NewGuid().ToString("N");
var store = new FileWorkflowSessionStore<OrderState>(
    Path.Combine(Path.GetTempPath(), "yoaiworkflow-ai-sample"));
var runner = new DurableWorkflowRunner<OrderState, CustomerReply>(
    new OrderConfirmationWorkflow(), store,
    [
        new PrintHandler("order.confirmed"),
        new PrintHandler("order.cancelled"),
        new PrintHandler("human.review-requested")
    ]);
await runner.StartAsync(orderId, new OrderState(orderId, OrderStatus.Pending));

var adapter = new AIWorkflowInputAdapter<OrderState, string, CustomerReply>(
    new SimulatedProposalSource(), new OrderInputValidator(), runner);
var result = await adapter.ProcessAsync(orderId, "reply-1", args[0]);
if (!result.Accepted)
{
    Console.WriteLine($"Proposal rejected: {result.Rejection}; state: {(await store.LoadAsync(orderId))!.State.Status}");
    return;
}

Console.WriteLine($"Decision saved: {result.Session!.State.Status}; pending: {result.Session.HasPendingActions}");
var dispatch = await runner.DispatchAsync(orderId);
Console.WriteLine($"Dispatch: {(dispatch.Succeeded ? "succeeded" : dispatch.Failure!.Kind.ToString())}");

sealed class SimulatedProposalSource : IAIInputSource<string, CustomerReply>
{
    public Task<AIInputProposal<CustomerReply>?> ProposeAsync(
        string context, CancellationToken cancellationToken = default)
    {
        AIInputProposal<CustomerReply>? proposal = context.Trim().ToLowerInvariant() switch
        {
            "confirm" => new(CustomerReply.Confirm, 0.96m),
            "cancel" => new(CustomerReply.Decline, 0.96m),
            "review" => new(CustomerReply.Unclear, 0.96m),
            "maybe" => new(CustomerReply.Confirm, 0.40m),
            _ => null
        };
        return Task.FromResult(proposal);
    }
}

sealed class OrderInputValidator : IAIInputValidator<OrderState, CustomerReply>
{
    public WorkflowInputValidation Validate(OrderState state, CustomerReply input) =>
        state.Status != OrderStatus.Pending || !Enum.IsDefined(input)
            ? WorkflowInputValidation.Reject("The order or proposed reply is invalid.")
            : WorkflowInputValidation.Allow();
}

sealed class PrintHandler(string actionName) : IIdempotentWorkflowActionHandler<OrderState>
{
    public string ActionName => actionName;
    public Task HandleAsync(string actionId, WorkflowAction action, OrderState state,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Handled {action.Name} for {state.OrderId} (ID: {actionId})");
        return Task.CompletedTask;
    }
}
