using BlazingRouter.Shared;

namespace BrDemo;

[AuthRoleEnum]
public enum Roles
{
    Unknown,
    User,
    Admin
}

[AuthRolePrefabsEnum]
public enum RolePrefabs
{
    /// <inheritdoc cref="RolePrefabsDocs.UserOrAdmin"/>
    UserOrAdmin
}