using Cindara.Core.Models;

namespace Cindara.Core.Authentication;

public sealed record AuthenticationRequest(
    ServerIdentity Server,
    string Username,
    string Password);
