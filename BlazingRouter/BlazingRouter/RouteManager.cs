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
    internal static readonly List<Route> Routes = [];
    internal static IBaseBlazingRouterBuilder Builder;
    
    private static List<Type> PageComponentTypes;
    private static readonly List<Route> IndexRoutes = [];
    internal static Route? IndexHomeRoute;
    private static BlazingRouter Router;
    private static readonly HashSet<string> Controllers = [];
    private static readonly Dictionary<string, bool> UsedExpandedRoutes = [];
    
    public RouteManager(IMemoryCache cache)
    {
      
    }

    public static void AddRoute(Route route)
    {
        Router.Tree.AddRoute(route);
    }
    
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

        PageComponentTypes = assembly.ExportedTypes.Where(t => t.Namespace is not null && (t.IsSubclassOf(typeof(ComponentBase)) || t.IsSubclassOf(typeof(ComponentBaseInternal))) && t.Namespace.Contains(".Pages")).ToList();
        
        foreach (Type t in PageComponentTypes)
        {
            string[]? segments = t.FullName?[(t.FullName.IndexOf("Pages", StringComparison.OrdinalIgnoreCase) + 6)..]?.Split('.');
            UsedExpandedRoutes.TryAdd(segments.ToCsv("/") ?? string.Empty, true);

            if (segments?.Length > 0)
            {
                AddController(segments[0]);
            }

            Routes.Add(new Route(segments, t));
            List<RouteAttribute> routes = t.GetCustomAttributes<RouteAttribute>().ToList();

            if (routes.Count > 0)
            {
                foreach (RouteAttribute route in routes)
                {
                    string[] routeSegments = route.Template.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    UsedExpandedRoutes.TryAdd(routeSegments.ToCsv("/") ?? string.Empty, true);
                    Routes.Add(new Route(routeSegments, t));
                }
            }
            
            List<Route> addedRoutes = builder.OnPageScanned?.Invoke(t) ?? [];
            Routes.AddRange(addedRoutes);
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
                AddToUnauthorizedRoutes(route.UriSegments.ToCsv("/") ?? string.Empty);
            }

            if (redirectUnauthorized && redirectUnauthorizedAttr is not null)
            {
                route.RedirectUnauthorized = true;
                route.RedirectUnauthorizedUrl = ((RedirectUnauthorized)redirectUnauthorizedAttr).Redirect;
            }
            
            AuthorizeExtAttributeBase? authAttr = (AuthorizeExtAttributeBase?)Attribute.GetCustomAttributes(route.Handler, typeof(AuthorizeExtAttributeBase), inherit: true).FirstOrDefault();

            if (authAttr is not null && !anyone)
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