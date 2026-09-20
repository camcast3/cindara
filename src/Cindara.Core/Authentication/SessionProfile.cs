using Cindara.Core.Models;

namespace Cindara.Core.Authentication;

public sealed record SessionProfile(
    ServerIdentity Server,
    string UserId,
    string Username)
{
    public string DisplayName => $"{Username} - {Server.DisplayName}";
}
