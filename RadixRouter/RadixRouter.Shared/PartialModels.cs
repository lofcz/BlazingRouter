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