namespace SimplArchive.Keys.Envelopes;

/// <summary>An envelope this app cannot open, with the reason worded for the person holding the phone.</summary>
public sealed class EnvelopeException(string reason, Exception? inner = null)
    : Exception($"The envelope cannot be opened: {reason}.", inner);
