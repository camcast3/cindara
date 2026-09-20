namespace Cindara.Core.Models;

public sealed record ServerIdentity(
    string Id,
    Uri BaseUri,
    string DisplayName,
    string Version,
    string? OperatingSystem);
