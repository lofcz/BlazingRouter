using System;
using System.Collections.Generic;

namespace BlazingRouter.Shared;

public interface IRole
{
    string Name { get; }
    int Value { get; }
}

/// <summary>
/// Mark the enum containing your roles with this attributed. This attribute can be applied only once per project.<br/>
/// Once marked, the following will be generated:<br/>
/// 1. [AuthorizeExt] attribute which can be used to limit access to .razor pages, .cs controllers/actions
/// </summary>
[AttributeUsage(AttributeTargets.Enum)]
public class AuthRoleEnumAttribute : Attribute
{
    
}

/// <summary>
/// Mark the enum containing your role prefabs (role discriminated unions) with this attribute. This attribute can be applied only once per project.<br/>
/// </summary>
[AttributeUsage(AttributeTargets.Enum)]
public class AuthRolePrefabsEnumAttribute : Attribute
{
    
}

public abstract class AuthorizeExtAttributeBase : Attribute
{
    public abstract IReadOnlyList<IRole>? Roles { get; }
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