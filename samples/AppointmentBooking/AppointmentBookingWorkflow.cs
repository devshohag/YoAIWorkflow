using YoAIWorkflow.Abstractions;

namespace AppointmentBooking;

/// <summary>Decides a multi-step booking request without calling a booking service.</summary>
public sealed class AppointmentBookingWorkflow(DateOnly earliestDate)
    : IWorkflow<BookingState, BookingInput>
{
    public WorkflowTransition<BookingState> Apply(BookingState state, BookingInput input)
    {
        if (state.Status is BookingStatus.BookingRequested
            or BookingStatus.Cancelled
            or BookingStatus.NeedsHuman)
        {
            return WorkflowTransition<BookingState>.WithoutActions(state);
        }

        if (input is BookingInput.Cancel)
        {
            return new WorkflowTransition<BookingState>(
                state with { Status = BookingStatus.Cancelled },
                [new WorkflowAction("booking.cancelled")]);
        }

        if (input is BookingInput.Unclear)
        {
            return new WorkflowTransition<BookingState>(
                state with { Status = BookingStatus.NeedsHuman },
                [new WorkflowAction("human.review-requested")]);
        }

        return (state.Status, input) switch
        {
            (BookingStatus.AwaitingService, BookingInput.ChooseService service)
                when !string.IsNullOrWhiteSpace(service.ServiceCode) => new WorkflowTransition<BookingState>(
                    state with
                    {
                        Status = BookingStatus.AwaitingDate,
                        ServiceCode = service.ServiceCode.Trim()
                    },
                    []),

            (BookingStatus.AwaitingDate, BookingInput.ChooseDate date)
                when !string.IsNullOrWhiteSpace(state.ServiceCode)
                    && date.Date < earliestDate => new WorkflowTransition<BookingState>(
                    state,
                    [new WorkflowAction("booking.date-rejected")]),

            (BookingStatus.AwaitingDate, BookingInput.ChooseDate date)
                when !string.IsNullOrWhiteSpace(state.ServiceCode) => new WorkflowTransition<BookingState>(
                state with
                {
                    Status = BookingStatus.AwaitingConfirmation,
                    RequestedDate = date.Date
                },
                [new WorkflowAction("booking.confirmation-requested")]),

            (BookingStatus.AwaitingConfirmation, BookingInput.Confirm)
                when state.RequestedDate is { } selectedDate
                    && selectedDate < earliestDate => new WorkflowTransition<BookingState>(
                        state with
                        {
                            Status = BookingStatus.AwaitingDate,
                            RequestedDate = null
                        },
                        [new WorkflowAction("booking.date-rejected")]),

            (BookingStatus.AwaitingConfirmation, BookingInput.Confirm)
                when !string.IsNullOrWhiteSpace(state.ServiceCode)
                    && state.RequestedDate is not null => new WorkflowTransition<BookingState>(
                state with { Status = BookingStatus.BookingRequested },
                [new WorkflowAction("booking.create-requested")]),

            _ => WorkflowTransition<BookingState>.WithoutActions(state)
        };
    }
}
