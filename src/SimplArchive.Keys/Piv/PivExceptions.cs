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
