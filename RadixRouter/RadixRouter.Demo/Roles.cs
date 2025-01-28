using RadixRouter.Shared;

namespace RadixRouter.Demo;

[AuthRoleEnum]
public enum MyRoles
{
    Unknown,
    User,
    Admin,
    Developer,
    Role4,
    Role5,
    Role6
}

[AuthRolePrefabsEnum]
public enum MyRolePrefabs
{
    /// <inheritdoc cref="MyRolePrefabsDocs.NewPrefab"/>
    [RolePrefab(MyRoles.Admin + 4)]
    NewPrefab,
    
    /// <inheritdoc cref="MyRolePrefabsDocs.AdminOrDeveloper"/>
    [RolePrefab([MyRoles.Admin, MyRoles.Developer, MyRoles.Role4])]
    AdminOrDeveloper,
    
    /// <inheritdoc cref="MyRolePrefabsDocs.DeveloperOrHigher"/>
    [RolePrefab(MyRoles.Developer)]
    DeveloperOrHigher,
    
    /// <inheritdoc cref="MyRolePrefabsDocs.AdminOrHigher"/>
    [RolePrefab([MyRoles.Admin], DeveloperOrHigher)]
    AdminOrHigher,
    
    /// <inheritdoc cref="MyRolePrefabsDocs.UserOrHigher"/>
    [RolePrefab([MyRoles.User, MyRoles.Role5], AdminOrHigher)]
    UserOrHigher
}

