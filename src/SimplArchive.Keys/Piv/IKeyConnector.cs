namespace SimplArchive.Keys.Piv;

/// <summary>How the hardware key reaches the device.</summary>
public enum KeyConnection
{
    /// <summary>Held to the device (NFC).</summary>
    Nfc,

    /// <summary>Plugged in (USB-C).</summary>
    Usb,
}

/// <summary>
/// Connects to the hardware key: the one platform-specific step of opening an envelope. Each app implements it for its
/// platform (NFC, USB); everything after the connection (PIV, the envelope) is this library's and shared.
/// </summary>
public interface IKeyConnector
{
    /// <summary>A transport to the key, once it is tapped or plugged in and the user has allowed access.</summary>
    /// <exception cref="PivException">This device cannot reach a key that way, saying why (no NFC, NFC off…).</exception>
    Task<IApduTransport> ConnectAsync(KeyConnection connection, CancellationToken cancellationToken);
}
