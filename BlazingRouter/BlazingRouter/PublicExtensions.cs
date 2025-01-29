using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace BlazingRouter;

public interface IBaseBlazingRouterBuilder
{
    public bool HasAccess(ClaimsPrincipal user, int role);
    public Func<Type, List<Route>?>? OnPageScanned { get; }
}

public interface IBlazingRouterBuilder<TEnum> : IBaseBlazingRouterBuilder where TEnum : struct, Enum
{
    public IBlazingRouterBuilder<TEnum> Configure(Action<BlazingRouterContext<TEnum>> context);
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

    public IBlazingRouterBuilder<TEnum> Configure(Action<BlazingRouterContext<TEnum>> ctx)
    {
        ctx(context);
        return this;
    }
}


public class BlazingRouterContext<TEnum> where TEnum : Enum
{
    public Func<ClaimsPrincipal, TEnum, bool>? HasRole { get; set; }
    public Func<Type, List<Route>?>? OnPageScanned { get; set; }
}
