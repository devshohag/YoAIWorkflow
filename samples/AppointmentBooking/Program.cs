using System.Globalization;
using AppointmentBooking;
using YoAIWorkflow.Core;

// The host supplies its local earliest date. The workflow itself never reads a clock.
var workflow = new AppointmentBookingWorkflow(DateOnly.FromDateTime(DateTime.Now));
var engine = new WorkflowEngine<BookingState, BookingInput>(workflow);
var executor = new WorkflowActionExecutor<BookingState>(
[
    new ConsoleBookingActionHandler("booking.cancelled"),
    new ConsoleBookingActionHandler("booking.date-rejected"),
    new ConsoleBookingActionHandler("booking.confirmation-requested"),
    new ConsoleBookingActionHandler("booking.create-requested"),
    new ConsoleBookingActionHandler("human.review-requested")
]);
var state = new BookingState("BOOKING-1001", BookingStatus.AwaitingService);

async Task<bool> ApplyAsync(BookingInput input)
{
    var transition = engine.Process(state, input);
    var report = await executor.ExecuteAsync(transition);
    if (!report.Succeeded)
    {
        Console.Error.WriteLine($"Action failed: {report.Failure!.Kind}");
        Environment.ExitCode = 1;
        return false;
    }

    state = transition.State;
    Console.WriteLine($"Booking state: {state.Status}");
    return true;
}

Console.Write("Service code: ");
if (!await ApplyAsync(new BookingInput.ChooseService(Console.ReadLine() ?? ""))) return;
if (state.Status != BookingStatus.AwaitingDate)
{
    Console.WriteLine("Provide a service code to continue.");
    return;
}

Console.Write("Requested date (yyyy-MM-dd): ");
var dateText = Console.ReadLine();
var dateInput = DateOnly.TryParseExact(
    dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
    ? (BookingInput)new BookingInput.ChooseDate(date)
    : new BookingInput.Unclear();
if (!await ApplyAsync(dateInput)) return;
if (state.Status != BookingStatus.AwaitingConfirmation)
{
    Console.WriteLine("A valid upcoming date is needed to continue.");
    return;
}

Console.Write("1 = confirm, 2 = cancel, anything else = human review: ");
var finalInput = Console.ReadLine() switch
{
    "1" => (BookingInput)new BookingInput.Confirm(),
    "2" => new BookingInput.Cancel(),
    _ => new BookingInput.Unclear()
};
await ApplyAsync(finalInput);

