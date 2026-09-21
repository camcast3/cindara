using System.Diagnostics;
using Cindara.Core.Models;

namespace Cindara.Core.Authentication;

[DebuggerDisplay("{Username,nq} on {Server.DisplayName,nq}")]
public sealed record AuthenticatedSession(
    ServerIdentity Server,
    string UserId,
    string Username,
    string AccessToken)
{
    public SessionProfile Profile => new(Server, UserId, Username);

    public override string ToString() => $"{Username} on {Server.DisplayName}";
}
