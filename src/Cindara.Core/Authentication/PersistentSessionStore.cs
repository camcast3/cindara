using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cindara.Core.Authentication;

public sealed class PersistentSessionStore(
    string indexPath,
    ISecureCredentialStore credentialStore) : ISessionStore, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _mutex = new(1, 1);

    public void Dispose() => _mutex.Dispose();

    public async Task<IReadOnlyList<SessionProfile>> GetProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadProfilesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<AuthenticatedSession?> GetAsync(
        SessionProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var token = await credentialStore
            .GetAsync(GetCredentialKey(profile), cancellationToken)
            .ConfigureAwait(false);

        return token is null
            ? null
            : new AuthenticatedSession(profile.Server, profile.UserId, profile.Username, token);
    }

    public async Task SaveAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ValidateSession(session);

        var profile = session.Profile;
        var credentialKey = GetCredentialKey(profile);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profiles = await ReadProfilesAsync(cancellationToken).ConfigureAwait(false);
            var previousToken = await credentialStore
                .GetAsync(credentialKey, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await credentialStore
                    .SetAsync(credentialKey, session.AccessToken, cancellationToken)
                    .ConfigureAwait(false);
                var updated = profiles
                    .Where(saved => !HasSameKey(saved, profile))
                    .Append(profile)
                    .OrderBy(saved => saved.Server.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(saved => saved.Username, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                await WriteProfilesAsync(updated, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception persistenceException)
            {
                try
                {
                    if (previousToken is null)
                    {
                        await credentialStore.RemoveAsync(credentialKey, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await credentialStore
                            .SetAsync(credentialKey, previousToken, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception rollbackException)
                {
                    throw new SessionStoreException(
                        SessionStoreError.PersistenceFailure,
                        "Session metadata could not be saved and the previous credential could not be restored.",
                        new AggregateException(persistenceException, rollbackException));
                }

                throw;
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<bool> RemoveAsync(
        SessionProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profiles = await ReadProfilesAsync(cancellationToken).ConfigureAwait(false);
            var updated = profiles.Where(saved => !HasSameKey(saved, profile)).ToArray();
            var existed = updated.Length != profiles.Count;
            var credentialKey = GetCredentialKey(profile);
            var previousToken = await credentialStore
                .GetAsync(credentialKey, cancellationToken)
                .ConfigureAwait(false);
            var credentialRemoved = await credentialStore
                .RemoveAsync(credentialKey, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                if (existed)
                {
                    await WriteProfilesAsync(updated, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception persistenceException) when (previousToken is not null)
            {
                try
                {
                    await credentialStore
                        .SetAsync(credentialKey, previousToken, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception rollbackException)
                {
                    throw new SessionStoreException(
                        SessionStoreError.PersistenceFailure,
                        "Session metadata could not be updated and the credential could not be restored.",
                        new AggregateException(persistenceException, rollbackException));
                }

                throw;
            }

            return existed || credentialRemoved;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<IReadOnlyList<SessionProfile>> ReadProfilesAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(indexPath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(indexPath);
            var profiles = await JsonSerializer
                .DeserializeAsync<SessionProfile[]>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false) ?? [];
            foreach (var profile in profiles)
            {
                ValidateProfile(profile);
            }

            return profiles;
        }
        catch (JsonException exception)
        {
            throw new SessionStoreException(
                SessionStoreError.InvalidData,
                "Saved session metadata is damaged and could not be read.",
                exception);
        }
        catch (IOException exception)
        {
            throw new SessionStoreException(
                SessionStoreError.PersistenceFailure,
                "Saved session metadata could not be read.",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new SessionStoreException(
                SessionStoreError.PersistenceFailure,
                "Cindara does not have permission to read saved session metadata.",
                exception);
        }
    }

    private async Task WriteProfilesAsync(
        IReadOnlyCollection<SessionProfile> profiles,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(indexPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new SessionStoreException(
                SessionStoreError.PersistenceFailure,
                "The saved session metadata path is invalid.");
        }

        var temporaryPath = $"{indexPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(directory);
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer
                    .SerializeAsync(stream, profiles, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, indexPath, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SessionStoreException(
                SessionStoreError.PersistenceFailure,
                "Saved session metadata could not be updated.",
                exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool HasSameKey(SessionProfile left, SessionProfile right) =>
        string.Equals(left.Server.Id, right.Server.Id, StringComparison.Ordinal)
        && string.Equals(left.UserId, right.UserId, StringComparison.Ordinal);

    private static string GetCredentialKey(SessionProfile profile)
    {
        var source = Encoding.UTF8.GetBytes($"{profile.Server.Id}\n{profile.UserId}");
        return Convert.ToHexStringLower(SHA256.HashData(source));
    }

    private static void ValidateSession(AuthenticatedSession session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(session.Server.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.UserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.AccessToken);
    }

    private static void ValidateProfile(SessionProfile? profile)
    {
        if (profile?.Server is null
            || string.IsNullOrWhiteSpace(profile.Server.Id)
            || profile.Server.BaseUri is null
            || !profile.Server.BaseUri.IsAbsoluteUri
            || string.IsNullOrWhiteSpace(profile.Server.DisplayName)
            || string.IsNullOrWhiteSpace(profile.UserId)
            || string.IsNullOrWhiteSpace(profile.Username))
        {
            throw new SessionStoreException(
                SessionStoreError.InvalidData,
                "Saved session metadata contains an invalid account.");
        }
    }
}
