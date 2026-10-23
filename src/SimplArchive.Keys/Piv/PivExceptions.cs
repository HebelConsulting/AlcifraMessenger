namespace SimplArchive.Keys.Piv;

/// <summary>A refusal by the key, or by this app reading the key, worded for the person holding it.</summary>
public class PivException(string message) : Exception(message);

/// <summary>The PIN was wrong; the key counts down, and blocks at zero.</summary>
public sealed class PivWrongPinException(int retriesLeft)
    : PivException(retriesLeft == 0 ? "The PIN is blocked." : $"Wrong PIN. {retriesLeft} attempt(s) left before the key blocks it.")
{
    /// <summary>How many attempts remain before the key blocks the PIN; zero means it is blocked.</summary>
    public int RetriesLeft { get; } = retriesLeft;
}

/// <summary>The key answered a status word this app does not treat as success.</summary>
public sealed class PivStatusException(string operation, ushort statusWord)
    : PivException($"The key refused {operation} (status {statusWord:X4}).")
{
    /// <summary>The ISO 7816 status word the key answered (SW1SW2).</summary>
    public ushort StatusWord { get; } = statusWord;
}

/// <summary>The key's firmware has no attestation (it answers ATTEST with <c>6D00</c>): it cannot prove where any of its
/// keys was made.</summary>
public sealed class PivAttestationNotSupportedException()
    : PivException("This key cannot attest its keys: its firmware has no attestation.");

/// <summary>The key in a slot cannot be attested: it was imported onto the token rather than generated there, or the slot
/// holds none (ATTEST answers <c>6A80</c> or <c>6A82</c>).</summary>
public sealed class PivKeyNotAttestableException(byte slot, ushort statusWord)
    : PivException($"The key in slot {slot:X2} cannot be attested: it was imported rather than generated on the key, or the slot holds none.")
{
    /// <summary>The slot that was asked about.</summary>
    public byte Slot { get; } = slot;

    /// <summary>The ISO 7816 status word the key answered (SW1SW2).</summary>
    public ushort StatusWord { get; } = statusWord;
}
