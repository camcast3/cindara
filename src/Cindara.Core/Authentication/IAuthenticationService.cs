namespace Cindara.Core.Authentication;

public interface IAuthenticationService
{
    Task<AuthenticatedSession> AuthenticateAsync(
        AuthenticationRequest request,
        CancellationToken cancellationToken = default);
}
