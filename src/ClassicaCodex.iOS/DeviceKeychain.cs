using ClassicaCodex.Mac.Platform;
using Foundation;
using Security;

namespace ClassicaCodex.iOS;

/// <summary>
/// The iOS keychain, for the API keys. Each key is readable while the iPad is
/// unlocked and stays on this iPad, out of backups and off any other device.
/// </summary>
internal sealed class DeviceKeychain : ISecretStore
{
    public string? Read(string service, string account)
    {
        var data = SecKeyChain.QueryAsData(Item(service, account), false, out var status);
        return status == SecStatusCode.Success ? data?.ToString(NSStringEncoding.UTF8) : null;
    }

    public bool Write(string service, string account, string? secret)
    {
        var removed = SecKeyChain.Remove(Item(service, account));
        if (removed != SecStatusCode.Success && removed != SecStatusCode.ItemNotFound) return false;
        if (string.IsNullOrEmpty(secret)) return true;

        var item = Item(service, account);
        item.Accessible = SecAccessible.WhenUnlockedThisDeviceOnly;
        item.ValueData = NSData.FromString(secret, NSStringEncoding.UTF8);
        return SecKeyChain.Add(item) == SecStatusCode.Success;
    }

    private static SecRecord Item(string service, string account) =>
        new(SecKind.GenericPassword) { Service = service, Account = account };
}
