using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using BlazingCore;
using BlazingRouter.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Caching.Memory;
using BlazingRouter.Shared;

namespace BlazingRouter;

public class MatchResult
{
    public bool IsMatch { get; set; }
    public Route? MatchedRoute { get; set; }
    public Route? BestMatchedRoute { get; set; }
    public Dictionary<string, string>? Params { get; set; }

    public MatchResult(bool isMatch, Route? matchedRoute, Dictionary<string, string>? pars = null, Route? bestMatchedRoute = null)
    {
        IsMatch = isMatch;
        MatchedRoute = matchedRoute;
        BestMatchedRoute = bestMatchedRoute;
        Params = pars;
    }

    public static MatchResult Match(Route? matchedRoute, Dictionary<string, string>? parameters = null)
    {
        return new MatchResult(true, matchedRoute, parameters);
    }

    public static MatchResult NoMatch()
    {
        return new MatchResult(false, null);
    }
}

public class RouteManager
{
    public static BlazingRouter Router;
    
    internal static readonly List<Route> Routes = [];
    internal static IBaseBlazingRouterBuilder Builder;
    
    private static List<Type> PageComponentTypes = [];
    private static readonly List<Route> IndexRoutes = [];
    internal static Route? IndexHomeRoute;
    private static readonly HashSet<string> Controllers = [];
    
    public RouteManager(IMemoryCache cache)
    {
      
    }

    public static void AddRoute(Route route)
    {
        Router.Tree.AddRoute(route);
    }
    
    /// <summary>
    /// When routing, add implicit routing rule resolving /<see cref="name"/> as /<see cref="name"/>/index.
    /// </summary>
    /// <param name="name">Name of the controller</param>
    public static void AddController(string name)
    {
        Controllers.Add(name.ToLowerInvariant());
    }

    public static void InitRouteManager(Assembly assembly, IBaseBlazingRouterBuilder builder)
    {
        Builder = builder;

        HashSet<string>? allowUnauthorizedRoutes = builder.OnSetupAllowedUnauthorizedRoles?.Invoke();

        if (allowUnauthorizedRoutes != null)
        {
            foreach (string str in allowUnauthorizedRoutes)
            {
                RouterExt.AllowedUnauthorizedUrls.Add(str);
            }
        }

        foreach (Type type in assembly.ExportedTypes)
        {
            if (type.Namespace is null)
            {
                continue;
            }

            bool isStandardPage = (type.IsSubclassOf(typeof(ComponentBase)) || type.IsSubclassOf(typeof(ComponentBaseInternal))) && type.Namespace.Contains(".Pages");
            bool routeAdded = false;
            
            if (isStandardPage)
            {
                string[]? segments = type.FullName?[(type.FullName.IndexOf("Pages", StringComparison.OrdinalIgnoreCase) + 6)..]?.Split('.');
                
                if (segments?.Length > 0)
                {
                    string template = string.Join('/', segments);

                    PageComponentTypes.Add(type);
                    AddController(segments[0]);
                    Routes.Add(new Route(template, type));
                    routeAdded = true;
            
                    List<RouteAttribute> routes = type.GetCustomAttributes<RouteAttribute>().ToList();

                    if (routes.Count > 0)
                    {
                        foreach (RouteAttribute route in routes)
                        {
                            Routes.Add(new Route(route.Template, type));
                        }
                    }
                }
            }
            else
            {
                Route? route = builder.OnTypeDiscovered?.Invoke(type);

                if (route is not null)
                {
                    routeAdded = true;
                    PageComponentTypes.Add(type);
                    Routes.Add(route);
                }
            }

            if (routeAdded)
            {
                List<Route> addedRoutes = builder.OnPageScanned?.Invoke(type) ?? [];
                Routes.AddRange(addedRoutes);   
            }
        }

        foreach (Route route in Routes)
        {
            Attribute? onlyUnauthorizedAttr = Attribute.GetCustomAttribute(route.Handler, typeof(AuthorizeUnauthorized));
            Attribute? anyoneAttr = Attribute.GetCustomAttribute(route.Handler, typeof(AuthorizeAnyone));
            Attribute? redirectUnauthorizedAttr = Attribute.GetCustomAttribute(route.Handler, typeof(RedirectUnauthorized));

            bool onlyUnauthorized = onlyUnauthorizedAttr is not null;
            bool anyone = anyoneAttr is not null;
            bool redirectUnauthorized = redirectUnauthorizedAttr is not null;

            if (onlyUnauthorized && !anyone)
            {
                route.OnlyUnauthorized = true;
            }

            if (onlyUnauthorized || anyone)
            {
                AddToUnauthorizedRoutes(route.Template);
            }

            if (redirectUnauthorized && redirectUnauthorizedAttr is not null)
            {
                route.RedirectUnauthorized = true;
                route.RedirectUnauthorizedUrl = ((RedirectUnauthorized)redirectUnauthorizedAttr).Redirect;
            }
            
            AuthorizeExtAttributeBase? authAttr = (AuthorizeExtAttributeBase?)Attribute.GetCustomAttributes(route.Handler, typeof(AuthorizeExtAttributeBase), inherit: true).FirstOrDefault();

            if (authAttr is not null && !anyone && authAttr.Roles?.Count > 0)
            {
                route.AuthorizedRoles = new List<IRole>(authAttr.Roles);
            }

            route.TypeFullnameLower = route.Handler.FullName?.ToLowerInvariant() ?? string.Empty;
            route.EndsWithIndex = route.TypeFullnameLower.EndsWith("index");

            if (route.EndsWithIndex)
            {
                IndexRoutes.Add(route);

                if (route.UriSegments is { Count: > 0 } && route.UriSegments[0].ToLowerInvariant() is "home")
                {
                    IndexHomeRoute = route;
                }
            }

            if (anyone)
            {
                Attribute[] pageDirectives = Attribute.GetCustomAttributes(route.Handler, typeof(RouteAttribute));

                foreach (Attribute attr in pageDirectives)
                {
                    if (attr is RouteAttribute rAttr)
                    {
                        string r = rAttr.Template;

                        if (r.StartsWith('/'))
                        {
                            r = r[1..];
                        }

                        AddToUnauthorizedRoutes(r);
                    }
                }

                if (route.UriSegments is not null)
                {
                    AddToUnauthorizedRoutes(string.Join('/', route.UriSegments));
                }
            }
        }

        Router = new BlazingRouter(Routes);
        RouterExt.SetupUnauthorizedRouterExt();
        return;

        void AddToUnauthorizedRoutes(string str)
        {
            str = str.Trim().ToLowerInvariant();
            RouterExt.AllowedUnauthorizedUrls.Add(str);
        }
    }

    public static bool TryMatch(string route, [NotNullWhen(true)] out MatchResult? result, out Exception? exception, ClaimsPrincipal? principal = null)
    {
        try
        {
            exception = null;
            result = Match(route.Split("/", StringSplitOptions.RemoveEmptyEntries), principal);
            return result.IsMatch;
        }
        catch (Exception e)
        {
            exception = e;
            result = null;
            return false;
        }
    }
    
    public static MatchResult Match(string route, ClaimsPrincipal? principal = null)
    {
        return Match(route.Split("/", StringSplitOptions.RemoveEmptyEntries), principal);
    }
    
    public static MatchResult Match(string[] segments, ClaimsPrincipal? principal = null)
    {
        // 1. /home/index
        // 2. /controller/index
        if (segments.Length is 0)
        {
            switch (IndexRoutes.Count)
            {
                case 1:
                {
                    return MatchResult.Match(IndexRoutes[0]);
                }
                case > 1:
                {
                    return MatchResult.Match(IndexHomeRoute ?? IndexRoutes[0]);
                }
            }
        }

        // 2. convention + custom routes
        MatchResult custom = Router.Match(segments);
    
        if (custom.MatchedRoute is null && segments.Length == 1)
        {
            if (Controllers.Contains(segments[0].ToLowerInvariant()))
            {
                string[] segmentsWithIndex = [segments[0], "index"];
                custom = Router.Match(segmentsWithIndex);
            }
        }

        // 4. 404
        return custom.MatchedRoute is not null ? custom : MatchResult.NoMatch();
    }
}