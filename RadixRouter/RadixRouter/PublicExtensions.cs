using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace RadixRouter;

public static class PublicExtensions
{
    public static void AddBlazingRouter(this IServiceCollection services, Assembly assembly)
    {
        services.AddSingleton<RouteManager>();
        RouteManager.InitRouteManager(assembly);
    }
}