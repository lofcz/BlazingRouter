using System.Reflection;
using System.Security.Claims;
using BlazingCore;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Caching.Memory;

namespace RadixRouter;

public class MatchResult(bool isMatch, Route? matchedRoute, Dictionary<string, string>? pars = null)
{
    public bool IsMatch { get; set; } = isMatch;
    public Route? MatchedRoute { get; set; } = matchedRoute;
    public Dictionary<string, string>? Params { get; set; } = pars;

    public static MatchResult Match(Route? matchedRoute)
    {
        return new MatchResult(true, matchedRoute);
    }

    public static MatchResult NoMatch()
    {
        return new MatchResult(false, null);
    }
}

public class Route
{
    public Route()
    {
    }

    public Route(string fullRoute)
    {
        UriSegments = fullRoute.Split('/', StringSplitOptions.RemoveEmptyEntries).ToArray();
    }

    public Route(string[]? uriSegments, Type handler)
    {
        UriSegments = uriSegments;
        Handler = handler;
    }

    public string[]? UriSegments { get; set; }
    public Type Handler { get; set; }
    public string TypeFullnameLower { get; set; }
    public bool EndsWithIndex { get; set; }
    public bool OnlyUnauthorized { get; set; }
    public bool RedirectUnauthorized { get; set; }
    public string? RedirectUnauthorizedUrl { get; set; }
    public List<IRole>? AuthorizedRoles { get; set; }

    public MatchResult Match(string[] segments)
    {
        if (UriSegments != null && segments.Length != UriSegments.Length)
        {
            return MatchResult.NoMatch();
        }

        return UriSegments != null && UriSegments.Where((t, i) => string.Compare(segments[i], t, StringComparison.OrdinalIgnoreCase) != 0).Any() ? MatchResult.NoMatch() : MatchResult.Match(this);
    }
}

public class RouteManager
{
    private static List<Type> PageComponentTypes;
    private static readonly List<Route> Routes = [];
    private static readonly List<Route> IndexRoutes = [];
    internal static Route? IndexHomeRoute;
    private static BlazingRouter Router;
    private static readonly HashSet<string> Controllers = [];
    private static readonly Dictionary<string, bool> UsedExpandedRoutes = [];

    public RouteManager(IMemoryCache cache)
    {
    }

    public static void InitRouteManager(Assembly assembly)
    {
        PageComponentTypes = assembly.ExportedTypes.Where(t => t.Namespace != null && (t.IsSubclassOf(typeof(ComponentBase)) || t.IsSubclassOf(typeof(ComponentBaseInternal))) && t.Namespace.Contains(".Pages")).ToList();
        
        foreach (Type t in PageComponentTypes)
        {
            string[]? segments = t.FullName?[(t.FullName.IndexOf("Pages", StringComparison.OrdinalIgnoreCase) + 6)..]?.Split('.');
            UsedExpandedRoutes.TryAdd(segments.ToCsv("/") ?? string.Empty, true);

            if (segments?.Length > 0)
            {
                Controllers.Add(segments[0]);
            }

            Routes.Add(new Route(segments, t));
            List<RouteAttribute> routes = t.GetCustomAttributes<RouteAttribute>().ToList();

            if (routes.Count > 0)
            {
                foreach (RouteAttribute route in routes)
                {
                    string[] routeSegments = route.Template.Split('/', StringSplitOptions.RemoveEmptyEntries).ToArray();
                    UsedExpandedRoutes.TryAdd(routeSegments.ToCsv("/") ?? string.Empty, true);
                    Routes.Add(new Route(routeSegments, t));
                }
            }

            /*List<LocalizedRouteAttribute> localizedAttributes = t.GetCustomAttributes<LocalizedRouteAttribute>().ToList();

            if (localizedAttributes.Count > 0)
            {
                foreach (LocalizedRouteAttribute routeAttr in localizedAttributes)
                {
                    if (routeAttr.Key.IsNullOrWhiteSpace())
                    {
                        continue;
                    }

                    foreach (Reo.KnownLangs lang in Enum.GetValues<Reo.KnownLangs>())
                    {
                        if (lang is Reo.KnownLangs.Unknown)
                        {
                            continue;
                        }

                        string localizedRoute = Reo.GetString(routeAttr.Key, lang);

                        if (!localizedRoute.IsNullOrWhiteSpace())
                        {
                            string[] routeSegments = localizedRoute.Split('/', StringSplitOptions.RemoveEmptyEntries).ToArray();
                            string joined = routeSegments.ToCsv("/") ?? string.Empty;

                            if (!UsedExpandedRoutes.ContainsKey(joined))
                            {
                                Routes.Add(new Route(routeSegments, t));
                            }
                        }
                    }
                }
            }*/
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

            AuthorizeExt? authAttr = (AuthorizeExt?)Attribute.GetCustomAttribute(route.Handler, typeof(AuthorizeExt));

            if (authAttr is not null && !anyone)
            {
                route.AuthorizedRoles = [];
                route.AuthorizedRoles.AddRange(authAttr.TypedRoles);
            }

            route.TypeFullnameLower = route.Handler.FullName?.ToLowerInvariant() ?? "";
            route.EndsWithIndex = route.TypeFullnameLower.EndsWith("index");

            if (route.EndsWithIndex)
            {
                IndexRoutes.Add(route);

                if (route.UriSegments is { Length: > 0 } && route.UriSegments[0].ToLowerInvariant() is "home")
                {
                    IndexHomeRoute = route;
                }
            }

            if (anyone)
            {
                Attribute[] pageDirectives = Attribute.GetCustomAttributes(route.Handler, typeof(RouteAttribute));

                foreach (Attribute attr in pageDirectives)
                {
                    if (attr is RouteAttribute rattr)
                    {
                        string r = rattr.Template;

                        if (r.StartsWith('/'))
                        {
                            r = r[1..];
                        }

                        AddToUnauthorizedRoutes(r);
                    }
                }

                if (route.UriSegments != null)
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

    public static MatchResult Match(string[] segments, ClaimsPrincipal? principal)
    {
        // 1. /home/index
        // 2. /controller/index
        if (segments.Length == 0)
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

        // 3. 404
        return custom.MatchedRoute is not null ? custom : MatchResult.NoMatch();
    }
}