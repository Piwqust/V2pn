namespace ServiceLib.Models.Dto;

public enum AvailabilityState
{
    Unchecked,
    Unsupported,
    Checking,
    Available,
    UnexpectedStatus,
    Redirected,
    TimedOut,
    Unreachable,
}

public sealed record AvailabilityCheckResult(AvailabilityState State, int Time = 0, string? Ip = null, int? HttpStatus = null)
{
    public static AvailabilityCheckResult Unchecked { get; } = new(AvailabilityState.Unchecked);
    public bool IsAvailable => State == AvailabilityState.Available;
    public bool IsFailure => State is AvailabilityState.UnexpectedStatus or AvailabilityState.Redirected
        or AvailabilityState.TimedOut or AvailabilityState.Unreachable;
    public bool CanSearchForReplacement => State is AvailabilityState.TimedOut or AvailabilityState.Unreachable;
}
