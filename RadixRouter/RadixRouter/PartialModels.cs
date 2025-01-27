namespace RadixRouter;

public class ParsedUrlQuery
{
    public Dictionary<string, object?> Pars { get; set; } = [];
    public int QueryStartCharIndex { get; set; }
}