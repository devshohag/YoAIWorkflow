using YoAIWorkflow.Abstractions;

namespace OrderConfirmation;

/// <summary>Demonstrates a handler; this sample only writes to the console.</summary>
public sealed class ConsoleOrderActionHandler(string actionName) : IWorkflowActionHandler<OrderState>
{
    public string ActionName { get; } = actionName;

    public Task HandleAsync(
        WorkflowAction action,
        OrderState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine($"Handled {action.Name} for {state.OrderId}");
        return Task.CompletedTask;
    }
}

