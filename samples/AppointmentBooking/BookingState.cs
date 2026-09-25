namespace AppointmentBooking;

public enum BookingStatus
{
    AwaitingService,
    AwaitingDate,
    AwaitingConfirmation,
    BookingRequested,
    Cancelled,
    NeedsHuman
}

public sealed record BookingState(
    string BookingId,
    BookingStatus Status,
    string? ServiceCode = null,
    DateOnly? RequestedDate = null);

