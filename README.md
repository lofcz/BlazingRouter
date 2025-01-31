[![BlazingRouter](https://badgen.net/nuget/v/BlazingRouter?v=302&icon=nuget&label=BlazingRouter)](https://www.nuget.org/packages/BlazingRouter)
[![BlazingRouter.CodeFix](https://badgen.net/nuget/v/BlazingRouter.CodeFix?v=302&icon=nuget&label=BlazingRouter.CodeFix)](https://www.nuget.org/packages/BlazingRouter.CodeFix)

# BlazingRouter

<img align="left" width="128" height="128" alt="Te Reo Icon" src="https://github.com/user-attachments/assets/2e8033f1-ad2c-4756-8224-078bd39b0afb" />
Strongly typed router, focused on performance and covering even the most complex routing needs. Recall the default router in .NET: <code>[Authorize(Roles = "admin,developer")]</code>. Was it really <code>admin</code>? Maybe <code>administrator</code>? Maybe you've introduced a constant somewhere, like <code>static class Roles { public const string Admin = "admin"; }</code>. Is there something enforcing usage of it? Maybe you've derived your own attribute <code>[AuthorizeRole(Roles role)]</code>. BlazingRouter offers another approach - <em>ditch the string-based underlying structure entirely!</em>

<br/><br/>

## Getting Started

1. Install the library and analyzers / code-fix providers from NuGet:

```
dotnet add package BlazingRouter
dotnet add package BlazingRouter.CodeFix
```

2. Add a new file `Roles.cs` (name doesn't matter). Define your roles enum inside and decorate it with `[AuthRoleEnum]`:

```cs
namespace YourProject;

[AuthRoleEnum] 
public enum MyRoles
{
    User,
    Admin,
    Developer
}
```

3. Add the router to the services (in default Blazor template `Program.cs`):

```cs
builder.Services.AddBlazingRouter()
  .Configure(ctx =>
  {

  })
  .Build();
```

4. Replace `<Router>` with `<RouterExt>`:
```html
<RouterExt AppAssembly="@typeof(App).Assembly">
  <Found Context="routeData">
      <AuthorizeRouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)">
          <NotAuthorized></NotAuthorized>
          <Authorizing></Authorizing>
      </AuthorizeRouteView>
  </Found>
  <NotFound></NotFound>
</RouterExt>
```

5. Add folder `Pages` and place your `.razor` views in, using MVC conventions (`Controller`/`Action`). For example:
```
|- Program.cs
|- Pages
   |- Home
      |- Index.razor
      |- About.razor
```

_There's no need to add `@page ""` directives in the `.razor` files, the routing will work automatically. However, `@page` can still be used to define extra routes._

6. Make sure routing is set up so that `<RouterExt>` can act on any request. One way is to use `_Host.cshtml` fallback:
```cs
WebApplication app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseMvcWithDefaultRoute();

app.UseEndpoints(x =>
{
    x.MapBlazorHub();
    x.MapFallbackToPage("/_Host");
});

app.Run();
```

_From `Host.cshtml` we load `App` component which loads `<RouterExt>` in the demo setup._

## Reaping The Benefits

Now that the basic setup is in place, we get to sew the rewards of our work.

1. MVC routing works out of the box. Navigate to:
```cs
/           // resolves to /Home/Index
/Home       // this too
/Home/Index // this one as well
/Home/About // gets us to /Home/About.razor
```

2. Actions or whole controllers can be protected with `[AuthrorizeExt]`:
```razor
@* About.razor *@
@attribute [AuthorizeExt(Roles.Admin)]
```

3. To grant access to the _at least one of roles_ pattern, we can introduce another enum in `Roles.cs`:
```cs
[AuthRolePrefabsEnum]
public enum MyRolePrefabs
{
  /// <inheritdoc cref="MyRolePrefabsDocs.UserOrHigher"/> <-- ✨ magic documentation rendering roles to which the prefab is resolved!
  [RolePrefab([MyRoles.User], [AdminOrHigher])]
  UserOrHigher, // grant access to "user" or any role granted access by "AdminOrHigher" prefab

  /// <inheritdoc cref="MyRolePrefabsDocs.AdminOrHigher"/>
  [RolePrefab(MyRoles.Admin, MyRoles.Developer)]
  AdminOrHigher // grant access to "admin" or "developer"
}
```

4. Now we can use prefabs in `[AuthorizeExt]`:
```razor
@* About.razor *@
@attribute [AuthorizeExt(MyRolePrefabs.AdminOrHigher)]
```

5. With the implementation as above, all checks for roles silently fail and users are denied access. To fix this, we need to extend our configuration:
```cs
builder.Services.AddBlazingRouter()
  .Configure(ctx =>
  {
     ctx.HasRole = (principal, role) =>
     {
         // use ClaimsPrincipal to check for the role, "role" is strongly typed as "MyRoles"!
         return false;   
     }
  })
  .Build();
```

6. The configuration can be further extended to implement:
```cs
ctx.OnSetupAllowedUnauthorizedRoles = () => {} // which resources are available to unauthenticated users (by default none!)
ctx.OnRedirectUnauthorized = (user, route) => {} // where do we redirect the user if the resource requested is inaccessible
ctx.OnPageScanned = (type) => {} // enables associating extra routes with Pages, apart from the one picked by conventions. Great for route localization!
ctx.OnTypeDiscovered = (type) => {} // by default, only certain types are considered as Pages. Using this callback, extra types may be promoted to Pages
```

7. We can add routes at runtime:
```cs
RouteManager.AddRoute("/blog/{year:int}/{month:int}/{slug}", typeof(MyPage)); 
```

_Route syntax supports most of the features implemented by the default router, see the [docs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/routing) for syntax._

## Benchmark

The library was measured to perform `> 200 000 op/s` with `1 000` registered non-trivial routes on a `i7 8th gen` CPU. See the [benchmark](https://github.com/lofcz/BlazingRouter/tree/master/BlazingRouter/BlazingRouter.Benchmark).

## License

This library is licensed under the [MIT](https://github.com/lofcz/BlazingRouter/blob/master/LICENSE) license. 💜
