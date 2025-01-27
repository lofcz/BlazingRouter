using System;
using System.Collections.Generic;

namespace RadixRouter.Shared;

public interface IRole
{
    string Name { get; }
    int Value { get; }
}

[AttributeUsage(AttributeTargets.Enum)]
public class AuthRoleEnumAttribute : Attribute { }

public abstract class AuthorizeExtAttributeBase : Attribute
{
    public abstract IReadOnlyList<IRole> Roles { get; }
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
/// All unauthorized visitors will be redirected to a specified page
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