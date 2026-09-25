using YoAIWorkflow.Abstractions;

namespace AppointmentBooking;

/// <summary>Demonstrates an action handler; no external booking is created.</summary>
public sealed class ConsoleBookingActionHandler(string actionName)
    : IWorkflowActionHandler<BookingState>
{
    public string ActionName { get; } = actionName;

    public Task HandleAsync(
        WorkflowAction action,
        BookingState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine($"Handled {action.Name} for {state.BookingId}");
        return Task.CompletedTask;
    }
}

