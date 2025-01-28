using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text;
using EnumsNET;
using Microsoft.Extensions.Caching.Memory;
using BlazingRouter.Shared;

namespace BlazingRouter;

internal static class Extensions
{
    public static bool IsInRoleAny(this ClaimsPrincipal p, IEnumerable<IRole> roles)
    {
        return roles.Any(x => RouteManager.Builder.HasAccess(p, x.Value));
    }
    
    public static void Forever(this IMemoryCache cache, string key, object? value)
    {
        cache.Set(key, value, DateTime.MaxValue);
    }
    
    public static string EncodeUri(this string? str)
    {
        return Uri.EscapeDataString(str ?? string.Empty);
    }
    
    public static bool IsNullOrWhiteSpace([NotNullWhen(returnValue: false)] this string? value)
    {
        return value is null || value.All(char.IsWhiteSpace);
    }
    
    public static string? ToCsv(this IEnumerable? elems, string separator = ",")
    {
        if (elems is null)
        {
            return null;
        }

        StringBuilder sb = new StringBuilder();
        foreach (object elem in elems)
        {
            if (sb.Length > 0)
            {
                sb.Append(separator);
            }

            if (elem is Enum)
            {
                sb.Append((int)elem);
            }
            else
            {
                sb.Append(elem);   
            }
        }

        return sb.ToString();
    }
    
    public static void AddOrUpdate<TKey, TVal>(this Dictionary<TKey, TVal> dict, TKey key, TVal val) where TKey : notnull
    {
        dict[key] = val;
    }
    
    public static object? ChangeType(this object? value, Type conversion) 
    {
        Type? t = conversion;

        if (t.IsEnum && value != null)
        {
            if (Enums.TryParse(t, value.ToString(), true, out object? x))
            {
                return x;
            }
        }
            
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>)) 
        {
            if (value == null) 
            { 
                return null; 
            }

            t = Nullable.GetUnderlyingType(t);
        }

        if (t == typeof(int) && value?.ToString() == "")
        {
            return 0;
        }
            
        if (t == typeof(int) && ((value?.ToString()?.Contains('.') ?? false) || (value?.ToString()?.Contains(',') ?? false)))
        {
            if (double.TryParse(value.ToString()?.Replace(",", "."), out double x))
            {
                return (int)x;
            }
        }

        if (value != null && t is {IsGenericType: true} && value.GetType().IsGenericType)
        {
            Type destT = t.GetGenericArguments()[0];
            Type sourceT = value.GetType().GetGenericArguments()[0];

            if (destT.IsEnum && sourceT == typeof(int))
            {
                IList? instance = (IList?)Activator.CreateInstance(t);

                foreach (object? x in (IList) value)
                {
                    instance?.Add(x);
                }

                return instance;
            }
        }

        return t != null ? Convert.ChangeType(value, t) : null;
    }
    
    private static readonly ConcurrentDictionary<Type, object> typeDefaults = new ConcurrentDictionary<Type, object>();
    
    public static object? GetDefaultValue(this Type type)
    {
        return type.IsValueType ? typeDefaults.GetOrAdd(type, Activator.CreateInstance!) : null;
    }
}