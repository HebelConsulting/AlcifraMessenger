namespace Alcifra.Keys.Piv;

/// <summary>
/// One way of exchanging ISO 7816 APDUs with a hardware key: NFC (IsoDep) or USB (CCID). The PIV logic above it does
/// not know which. Synchronous because both Android transports block; the app calls it off the UI thread.
/// </summary>
public interface IApduTransport : IDisposable
{
    /// <summary>Sends one command APDU and answers the response APDU, status word included (the last two bytes).</summary>
    byte[] Transmit(byte[] command);
}
