namespace Cindara.Core.Authentication;

public sealed record JellyfinClientIdentity(
    string ClientName,
    string DeviceName,
    string DeviceId,
    string Version);
