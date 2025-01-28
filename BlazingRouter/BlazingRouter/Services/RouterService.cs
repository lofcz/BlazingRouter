using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Caching.Memory;

namespace BlazingRouter.Services;

internal static class RouterService
{
    class RouteParam
    {
        public string Name { get; set; }
        public Type Type { get; set; }
    }

    public static IMemoryCache Cache;
    private static readonly Dictionary<string, object?> EmptyKvDict = new Dictionary<string, object?>();
    private static readonly Tuple<bool, Dictionary<string, object?>> EmptyParamMap = new Tuple<bool, Dictionary<string, object?>>(true, EmptyKvDict);

    private static List<RouteParam>? GetRouteMappedPars(Type? type)
    {
        if (type is null)
        {
            return null;
        }
        
        if (Cache.TryGetValue($"router_route_pars_{type.FullName}", out List<RouteParam>? cached))
        {
            return cached;
        }
        
        List<RouteAttribute> attrs = type.GetCustomAttributes<RouteAttribute>().ToList();
        List<RouteParam> mappedPars = [];

        if (attrs.Count > 0)
        {
            RouteAttribute first = attrs[0];
            IEnumerable<string> attrPars = first.Template.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(x => x.StartsWith('{') && x.EndsWith('}'));
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            
            foreach (string attrPar in attrPars)
            {
                string finalPar = attrPar[1..];
                finalPar = finalPar.Remove(finalPar.Length - 1);

                PropertyInfo? matchingProperty = properties.FirstOrDefault(x => string.Equals(x.Name, finalPar, StringComparison.InvariantCultureIgnoreCase));

                if (matchingProperty is null)
                {
                    throw new Exception("Nenalezena odpovídající vlastnost");
                }
                
                mappedPars.Add(new RouteParam {Name = finalPar, Type = matchingProperty.PropertyType});
            }
        }

        Cache.Set($"router_route_pars_{type.FullName}", mappedPars, DateTime.MaxValue);
        return mappedPars;
    }
    
    public static Tuple<bool, Dictionary<string, object?>> MapUrlParams(Type? type, Dictionary<string, string>? pars)
    {
        List<RouteParam>? map = GetRouteMappedPars(type);

        if (map == null)
        {
            return EmptyParamMap;
        }

        Dictionary<string, object?> mapped = [];

        for (int i = 0; i < map.Count; i++)
        {
            if (pars?.TryGetValue($"par_{i}", out string? val) ?? false)
            {
                if (typeof(string) == map[i].Type)
                {
                    mapped.Add(map[i].Name, val);   
                }
                else
                {
                    try
                    {
                        mapped.Add(map[i].Name, val.ChangeType(map[i].Type));
                    }
                    catch (Exception e)
                    {
                        return new Tuple<bool, Dictionary<string, object?>>(false, mapped);
                    }
                }
            }
        }

        return new Tuple<bool, Dictionary<string, object?>>(true, mapped);
    }

    public static Dictionary<string, object?>? FilterQueryParams(Type? type, Dictionary<string, object?>? pars)
    {
        if (type is null || pars is null || pars.Count is 0)
        {
            return pars;
        }
        
        Dictionary<string, RouteParam> properties;
        
        if (Cache.TryGetValue($"router_type_{type.FullName}", out object? info) && info is Dictionary<string, RouteParam> pi)
        {
            properties = pi;
        }
        else
        {
            properties = [];
            
            foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                string paramName = prop.Name.ToLowerInvariant();
                SupplyParameterFromQueryAttribute? queryParamAttr = prop.GetCustomAttribute<SupplyParameterFromQueryAttribute>();
                ParameterAttribute? paramAttr = prop.GetCustomAttribute<ParameterAttribute>();
                CascadingParameterAttribute? cascadingParamAttr = prop.GetCustomAttribute<CascadingParameterAttribute>();
                SupplyParameterFromFormAttribute? formParamAttr = prop.GetCustomAttribute<SupplyParameterFromFormAttribute>();
                
                // any cascading or cascading-derived params must be filtered
                if (queryParamAttr is not null || formParamAttr is not null || cascadingParamAttr is not null)
                {
                    continue;
                }
                
                if (paramAttr is not null)
                {
                    properties[paramName] = new RouteParam
                    {
                        Name = paramName,
                        Type = prop.PropertyType
                    };
                }
            }
                
            Cache.Forever($"router_type_{type.FullName}", properties);
        }
        
        List<KeyValuePair<string, object?>> itemsToRemove = pars.Where(x => !properties.ContainsKey(x.Key)).ToList();
        
        #if BLAZING_ROUTER_VERBOSE
        List<KeyValuePair<string, object?>>? ignored = null;
        #endif
        
        foreach (KeyValuePair<string, object?> item in itemsToRemove)
        {
            pars.Remove(item.Key);
            
            #if BLAZING_ROUTER_VERBOSE
            ignored ??= [];
            ignored.Add(item);
            #endif
        }
        
        #if BLAZING_ROUTER_VERBOSE
        if (ignored?.Count > 0)
        {
            Debug.WriteLine("--- Some arguments were ignored when resolving the route ---");

            foreach (KeyValuePair<string, object?> itm in ignored)
            {
                Debug.WriteLine($"{itm.Key} = {itm.Value}");
            }
        }
        #endif
        
        foreach (KeyValuePair<string, object?> par in pars)
        {
            if (!properties.TryGetValue(par.Key, out RouteParam? rp))
            {
                continue;
            }

            object? value;

            if (par.Value?.GetType() == rp.Type)
            {
                value = par.Value;
            }
            else
            {
                try
                {
                    value = par.Value.ChangeType(rp.Type);
                }
                catch (Exception)
                {
                    value = rp.Type.GetDefaultValue();
                }   
            }

            pars[par.Key] = value;
        }
        
        return pars;
    }
}