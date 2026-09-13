namespace RevitCodexBridge.Addin;

internal sealed class DocumentSessionTokens
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, string> _tokens = new();

    public string Get(Guid documentGuid)
    {
        lock (_sync)
        {
            if (!_tokens.TryGetValue(documentGuid, out var token))
                _tokens[documentGuid] = token = Guid.NewGuid().ToString("N");
            return token;
        }
    }

    public string Open(Guid documentGuid)
    {
        lock (_sync) return _tokens[documentGuid] = Guid.NewGuid().ToString("N");
    }

    public void Close(Guid documentGuid)
    {
        lock (_sync) _tokens.Remove(documentGuid);
    }
}
