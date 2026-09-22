using System.Diagnostics;

namespace Cindara.Core.Diagnostics;

public sealed class DiagnosticHttpHandler(LocalDiagnostics diagnostics, HttpMessageHandler innerHandler)
    : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            diagnostics.Record(DiagnosticArea.Network, DiagnosticAction.Request,
                response.IsSuccessStatusCode ? DiagnosticOutcome.Completed : DiagnosticOutcome.Failed,
                response.IsSuccessStatusCode ? DiagnosticLevel.Information : DiagnosticLevel.Warning,
                elapsedMilliseconds: (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                httpStatus: (int)response.StatusCode);
            return response;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
        {
            diagnostics.Record(DiagnosticArea.Network, DiagnosticAction.Request,
                cancellationToken.IsCancellationRequested ? DiagnosticOutcome.Canceled : DiagnosticOutcome.Failed,
                cancellationToken.IsCancellationRequested ? DiagnosticLevel.Information : DiagnosticLevel.Error,
                exception, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }
}
