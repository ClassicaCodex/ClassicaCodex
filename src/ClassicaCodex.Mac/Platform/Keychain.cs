using System.Runtime.InteropServices;
using System.Text;

namespace ClassicaCodex.Mac.Platform;

/// <summary>
/// The macOS login keychain, for the API keys.
///
/// Windows keeps the keys in a file encrypted with DPAPI, which ties them to
/// the Windows account. The Mac has no DPAPI - ProtectedData throws on any
/// other system - and the right place on a Mac is the keychain anyway: it is
/// where Keychain Access will show the key, where Time Machine and iCloud
/// handle it properly, and where every other app's secrets already are.
///
/// The generic-password calls are the old ones, deprecated in favour of
/// SecItemAdd and friends, but still present in every macOS and far simpler
/// to call: three strings in, a buffer out. An app that is not sandboxed
/// needs no entitlement to use them.
/// </summary>
internal static class Keychain
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int ErrSecItemNotFound = -25300;

    /// <summary>The service name the items are filed under in Keychain Access.</summary>
    internal const string Service = "Classica Codex";

    public static bool IsAvailable => OperatingSystem.IsMacOS();

    /// <summary>The secret stored for <paramref name="account"/>, or null if there is none.</summary>
    public static string? Read(string account)
    {
        if (!IsAvailable) return null;

        var service = Encoding.UTF8.GetBytes(Service);
        var name = Encoding.UTF8.GetBytes(account);

        var status = SecKeychainFindGenericPassword(
            IntPtr.Zero, (uint)service.Length, service, (uint)name.Length, name,
            out var length, out var data, out var item);

        if (status != 0) return null;

        try
        {
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, (int)length);
            var value = Encoding.UTF8.GetString(bytes);
            return value.Length == 0 ? null : value;
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
            if (item != IntPtr.Zero) CFRelease(item);
        }
    }

    /// <summary>
    /// Stores <paramref name="secret"/> for <paramref name="account"/>, replacing
    /// whatever was there; null or empty removes the item.
    /// </summary>
    /// <returns>False when the keychain refused, so the caller can say so.</returns>
    public static bool Write(string account, string? secret)
    {
        if (!IsAvailable) return false;

        var service = Encoding.UTF8.GetBytes(Service);
        var name = Encoding.UTF8.GetBytes(account);

        var found = SecKeychainFindGenericPassword(
            IntPtr.Zero, (uint)service.Length, service, (uint)name.Length, name,
            out _, out var oldData, out var item);

        try
        {
            if (found == 0 && oldData != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, oldData);

            if (string.IsNullOrEmpty(secret))
            {
                return found != 0 || SecKeychainItemDelete(item) == 0;
            }

            var bytes = Encoding.UTF8.GetBytes(secret);

            if (found == 0)
                return SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)bytes.Length, bytes) == 0;

            if (found != ErrSecItemNotFound) return false;

            var added = SecKeychainAddGenericPassword(
                IntPtr.Zero, (uint)service.Length, service, (uint)name.Length, name,
                (uint)bytes.Length, bytes, out var newItem);

            if (newItem != IntPtr.Zero) CFRelease(newItem);
            return added == 0;
        }
        finally
        {
            if (item != IntPtr.Zero) CFRelease(item);
        }
    }

    [DllImport(Security)]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychainOrArray, uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName,
        out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainAddGenericPassword(
        IntPtr keychain, uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName,
        uint passwordLength, byte[] passwordData, out IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainItemModifyAttributesAndData(
        IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

    [DllImport(Security)]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);
}
