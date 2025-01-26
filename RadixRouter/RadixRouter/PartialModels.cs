namespace RadixRouter;

public class ParsedUrlQuery
{
    public Dictionary<string, object?> Pars { get; set; } = [];
    public int QueryStartCharIndex { get; set; }
}

/// <summary>
/// Only not logged in visitors can visit this page
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AuthorizeUnauthorized : Attribute
{
    
}

/// <summary>
/// Anyone can visit this page, regardless of authentication and authorization
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AuthorizeAnyone : Attribute
{
    
}

/// <summary>
/// Anyone can visit this page, regardless of authentication & authorization
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AuthorizePublic : Attribute
{
    
}

/// <summary>
/// All unauthrorized visitors will be redirect to specified page
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RedirectUnauthorized : Attribute
{
    public string Redirect { get; set; }
    
    public RedirectUnauthorized(string redirect)
    {
        Redirect = redirect;
    }
}

public interface IRole
{
    string Name { get; }
    int Value { get; }
}

public class AuthorizeExt : Attribute
{
    public List<IRole> TypedRoles { get; set; }

    public AuthorizeExt(IRole role)
    {
        TypedRoles = new List<IRole> {role};
    }

    public AuthorizeExt(IEnumerable<IRole>? roles)
    {
        TypedRoles = roles?.ToList() ?? new List<IRole>();
    }
    
    public AuthorizeExt(params IRole[] roles)
    {
        TypedRoles = roles.ToList();
    }
}