using System.Text.Json;
using Cindara.Core.Authentication;
using Cindara.Core.Jellyfin;

namespace Cindara.Core.Diagnostics;

/// <summary>
/// Redacts by omission, not pattern matching: messages, stacks, Data, headers, URLs,
/// paths and arbitrary exception type names are never accepted as diagnostic fields.
/// </summary>
public static class DiagnosticRedactor
{
    private static readonly HashSet<string> AllowedCodes =
    [
        "Canceled", "HttpRequest", "IO", "AccessDenied", "InvalidJson", "MissingNativeLibrary",
        "MissingNativeEntryPoint", "Cryptography", "Unknown",
        .. Enum.GetNames<AuthenticationError>().Select(name => $"Authentication.{name}"),
        .. Enum.GetNames<SessionStoreError>().Select(name => $"Storage.{name}"),
        .. Enum.GetNames<ServerConnectionError>().Select(name => $"Server.{name}"),
        .. Enum.GetNames<MediaPreviewError>().Select(name => $"Media.{name}"),
    ];

    public static string[] Describe(Exception? exception)
    {
        var codes = new List<string>();
        Add(exception, codes);
        return [.. codes];
    }

    internal static bool IsSafe(DiagnosticEntry entry) =>
        entry.Operation >= 0 && Enum.IsDefined(entry.Level) && Enum.IsDefined(entry.Area)
        && Enum.IsDefined(entry.Action) && Enum.IsDefined(entry.Outcome)
        && entry.ElapsedMilliseconds is null or >= 0 && entry.HttpStatus is null or >= 100 and <= 599
        && entry.Errors is { Length: <= 8 } && entry.Errors.All(code => AllowedCodes.Contains(code));

    private static void Add(Exception? exception, List<string> codes)
    {
        if (exception is null || codes.Count >= 8)
        {
            return;
        }

        var code = exception switch
        {
            AuthenticationException error => $"Authentication.{error.Error}",
            SessionStoreException error => $"Storage.{error.Error}",
            ServerConnectionException error => $"Server.{error.Error}",
            MediaPreviewException error => $"Media.{error.Error}",
            OperationCanceledException => "Canceled",
            HttpRequestException => "HttpRequest",
            UnauthorizedAccessException => "AccessDenied",
            IOException => "IO",
            JsonException => "InvalidJson",
            DllNotFoundException => "MissingNativeLibrary",
            EntryPointNotFoundException => "MissingNativeEntryPoint",
            System.Security.Cryptography.CryptographicException => "Cryptography",
            _ => "Unknown",
        };
        codes.Add(AllowedCodes.Contains(code) ? code : "Unknown");
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                Add(inner, codes);
            }
        }
        else
        {
            Add(exception.InnerException, codes);
        }
    }
}
