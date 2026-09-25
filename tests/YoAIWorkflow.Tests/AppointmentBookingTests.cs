using AppointmentBooking;
using OrderConfirmation;
using Xunit;
using YoAIWorkflow.Abstractions;
using YoAIWorkflow.Core;

namespace YoAIWorkflow.Tests;

public sealed class AppointmentBookingTests
{
    private static readonly DateOnly EarliestDate = new(2026, 9, 25);

    private readonly WorkflowEngine<BookingState, BookingInput> _engine =
        new(new AppointmentBookingWorkflow(EarliestDate));

    [Fact]
    public void Booking_is_requested_only_after_service_date_and_confirmation()
    {
        var initial = new BookingState("BOOKING-1", BookingStatus.AwaitingService);

        var service = _engine.Process(initial, new BookingInput.ChooseService(" cleaning "));
        Assert.Equal(BookingStatus.AwaitingService, initial.Status);
        Assert.Equal(BookingStatus.AwaitingDate, service.State.Status);
        Assert.Equal("cleaning", service.State.ServiceCode);
        Assert.Empty(service.Actions);

        var date = _engine.Process(service.State,
            new BookingInput.ChooseDate(new DateOnly(2026, 9, 26)));
        Assert.Equal(BookingStatus.AwaitingConfirmation, date.State.Status);
        Assert.Equal(new DateOnly(2026, 9, 26), date.State.RequestedDate);
        Assert.Equal("booking.confirmation-requested", Assert.Single(date.Actions).Name);

        var confirmed = _engine.Process(date.State, new BookingInput.Confirm());
        Assert.Equal(BookingStatus.BookingRequested, confirmed.State.Status);
        Assert.Equal("booking.create-requested", Assert.Single(confirmed.Actions).Name);
    }

    [Fact]
    public void Past_date_cannot_advance_to_confirmation()
    {
        var state = new BookingState("BOOKING-1", BookingStatus.AwaitingDate,
            ServiceCode: "cleaning");

        var result = _engine.Process(state,
            new BookingInput.ChooseDate(new DateOnly(2026, 9, 24)));

        Assert.Same(state, result.State);
        Assert.Equal("booking.date-rejected", Assert.Single(result.Actions).Name);
    }

    [Fact]
    public void Confirmation_rechecks_the_date_when_the_request_was_started_earlier()
    {
        var state = new BookingState("BOOKING-1", BookingStatus.AwaitingConfirmation,
            "cleaning", new DateOnly(2026, 9, 24));

        var result = _engine.Process(state, new BookingInput.Confirm());

        Assert.Equal(BookingStatus.AwaitingDate, result.State.Status);
        Assert.Null(result.State.RequestedDate);
        Assert.Equal("booking.date-rejected", Assert.Single(result.Actions).Name);
    }

    [Fact]
    public void Invalid_or_out_of_order_input_cannot_skip_required_steps()
    {
        var state = new BookingState("BOOKING-1", BookingStatus.AwaitingService);

        var emptyService = _engine.Process(state, new BookingInput.ChooseService("  "));
        var earlyConfirmation = _engine.Process(state, new BookingInput.Confirm());

        Assert.Same(state, emptyService.State);
        Assert.Empty(emptyService.Actions);
        Assert.Same(state, earlyConfirmation.State);
        Assert.Empty(earlyConfirmation.Actions);

        var incomplete = new BookingState("BOOKING-2", BookingStatus.AwaitingConfirmation);
        var incompleteConfirmation = _engine.Process(incomplete, new BookingInput.Confirm());
        Assert.Same(incomplete, incompleteConfirmation.State);
        Assert.Empty(incompleteConfirmation.Actions);
    }

    [Fact]
    public void Cancellation_and_unclear_input_take_distinct_paths()
    {
        var state = new BookingState("BOOKING-1", BookingStatus.AwaitingDate,
            ServiceCode: "cleaning");

        var cancelled = _engine.Process(state, new BookingInput.Cancel());
        var handoff = _engine.Process(state, new BookingInput.Unclear());

        Assert.Equal(BookingStatus.Cancelled, cancelled.State.Status);
        Assert.Equal("booking.cancelled", Assert.Single(cancelled.Actions).Name);
        Assert.Equal(BookingStatus.NeedsHuman, handoff.State.Status);
        Assert.Equal("human.review-requested", Assert.Single(handoff.Actions).Name);
        Assert.Empty(_engine.Process(cancelled.State, new BookingInput.Confirm()).Actions);
        Assert.Empty(_engine.Process(handoff.State, new BookingInput.Confirm()).Actions);
    }

    [Fact]
    public async Task Booking_and_order_use_the_same_engine_and_action_contracts()
    {
        var order = new WorkflowEngine<OrderState, CustomerReply>(new OrderConfirmationWorkflow())
            .Process(new OrderState("ORDER-1", OrderStatus.Pending), CustomerReply.Confirm);
        var bookingState = new BookingState("BOOKING-1", BookingStatus.AwaitingConfirmation,
            "cleaning", new DateOnly(2026, 9, 26));
        var booking = _engine.Process(bookingState, new BookingInput.Confirm());
        BookingState? handledState = null;
        var executor = new WorkflowActionExecutor<BookingState>(
            [new RecordingHandler((_, state, _) =>
            {
                handledState = state;
                return Task.CompletedTask;
            })]);

        var report = await executor.ExecuteAsync(booking);

        Assert.Equal("order.confirmed", Assert.Single(order.Actions).Name);
        Assert.Equal("booking.create-requested", Assert.Single(booking.Actions).Name);
        Assert.True(report.Succeeded);
        Assert.Same(booking.State, handledState);
    }

    private sealed class RecordingHandler(
        Func<WorkflowAction, BookingState, CancellationToken, Task> handle)
        : IWorkflowActionHandler<BookingState>
    {
        public string ActionName => "booking.create-requested";

        public Task HandleAsync(
            WorkflowAction action,
            BookingState state,
            CancellationToken cancellationToken = default) => handle(action, state, cancellationToken);
    }
}
