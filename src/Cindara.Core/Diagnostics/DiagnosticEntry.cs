using System.Text.Json.Serialization;

namespace Cindara.Core.Diagnostics;

[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticLevel>))]
public enum DiagnosticLevel { Debug, Information, Warning, Error }

[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticArea>))]
public enum DiagnosticArea { Startup, Authentication, Network, Storage, Controller, Playback, Support }

[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticAction>))]
public enum DiagnosticAction
{
    Start, Stop, Connect, LoadSessions, SignIn, RestoreSession, RemoveSession, SignOut,
    LoadHome, Request, InitializeController, OpenController, ControllerConnected,
    ControllerDisconnected, PollController, PlaybackUnavailable, PreviewBundle, ExportBundle,
}

[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticOutcome>))]
public enum DiagnosticOutcome { Started, Completed, Failed, Canceled, Unavailable }

public sealed record DiagnosticEntry(
    DateTimeOffset Timestamp,
    DateTimeOffset RunStarted,
    long Operation,
    DiagnosticLevel Level,
    DiagnosticArea Area,
    DiagnosticAction Action,
    DiagnosticOutcome Outcome,
    long? ElapsedMilliseconds,
    int? HttpStatus,
    string[] Errors);
