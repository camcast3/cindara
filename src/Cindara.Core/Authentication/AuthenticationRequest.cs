using System.Diagnostics;
using Cindara.Core.Models;

namespace Cindara.Core.Authentication;

[DebuggerDisplay("{Username,nq} on {Server.DisplayName,nq}")]
public sealed record AuthenticationRequest(
    ServerIdentity Server,
    string Username,
    string Password)
{
    public override string ToString() => $"{Username} on {Server.DisplayName}";
}
