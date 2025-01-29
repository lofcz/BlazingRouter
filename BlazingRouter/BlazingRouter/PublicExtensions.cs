using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using BlazingRouter.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace BlazingRouter;

public interface IBaseBlazingRouterBuilder
{
    public bool HasAccess(ClaimsPrincipal user, int role);
    public Func<Type, List<Route>?>? OnPageScanned { get; }
    public Func<HashSet<string>?>? OnSetupAllowedUnauthorizedRoles { get; }
    public Func<ClaimsPrincipal?, string, string?>? OnRedirectUnauthorized { get; }
}

public interface IBlazingRouterBuilder<TEnum> : IBaseBlazingRouterBuilder where TEnum : struct, Enum
{
    /// <summary>
    /// This can be used to configure BlazingRouter. Should be only used once.
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    public IBlazingRouterBuilder<TEnum> Configure(Action<BlazingRouterContext<TEnum>> context);
    
    /// <summary>
    /// This must be called as the last step of initialization pipeline.
    /// Consider calling <see cref="Configure"/> before this.
    /// </summary>
    /// <param name="assembly">The current assembly will be used by default.</param>
    public void Build(Assembly? assembly = null);
}

public class BlazingRouterBuilder<TEnum> : IBlazingRouterBuilder<TEnum> where TEnum : struct, Enum
{
    private readonly BlazingRouterContext<TEnum> context = new BlazingRouterContext<TEnum>();
    private readonly HashSet<int> validValues;
    
    public BlazingRouterBuilder()
    {
        validValues = new HashSet<int>(Enum.GetValues<TEnum>().Select(x => Convert.ToInt32(x)));
    }

    public bool HasAccess(ClaimsPrincipal user, int role)
    {
        return validValues.Contains(role) && context.HasRole is not null && context.HasRole(user, Unsafe.As<int, TEnum>(ref role));
    }

    public void Build(Assembly? assembly = null)
    {
        RouteManager.InitRouteManager(assembly ?? Assembly.GetCallingAssembly(), this);
    }

    public Func<Type, List<Route>?>? OnPageScanned => context.OnPageScanned;
    public Func<HashSet<string>?>? OnSetupAllowedUnauthorizedRoles => context.OnSetupAllowedUnauthorizedRoles;
    public Func<ClaimsPrincipal?, string, string?>? OnRedirectUnauthorized => context.OnRedirectUnauthorized;

    public IBlazingRouterBuilder<TEnum> Configure(Action<BlazingRouterContext<TEnum>> ctx)
    {
        ctx(context);
        return this;
    }
}

public class BlazingRouterContext<TEnum> where TEnum : Enum
{
    /// <summary>
    /// Invoked when a user tries to access page protected by authorization rules.
    /// </summary>
    public Func<ClaimsPrincipal, TEnum, bool>? HasRole { get; set; }
        
    /// <summary>
    /// Invoked when a Page-like type is detected during setup. Use this to return additional routes associated with the type if needed.
    /// </summary>
    public Func<Type, List<Route>?>? OnPageScanned { get; set; }
    
    /// <summary>
    /// Invoked during initialization, use this mark any routes as accessible to unauthorized users.
    /// Routes for pages marked with <see cref="AuthorizeAnyone"/> or <see cref="AuthorizeUnauthorized"/> are added automatically.
    /// </summary>
    public Func<HashSet<string>?>? OnSetupAllowedUnauthorizedRoles { get; set; }
    
    /// <summary>
    /// Invoked whenever user is not authorized to access the route. User may or may not be authenticated.
    /// Arguments: user, route accessed.
    /// </summary>
    public Func<ClaimsPrincipal?, string, string?>? OnRedirectUnauthorized { get; set; }
}
