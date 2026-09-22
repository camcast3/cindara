using System.Diagnostics;
using Cindara.Core.Authentication;

namespace Cindara.Core.Diagnostics;

public sealed class DiagnosticSessionStore(ISessionStore inner, LocalDiagnostics diagnostics) : ISessionStore
{
    public Task<IReadOnlyList<SessionProfile>> GetProfilesAsync(CancellationToken cancellationToken = default) =>
        TraceAsync(DiagnosticAction.LoadSessions, () => inner.GetProfilesAsync(cancellationToken));

    public Task<AuthenticatedSession?> GetAsync(SessionProfile profile, CancellationToken cancellationToken = default) =>
        TraceAsync(DiagnosticAction.RestoreSession, () => inner.GetAsync(profile, cancellationToken));

    public async Task SaveAsync(AuthenticatedSession session, CancellationToken cancellationToken = default) =>
        await TraceAsync(DiagnosticAction.SignIn, async () =>
        {
            await inner.SaveAsync(session, cancellationToken).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);

    public Task<bool> RemoveAsync(SessionProfile profile, CancellationToken cancellationToken = default) =>
        TraceAsync(DiagnosticAction.RemoveSession, () => inner.RemoveAsync(profile, cancellationToken));

    public Task<bool> RemoveIfMatchesAsync(
        SessionProfile profile, string? expectedAccessToken, CancellationToken cancellationToken = default) =>
        TraceAsync(DiagnosticAction.RemoveSession,
            () => inner.RemoveIfMatchesAsync(profile, expectedAccessToken, cancellationToken));

    private async Task<T> TraceAsync<T>(DiagnosticAction action, Func<Task<T>> work)
    {
        var started = Stopwatch.GetTimestamp();
        diagnostics.Record(DiagnosticArea.Storage, action, DiagnosticOutcome.Started);
        try
        {
            var result = await work().ConfigureAwait(false);
            diagnostics.Record(DiagnosticArea.Storage, action, DiagnosticOutcome.Completed,
                elapsedMilliseconds: (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return result;
        }
        catch (Exception exception)
        {
            diagnostics.Record(DiagnosticArea.Storage, action,
                exception is OperationCanceledException ? DiagnosticOutcome.Canceled : DiagnosticOutcome.Failed,
                exception is OperationCanceledException ? DiagnosticLevel.Information : DiagnosticLevel.Error,
                exception, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }
}
