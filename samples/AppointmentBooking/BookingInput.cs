namespace AppointmentBooking;

public abstract record BookingInput
{
    public sealed record ChooseService(string ServiceCode) : BookingInput;
    public sealed record ChooseDate(DateOnly Date) : BookingInput;
    public sealed record Confirm : BookingInput;
    public sealed record Cancel : BookingInput;
    public sealed record Unclear : BookingInput;
}

