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
    /// <summary>Neither the check address nor a second address on another host answered through the server.</summary>
    NoResponse,
}

/// <param name="CheckUrlFailed">The connection works, but the configured check address did not answer.</param>
public sealed record AvailabilityCheckResult(AvailabilityState State, int Time = 0, string? Ip = null, int? HttpStatus = null,
    bool CheckUrlFailed = false)
{
    public static AvailabilityCheckResult Unchecked { get; } = new(AvailabilityState.Unchecked);
    public bool IsAvailable => State == AvailabilityState.Available;
    public bool IsFailure => State is AvailabilityState.UnexpectedStatus or AvailabilityState.Redirected
        or AvailabilityState.TimedOut or AvailabilityState.Unreachable or AvailabilityState.NoResponse;
    public bool CanSearchForReplacement => State is AvailabilityState.TimedOut or AvailabilityState.Unreachable
        or AvailabilityState.NoResponse;
}
