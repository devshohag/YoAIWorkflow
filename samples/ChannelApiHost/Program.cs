using OrderConfirmation;
using YoAIWorkflow.Abstractions;
using YoAIWorkflow.Channels;
using YoAIWorkflow.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IWorkflowSessionStore<OrderState>>(
    new FileWorkflowSessionStore<OrderState>(
        Path.Combine(builder.Environment.ContentRootPath, "workflow-data")));
builder.Services.AddSingleton(sp => new DurableWorkflowRunner<OrderState, CustomerReply>(
    new OrderConfirmationWorkflow(), sp.GetRequiredService<IWorkflowSessionStore<OrderState>>(),
    [
        new ConsoleActionHandler("order.confirmed"),
        new ConsoleActionHandler("order.cancelled"),
        new ConsoleActionHandler("human.review-requested")
    ]));
builder.Services.AddSingleton(sp => new WorkflowInputRouter<ApiEvent, OrderState, CustomerReply>(
    new ApiEventMapper(),
    sp.GetRequiredService<DurableWorkflowRunner<OrderState, CustomerReply>>(),
    (state, input) => state.Status == OrderStatus.Pending && Enum.IsDefined(input)
        ? WorkflowInputValidation.Allow()
        : WorkflowInputValidation.Reject("The order is closed or the reply is unsupported.")));

var app = builder.Build();

app.MapPost("/workflows/{workflowId}/start", async (
    string workflowId, DurableWorkflowRunner<OrderState, CustomerReply> runner,
    CancellationToken cancellationToken) =>
{
    var session = await runner.StartAsync(workflowId,
        new OrderState(workflowId, OrderStatus.Pending), cancellationToken);
    return Results.Ok(new { session.State.Status, session.Revision });
});

app.MapPost("/workflows/{workflowId}/events", async (
    string workflowId, IncomingRequest request,
    WorkflowInputRouter<ApiEvent, OrderState, CustomerReply> router,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await router.RouteAsync(
            new ApiEvent(workflowId, request.InputId, request.Kind, request.Value), cancellationToken);
        return result.Accepted
            ? (IResult)Results.Ok(new { result.Session!.State.Status, result.Session.Revision,
                PendingActions = result.Session.HasPendingActions })
            : Results.BadRequest(new { result.Rejection, result.Detail });
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { exception.Message });
    }
});

app.MapPost("/workflows/{workflowId}/dispatch", async (
    string workflowId, DurableWorkflowRunner<OrderState, CustomerReply> runner,
    CancellationToken cancellationToken) =>
{
    var report = await runner.DispatchAsync(workflowId, cancellationToken);
    return Results.Ok(new { report.Succeeded, report.CompletedCount,
        Failure = report.Failure?.Kind.ToString() });
});

app.MapGet("/workflows/{workflowId}", async (
    string workflowId, IWorkflowSessionStore<OrderState> store,
    CancellationToken cancellationToken) =>
{
    var session = await store.LoadAsync(workflowId, cancellationToken);
    return session is null ? (IResult)Results.NotFound() : Results.Ok(session);
});

app.Run();

internal sealed record IncomingRequest(string InputId, string Kind, string Value);
internal sealed record ApiEvent(string WorkflowId, string InputId, string Kind, string Value);

// Transport-specific parsing belongs to this host application, never the SDK.
internal sealed class ApiEventMapper : IWorkflowEventMapper<ApiEvent, CustomerReply>
{
    public Task<WorkflowInboundEvent<CustomerReply>?> MapAsync(
        ApiEvent externalEvent, CancellationToken cancellationToken = default)
    {
        CustomerReply? reply = (externalEvent.Kind?.ToLowerInvariant(),
            externalEvent.Value?.ToLowerInvariant()) switch
        {
            ("text", "confirm") => CustomerReply.Confirm,
            ("text", "cancel") => CustomerReply.Decline,
            ("key", "1") => CustomerReply.Confirm,
            ("key", "2") => CustomerReply.Decline,
            _ => null
        };
        WorkflowInboundEvent<CustomerReply>? mapped = reply is { } value
            ? new(externalEvent.WorkflowId, externalEvent.InputId, value)
            : null;
        return Task.FromResult(mapped);
    }
}

// Printing illustrates an action only. Real handlers must deduplicate external effects.
internal sealed class ConsoleActionHandler(string actionName) : IIdempotentWorkflowActionHandler<OrderState>
{
    public string ActionName => actionName;

    public Task HandleAsync(string actionId, WorkflowAction action, OrderState state,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Handled {action.Name} for {state.OrderId}; ID: {actionId}");
        return Task.CompletedTask;
    }
}
