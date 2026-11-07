namespace Cindara.Desktop.Startup;

public sealed record StartupRecoveryDecision(
    bool UseSafeMode,
    int ConsecutiveFailedStarts,
    string? Error);
