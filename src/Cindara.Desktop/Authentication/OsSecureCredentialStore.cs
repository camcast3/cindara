using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Cindara.Core.Authentication;

namespace Cindara.Desktop.Authentication;

internal sealed class OsSecureCredentialStore(string credentialDirectory) : ISecureCredentialStore
{
    private static readonly byte[] WindowsEntropy = Encoding.UTF8.GetBytes("Cindara.Session.v1");

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);

        if (OperatingSystem.IsWindows())
        {
            return GetWindowsAsync(key, cancellationToken);
        }

        if (OperatingSystem.IsLinux())
        {
            return RunSecretToolAsync(
                ["lookup", "service", "Cindara", "account", key],
                null,
                1,
                cancellationToken);
        }

        if (OperatingSystem.IsMacOS())
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(MacOsKeychain.Get(key));
        }

        throw UnsupportedPlatform();
    }

    public async Task SetAsync(
        string key,
        string secret,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        if (OperatingSystem.IsWindows())
        {
            await SetWindowsAsync(key, secret, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            await RunSecretToolAsync(
                ["store", "--label=Cindara", "service", "Cindara", "account", key],
                secret,
                null,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            cancellationToken.ThrowIfCancellationRequested();
            MacOsKeychain.Set(key, secret);
            return;
        }

        throw UnsupportedPlatform();
    }

    public async Task<bool> RemoveAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(key);

        if (OperatingSystem.IsWindows())
        {
            var path = GetWindowsCredentialPath(key);
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw StorageFailure("The Windows credential could not be removed.", exception);
            }
        }

        if (OperatingSystem.IsLinux())
        {
            return await RunRemoveAsync(
                "secret-tool",
                ["clear", "service", "Cindara", "account", key],
                cancellationToken).ConfigureAwait(false);
        }

        if (OperatingSystem.IsMacOS())
        {
            cancellationToken.ThrowIfCancellationRequested();
            return MacOsKeychain.Remove(key);
        }

        throw UnsupportedPlatform();
    }

    [SupportedOSPlatform("windows")]
    private async Task<string?> GetWindowsAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetWindowsCredentialPath(key);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var encrypted = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var clear = ProtectedData.Unprotect(
                encrypted,
                WindowsEntropy,
                DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.UTF8.GetString(clear);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clear);
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or CryptographicException)
        {
            throw StorageFailure("The Windows credential could not be decrypted.", exception);
        }
    }

    [SupportedOSPlatform("windows")]
    private async Task SetWindowsAsync(
        string key,
        string secret,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clear = Encoding.UTF8.GetBytes(secret);
        try
        {
            var encrypted = ProtectedData.Protect(
                clear,
                WindowsEntropy,
                DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(credentialDirectory);
            var credentialPath = GetWindowsCredentialPath(key);
            var temporaryPath = $"{credentialPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(
                    temporaryPath,
                    encrypted,
                    cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, credentialPath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or CryptographicException)
        {
            throw StorageFailure("The Windows credential could not be protected.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
        }
    }

    private string GetWindowsCredentialPath(string key) =>
        Path.Combine(credentialDirectory, $"{key}.credential");

    private static Task<string?> RunSecretToolAsync(
        IReadOnlyList<string> arguments,
        string? standardInput,
        int? notFoundExitCode,
        CancellationToken cancellationToken) =>
        RunCommandAsync(
            "secret-tool",
            arguments,
            standardInput,
            notFoundExitCode,
            cancellationToken);

    private static async Task<bool> RunRemoveAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await RunCommandAsync(
            executable,
            arguments,
            null,
            1,
            cancellationToken).ConfigureAwait(false);
        return result is not null;
    }

    private static async Task<string?> RunCommandAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? standardInput,
        int? notFoundExitCode,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"{executable} did not start.");
            }

            if (standardInput is not null)
            {
                await process.StandardInput.WriteLineAsync(standardInput).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = (await outputTask.ConfigureAwait(false)).TrimEnd('\r', '\n');
            var error = (await errorTask.ConfigureAwait(false)).Trim();

            if (process.ExitCode == 0)
            {
                return string.IsNullOrEmpty(output) ? string.Empty : output;
            }

            if (process.ExitCode == notFoundExitCode && string.IsNullOrEmpty(error))
            {
                return null;
            }

            throw StorageFailure(
                $"The operating system credential service rejected the request: {error}");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }

            throw;
        }
        catch (Exception exception) when (
            exception is Win32Exception
                or InvalidOperationException
                or IOException)
        {
            throw StorageFailure(
                "The operating system credential service is unavailable.",
                exception);
        }
    }

    private static void ValidateKey(string key) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

    private static SessionStoreException UnsupportedPlatform() =>
        StorageFailure("Secure credential storage is not supported on this operating system.");

    private static SessionStoreException StorageFailure(
        string message,
        Exception? exception = null) =>
        new(SessionStoreError.SecureStorageUnavailable, message, exception);
}
