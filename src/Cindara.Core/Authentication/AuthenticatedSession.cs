using Cindara.Core.Models;

namespace Cindara.Core.Authentication;

public sealed record AuthenticatedSession(
    ServerIdentity Server,
    string UserId,
    string Username,
    string AccessToken);
