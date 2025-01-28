using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Caching.Memory;
using BlazingRouter.Services;

namespace BlazingRouter;

public class RouterExt : IComponent, IHandleAfterRender, IDisposable
{
    RenderHandle _renderHandle;
    bool _navigationInterceptionEnabled;
    string? _location;
    
    [Inject] 
    private IMemoryCache Cache { get; set; }
    [Inject] 
    private NavigationManager NavigationManager { get; set; }
    [Inject] 
    private INavigationInterception NavigationInterception { get; set; }
    [Inject] 
    private RouteManager RouteManager { get; set; }
    [Parameter] 
    public RenderFragment NotFound { get; set; }
    [Parameter] 
    public RenderFragment<RouteData> Found { get; set; }
    [Parameter]
    [EditorRequired]
    public Assembly AppAssembly { get; set; }
    [CascadingParameter]
    private Task<AuthenticationState>? authenticationStateTask { get; set; }
    public ClaimsPrincipal? User { get; set; }
    [Inject]
    private AuthenticationStateProvider Asp { get; set; }
    private AuthenticationState? authState { get; set; }
    
    internal RouteManager Manager { get; set; }
    
    public static readonly HashSet<string> AllowedUnauthorizedUrls = [
        string.Empty, "/", "home", "home/index", "account/login", "account/register", "account/school",
        "account/externallogincallback", "/home/changelog", "account/forgotpassword",
        "account/logininternalgooglecallback", "signin-google", "account/registerparent",
        "account/externallogincallback", "account/externalloginregister", "account/resetpassword",
        "account/verifyemail", "sck", "llm/chat", "llm/unauthorized", "privacy-policy", "account/register-flow", "preparations",
        "webinar", "home/preparations", "home/webinar", "home/privacy-policy", "cookies", "home/cookies", "priprava/{id}", "o-aplikaci", "o-aplikaci-sciobot",
        "about", "vop", "ukazky", "ukazka", "ukazky-priprav", "demo/i18n", "tos", "terms-of-service", "podminky", "podminky-sluzby", "podminky-uzivani"
    ];

    private static BlazingRouter AllowedUnauthorizedRouter = null!;
    private static readonly Dictionary<string, object?> emptyQueryParamsDict = [];
    
    internal static void SetupUnauthorizedRouterExt()
    {
        List<Route> routes = AllowedUnauthorizedUrls.Select(x => new Route(x)).ToList();
        AllowedUnauthorizedRouter = new BlazingRouter(routes);
    }
    
    public void Attach(RenderHandle renderHandle)
    {
        RouterService.Cache ??= Cache!;
        
        _renderHandle = renderHandle;
        _location = NavigationManager.Uri;
        NavigationManager.LocationChanged += HandleLocationChanged;
    }
    
    private string UnauthorizedRedirectUrl(string? relativeUrl)
    {
        return relativeUrl.IsNullOrWhiteSpace() ? $"/account/login" : $"/account/login?r={relativeUrl.EncodeUri()}";
    }

    public async Task GetAuthState()
    {
        if (authenticationStateTask != null)
        {
            User = (await authenticationStateTask).User;
        }
    }

    public async Task SetParametersAsync(ParameterView parameters)
    {
        authState ??= await Asp.GetAuthenticationStateAsync();
        parameters.SetParameterProperties(this);

        if (Found is null)
        {
            throw new InvalidOperationException($"The {nameof(RouterExt)} component requires a value for the parameter {nameof(Found)}.");
        }

        if (NotFound is null)
        {
            throw new InvalidOperationException($"The {nameof(RouterExt)} component requires a value for the parameter {nameof(NotFound)}.");
        }
        
        Refresh();
    }

    public async Task OnAfterRenderAsync()
    {
        if (!_navigationInterceptionEnabled)
        {
            _navigationInterceptionEnabled = true;
            await NavigationInterception.EnableNavigationInterceptionAsync();
        }
    }

    public void Dispose()
    {
        NavigationManager.LocationChanged -= HandleLocationChanged;
        GC.SuppressFinalize(this);
    }

    private void HandleLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        _location = args.Location;
        Refresh();
    }

    private void Refresh()
    {
        string relativeUri = NavigationManager.ToBaseRelativePath(_location ?? string.Empty);
        string originalPath = relativeUri;
        
        // 1. fragment
        string[] fragmentParts = relativeUri.Split('#', 2);
        relativeUri = fragmentParts[0];
        // string? fragment = fragmentParts.Length is 2 ? fragmentParts[1] : null;
        
        // 2. query
        ParsedUrlQuery? parsedUrlQuery = ParseQueryString(relativeUri);
        Dictionary<string, object?>? parameters = parsedUrlQuery?.Pars;

        if (parsedUrlQuery is not null)
        {
            relativeUri = relativeUri[..parsedUrlQuery.QueryStartCharIndex];
        }

        // 3. path
        string[] pathSegments = relativeUri.Trim().ToLowerInvariant().Split('/', StringSplitOptions.RemoveEmptyEntries);
        MatchResult matchResult = RouteManager.Match(pathSegments, authState?.User);

        if (matchResult.IsMatch)
        {
            if ((matchResult.MatchedRoute?.OnlyUnauthorized ?? false) && (authState?.User.Identity?.IsAuthenticated ?? false) && RouteManager.IndexHomeRoute is not null)
            {
                NavigationManager.NavigateTo(matchResult.MatchedRoute?.RedirectUnauthorizedUrl ?? "/", true);
                return;
            }

            if ((matchResult.MatchedRoute?.RedirectUnauthorized ?? false) && !(authState?.User.Identity?.IsAuthenticated ?? false))
            {
                NavigationManager.NavigateTo(matchResult.MatchedRoute?.RedirectUnauthorizedUrl ?? UnauthorizedRedirectUrl(originalPath), true);
                return;
            }
            
            if (pathSegments.Length > 0 && !(authState?.User.Identity?.IsAuthenticated ?? false))
            {
                MatchResult unauthorizedMatch = AllowedUnauthorizedRouter.Match(pathSegments);

                if (unauthorizedMatch.MatchedRoute is null)
                {
                    NavigationManager.NavigateTo(UnauthorizedRedirectUrl(originalPath), true);
                    return;
                }
            }

            if (matchResult.MatchedRoute?.AuthorizedRoles is { Count: > 0 })
            {
                if (!authState?.User.IsInRoleAny(matchResult.MatchedRoute.AuthorizedRoles) ?? true)
                {
                    NavigationManager.NavigateTo("/", true);
                    return;
                }   
            }

            if (matchResult.Params is not null)
            {
                parameters ??= [];

                Tuple<bool, Dictionary<string, object?>> casted = RouterService.MapUrlParams(matchResult.MatchedRoute?.Handler, matchResult.Params);

                if (!casted.Item1) // invalid route param value, cast failed
                {
                    NavigationManager.NavigateTo("/", true);
                    return;
                }
                
                foreach ((string? key, object? value) in casted.Item2)
                {
                    parameters.AddOrUpdate(key, value);   
                }
            }

            if (matchResult.MatchedRoute is null)
            {
                _renderHandle.Render(NotFound);
                return;
            }
            
            parameters = RouterService.FilterQueryParams(matchResult.MatchedRoute?.Handler, parameters);

            if (matchResult.MatchedRoute?.Handler is not null)
            {
                RouteData routeData = new RouteData(matchResult.MatchedRoute.Handler, parameters ?? emptyQueryParamsDict);
                _renderHandle.Render(Found(routeData));   
            }
            else
            {
                _renderHandle.Render(NotFound);
            }
        }
        else
        {
            _renderHandle.Render(NotFound);
        }
    }

    /// <summary>
    /// Unlike some engines, we deliberately onl
    /// </summary>
    /// <param name="uri"></param>
    /// <returns></returns>
    private static ParsedUrlQuery? ParseQueryString(string uri)
    {
        int paramsIndex = uri.IndexOf('?', StringComparison.Ordinal);
        
        if (paramsIndex is -1)
        {
            return null;
        }

        ParsedUrlQuery toRet = new ParsedUrlQuery
        {
            QueryStartCharIndex = paramsIndex
        };
        
        foreach (string kvp in uri[(paramsIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (kvp.Contains('='))
            {
                string[] pair = kvp.Split('=');
                toRet.Pars[pair[0].ToLowerInvariant()] = pair[1];
            }
            else
            {
                toRet.Pars[kvp.ToLowerInvariant()] = string.Empty;
            }
        }

        return toRet;
    }
}