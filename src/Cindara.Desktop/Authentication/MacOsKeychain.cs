using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Cindara.Core.Authentication;

namespace Cindara.Desktop.Authentication;

[SupportedOSPlatform("macos")]
internal static partial class MacOsKeychain
{
    private const int ItemNotFound = -25300;
    private const string ServiceName = "Cindara";

    public static string? Get(string key)
    {
        var service = Encoding.UTF8.GetBytes(ServiceName);
        var account = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            (uint)service.Length,
            service,
            (uint)account.Length,
            account,
            out var passwordLength,
            out var passwordData,
            out var item);

        if (status == ItemNotFound)
        {
            return null;
        }

        ThrowIfFailed(status, "read");
        try
        {
            var password = new byte[passwordLength];
            Marshal.Copy(passwordData, password, 0, password.Length);
            try
            {
                return Encoding.UTF8.GetString(password);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(password);
            }
        }
        finally
        {
            _ = SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
            if (item != IntPtr.Zero)
            {
                CFRelease(item);
            }
        }
    }

    public static void Set(string key, string secret)
    {
        var service = Encoding.UTF8.GetBytes(ServiceName);
        var account = Encoding.UTF8.GetBytes(key);
        var password = Encoding.UTF8.GetBytes(secret);
        try
        {
            var status = SecKeychainFindGenericPassword(
                IntPtr.Zero,
                (uint)service.Length,
                service,
                (uint)account.Length,
                account,
                out _,
                out var existingPassword,
                out var item);

            if (status == 0)
            {
                try
                {
                    ThrowIfFailed(
                        SecKeychainItemModifyAttributesAndData(
                            item,
                            IntPtr.Zero,
                            (uint)password.Length,
                            password),
                        "update");
                    return;
                }
                finally
                {
                    _ = SecKeychainItemFreeContent(IntPtr.Zero, existingPassword);
                    CFRelease(item);
                }
            }

            if (status != ItemNotFound)
            {
                ThrowIfFailed(status, "find");
            }

            ThrowIfFailed(
                SecKeychainAddGenericPassword(
                    IntPtr.Zero,
                    (uint)service.Length,
                    service,
                    (uint)account.Length,
                    account,
                    (uint)password.Length,
                    password,
                    out var addedItem),
                "save");
            if (addedItem != IntPtr.Zero)
            {
                CFRelease(addedItem);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
        }
    }

    public static bool Remove(string key)
    {
        var service = Encoding.UTF8.GetBytes(ServiceName);
        var account = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            (uint)service.Length,
            service,
            (uint)account.Length,
            account,
            out _,
            out var passwordData,
            out var item);
        if (status == ItemNotFound)
        {
            return false;
        }

        ThrowIfFailed(status, "find");
        try
        {
            ThrowIfFailed(SecKeychainItemDelete(item), "remove");
            return true;
        }
        finally
        {
            _ = SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
            CFRelease(item);
        }
    }

    private static void ThrowIfFailed(int status, string operation)
    {
        if (status != 0)
        {
            throw new SessionStoreException(
                SessionStoreError.SecureStorageUnavailable,
                $"macOS Keychain could not {operation} the Cindara credential (status {status}).");
        }
    }

    [LibraryImport(
        "/System/Library/Frameworks/Security.framework/Security",
        EntryPoint = "SecKeychainFindGenericPassword")]
    private static partial int SecKeychainFindGenericPassword(
        IntPtr keychainOrArray,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        out uint passwordLength,
        out IntPtr passwordData,
        out IntPtr itemRef);

    [LibraryImport(
        "/System/Library/Frameworks/Security.framework/Security",
        EntryPoint = "SecKeychainAddGenericPassword")]
    private static partial int SecKeychainAddGenericPassword(
        IntPtr keychain,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        uint passwordLength,
        byte[] passwordData,
        out IntPtr itemRef);

    [LibraryImport(
        "/System/Library/Frameworks/Security.framework/Security",
        EntryPoint = "SecKeychainItemModifyAttributesAndData")]
    private static partial int SecKeychainItemModifyAttributesAndData(
        IntPtr itemRef,
        IntPtr attributes,
        uint dataLength,
        byte[] data);

    [LibraryImport(
        "/System/Library/Frameworks/Security.framework/Security",
        EntryPoint = "SecKeychainItemDelete")]
    private static partial int SecKeychainItemDelete(IntPtr itemRef);

    [LibraryImport(
        "/System/Library/Frameworks/Security.framework/Security",
        EntryPoint = "SecKeychainItemFreeContent")]
    private static partial int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);

    [LibraryImport(
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation",
        EntryPoint = "CFRelease")]
    private static partial void CFRelease(IntPtr value);
}
