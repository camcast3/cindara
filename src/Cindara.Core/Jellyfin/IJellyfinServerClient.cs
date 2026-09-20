using Cindara.Core.Models;

namespace Cindara.Core.Jellyfin;

public interface IJellyfinServerClient
{
    Task<ServerIdentity> ConnectAsync(
        string serverAddress,
        CancellationToken cancellationToken = default);
}
