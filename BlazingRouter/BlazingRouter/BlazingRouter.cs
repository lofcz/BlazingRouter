using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using BlazingRouter.Shared;

namespace BlazingRouter;

internal class PatternParser
{
    private string pattern;
    private int position;

    public void Prepare(string pattern)
    {
        this.pattern = pattern;
        position = 0;
    }
    
    public PatternParser(string pattern)
    {
        Prepare(pattern);
    }

    private char? Peek(int offset = 0)
    {
        int pos = position + offset;
        return pos < pattern.Length ? pattern[pos] : null;
    }

    private void Consume()
    {
        if (position < pattern.Length)
            position++;
    }

    public List<string> Parse()
    {
        List<string> segments = [];
        
        // Skip leading slash
        if (position < pattern.Length && pattern[position] == '/')
        {
            Consume();
        }

        while (position < pattern.Length)
        {
            string segment = ParseSegment();
            if (!string.IsNullOrEmpty(segment))
            {
                segments.Add(segment);
            }
            
            // Skip slash between segments
            if (position < pattern.Length && pattern[position] == '/')
            {
                Consume();
            }
        }

        return segments;
    }

    private string ParseSegment()
    {
        int start = position;
        bool inParameter = false;
        int bracketDepth = 0;

        while (position < pattern.Length)
        {
            char current = pattern[position];

            if (current is '/' && !inParameter)
            {
                break;
            }

            if (current is '{')
            {
                if (Peek(1) is '{')
                {
                    // Skip escaped brace
                    Consume();
                    Consume();
                    continue;
                }
                inParameter = true;
                bracketDepth++;
            }
            else if (current is '}')
            {
                if (Peek(1) is '}')
                {
                    // Skip escaped brace
                    Consume();
                    Consume();
                    continue;
                }
                
                bracketDepth--;
                if (bracketDepth is 0)
                {
                    inParameter = false;
                }
            }

            Consume();
        }

        return pattern[start..position];
    }
}


/// <summary>
/// A route. Routes use the following syntax:<br/>
/// <i>1.</i> Routes consist of segments, which are divided by <c>/</c> (forward slash)<br/>
/// <i>2.</i> a route consists of zero or more segments, for example <c>/test/ping</c> uses two segments<br/>
/// <i>3.</i> a segment is defined as:<br/>
/// <i>3a)</i> alphanumeric literal, for example <c>test</c><br/>
/// <i>3b)</i> alphanumeric literal enclosed in curly brackets <c>{test}</c> (query argument), for example <c>/product/{name}</c><br/>
/// <i>3c)</i> star symbol <c>*</c> (wildcard), captures anything. <c>/blog/{id}/*</c> makes <c>/blog/2/my-book</c> a valid route. If wildcard is used, no further segments can be used for the route.<br/>
/// <i>3d)</i> alphanumeric literal enclosed in curly brackets, prepended by <c>**</c> (two stars), named catch-all. <c>/post/{**arg}</c>, acts in the same manner as <i>3c</i>, but stores the result into a named argument. A route can't include both named and unnamed catch-all.<br/>
/// <i>4.</i> a segment defined as query argument (3b) may use one or more constrains, delimited by <c>:</c> (colon)<br/>
/// <i>4a)</i> type constrain: <c>{arg:int|bool|datetime|decimal|double|float|guid|long}</c><br/>
/// <i>4b)</i> length constraint: <c>{arg:minlength(val)|maxlength(val)|length(val)|length(min,max))</c><br/>
/// <i>4c)</i> numeric value constraint: <c>{arg:min(val)|max(val)|range(min,max)}</c><br/>
/// <i>4d)</i> string content constraint: <c>{arg:alpha}</c> (one or more alphabetical characters, a-z, case-insensitive)<br/>
/// <i>4e)</i> regex constraint: <c>{arg:regex(str)}</c> (for example <c>{arg:regex(:(.*))}</c> constrains <c>arg</c> to regex <c>:(.*)</c>)<br/>
/// <i>4f)</i> required constraint: <c>{arg:required}</c> (enforce that the argument can't be empty)
/// <i>5.</i> a segment defined as query argument (3b) may be flagged as optional by <c>?</c> (question mark). Multiple segments can be marked as optional, but all optional segments must come after mandatory segments<br/>
/// </summary>
public class Route
{
    public int Priority { get; }
    public List<string>? UriSegments { get; set; }
    public List<RouteSegment> Segments { get; set; } = [];
    public Type Handler { get; set; }
    public string TypeFullnameLower { get; set; }
    public bool EndsWithIndex { get; set; }
    public bool OnlyUnauthorized { get; set; }
    public bool RedirectUnauthorized { get; set; }
    public string? RedirectUnauthorizedUrl { get; set; }
    public List<IRole>? AuthorizedRoles { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    private Route()
    {
    }
    
    /// <summary>
    /// Creates a route from a route, e.g. /test/ping. Segments should be delimited by "/"
    /// </summary>
    /// <param name="pattern">See <see cref="Route"/> for syntax</param>
    /// <param name="handler">Type (of a page) associated with this route</param>
    /// <param name="priority">Optional priority, use numbers > 0 for higher priority</param>
    public Route(string pattern, Type handler, int priority = 0)
    {
        UriSegments = SplitPattern(pattern);
        Handler = handler;
        Priority = priority;
        ParseSegments();
    }
    
    /// <summary>
    /// Creates a route from a route, e.g. /test/ping. Segments should be delimited by "/"
    /// </summary>
    /// <param name="pattern">See <see cref="Route"/> for syntax</param>
    /// <param name="handler">Type (of a page) associated with this route</param>
    /// <param name="authorizedRoles">User must be at least in one of the roles listed to access the route</param>
    /// <param name="priority">Optional priority, use numbers > 0 for higher priority</param>
    public Route(string pattern, Type handler, List<IRole> authorizedRoles, int priority = 0)
    {
        UriSegments = SplitPattern(pattern);
        Handler = handler;
        Priority = priority;
        AuthorizedRoles = authorizedRoles;
        ParseSegments();
    }
    
    internal Route(string pattern)
    {
        UriSegments = SplitPattern(pattern);
        ParseSegments();
    }
    
    /// <summary>
    /// Creates a route from an array of segments.
    /// </summary>
    /// <param name="uriSegments"></param>
    /// <param name="handler"></param>
    internal Route(string[] uriSegments, Type handler)
    {
        UriSegments = uriSegments.ToList();
        Handler = handler;
        ParseSegments();
    }
    
    /// <summary>
    /// Creates a route from an enumerable strings of segments.
    /// </summary>
    /// <param name="uriSegments"></param>
    /// <param name="handler"></param>
    internal Route(IEnumerable<string> uriSegments, Type handler)
    {
        UriSegments = uriSegments.ToList();
        Handler = handler;
        ParseSegments();
    }
    
    private static List<string> SplitPattern(string pattern)
    {
        PatternParser parser = new PatternParser(pattern);
        return parser.Parse();
    }
    
    private void ParseSegments()
    {
        if (UriSegments is null)
        {
            return;
        }

        RouteSegmentTypes maxSegment = RouteSegmentTypes.Static;
        bool hasOptional = false;
        bool hasWildcard = false;
        bool hasCatchAll = false;

        for (int i = 0; i < UriSegments.Count; i++)
        {
            string routeSegment = UriSegments[i];
            RouteSegment parsedSegment = new RouteSegment();
            ReadOnlySpan<char> segmentSpan = routeSegment.AsSpan().Trim();
            RouteSegmentTypes localSegment = RouteSegmentTypes.Static;

            if (segmentSpan.Length >= 2 && segmentSpan[0] == '{' && segmentSpan[^1] == '}')
            {
                ReadOnlySpan<char> innerSpan = segmentSpan.Slice(1, segmentSpan.Length - 2);
                List<string> parts = [];
                int partStart = 0;

                for (int j = 0; j <= innerSpan.Length; j++)
                {
                    if (j == innerSpan.Length || innerSpan[j] == ':')
                    {
                        if (partStart < j)
                        {
                            ReadOnlySpan<char> part = innerSpan.Slice(partStart, j - partStart).Trim();
                            if (!part.IsEmpty)
                            {
                                parts.Add(part.ToString());
                            }
                        }
                        partStart = j + 1;
                    }
                }

                if (parts.Count == 0)
                    continue;

                string paramNameWithDefault = parts[0];
                List<string> constraints = parts.Skip(1).ToList();
                bool isOptional = false;
                string? defaultValue = null;

                // Split parameter name and default value
                int equalsIndex = paramNameWithDefault.IndexOf('=');
                string paramName;
                if (equalsIndex >= 0)
                {
                    paramName = paramNameWithDefault[..equalsIndex];
                    defaultValue = paramNameWithDefault[(equalsIndex + 1)..];
        
                    if (paramName.EndsWith('?') || defaultValue.EndsWith('?'))
                    {
                        throw new ArgumentException("Segment cannot be both optional and have a default value.");
                    }
                }
                else
                {
                    paramName = paramNameWithDefault;
                }

                // Check for catch-all parameter (starts with **)
                if (paramName.StartsWith("**", StringComparison.Ordinal))
                {
                    if (paramName.Length is 2)
                    {
                        throw new ArgumentException("Catch-all parameter must have a name.");
                    }
                    
                    paramName = paramName[2..];
                    localSegment = RouteSegmentTypes.CatchAll;

                    // Catch-all parameters cannot be optional
                    if (paramName.EndsWith('?'))
                    {
                        throw new ArgumentException("Catch-all parameters cannot be optional.");
                    }
                
                    paramName = paramName.TrimEnd('?');
                }
                else
                {
                    // Existing optional and default checks
                    if (paramName.EndsWith('?'))
                    {
                        if (defaultValue != null)
                        {
                            throw new ArgumentException("Segment cannot be both optional and have a default value.");
                        }
                        
                        paramName = paramName[..^1];
                        isOptional = true;
                    }
                    
                    if (constraints.Count > 0)
                    {
                        string lastConstraint = constraints[^1];
                        if (lastConstraint.EndsWith('?'))
                        {
                            if (defaultValue != null)
                            {
                                throw new ArgumentException("Segment cannot be both optional and have a default value.");
                            }
                            constraints[^1] = lastConstraint[..^1];
                            isOptional = true;
                        }
                    }

                    localSegment = RouteSegmentTypes.Dynamic;
                }

                parsedSegment.DefaultValue = defaultValue;
                parsedSegment.LiteralValue = paramName.ToLowerInvariant();
                parsedSegment.Constraints = constraints.Count > 0 ? constraints : null;
                parsedSegment.IsOptional = isOptional;
                parsedSegment.RawSegment = localSegment == RouteSegmentTypes.CatchAll ? "**" + paramName : "__dynamic";
            }
            else if (segmentSpan.Length == 1 && segmentSpan[0] == '*')
            {
                localSegment = RouteSegmentTypes.Wildcard;
                parsedSegment.RawSegment = "*";
            }
            else
            {
                parsedSegment.RawSegment = segmentSpan.ToString().ToLowerInvariant();
            }
            
            switch (localSegment)
            {
                case RouteSegmentTypes.CatchAll when hasCatchAll || hasWildcard:
                {
                    throw new ArgumentException("Cannot have multiple catch-all or wildcard segments.");
                }
                case RouteSegmentTypes.CatchAll:
                {
                    hasCatchAll = true;

                    if (i != UriSegments.Count - 1)
                    {
                        throw new ArgumentException("Catch-all must be the last segment.");
                    }
                    
                    break;
                }
                case RouteSegmentTypes.Wildcard when hasWildcard || hasCatchAll:
                {
                    throw new ArgumentException("Cannot have multiple wildcard or catch-all segments.");
                }
                case RouteSegmentTypes.Wildcard:
                {
                    hasWildcard = true;

                    if (i != UriSegments.Count - 1)
                    {
                        throw new ArgumentException("Wildcard must be the last segment.");
                    }
                    
                    break;
                }
            }
            
            if (maxSegment is RouteSegmentTypes.Wildcard or RouteSegmentTypes.CatchAll)
            {
                break;
            }

            if (hasOptional && !parsedSegment.IsOptional && localSegment != RouteSegmentTypes.Wildcard && localSegment != RouteSegmentTypes.CatchAll)
            {
                throw new ArgumentException("Optional parameters must come after all required parameters.");
            }

            if (parsedSegment.IsOptional)
            {
                hasOptional = true;
            }

            parsedSegment.Type = localSegment;
            Segments.Add(parsedSegment);
            maxSegment = localSegment;

            // After adding a wildcard or catch-all, break the loop
            if (localSegment is RouteSegmentTypes.Wildcard or RouteSegmentTypes.CatchAll)
            {
                break;
            }
        }

        if (hasWildcard && hasCatchAll)
        {
            throw new ArgumentException("Route cannot contain both a single star and a double star segment.");
        }
    }
}

public class BlazingRouter
{
    public RadixTree Tree { get; set; }

    public BlazingRouter(List<Route> routes)
    {
        Tree = new RadixTree(routes);
    }

    public MatchResult Match(string[] segments)
    {
        ParametrizedRouteResult x = Tree.ResolveRoute(segments);
        return new MatchResult(x.Success && (x.NativeResult?.IsExactMatch ?? false), x.NativeResult?.Node?.Handler, x.NativeResult?.Params, x.NativeResult?.LastMatchedNode?.Handler);
    }
    
}

public class RouteConstraint
{
    public string Type { get; }
    public string? Value { get; }
    public string? Value2 { get; }

    public RouteConstraint(string type, string? value = null, string? value2 = null)
    {
        Type = type.ToLowerInvariant();
        Value = value;
        Value2 = value2;
    }
}

public static partial class RouteConstraintParser
{
    private static readonly Regex ConstraintRegex = GeneratedConstraintRegex();
    private static readonly ConcurrentDictionary<string, RouteConstraint> ConstraintCache = [];
    
    public static RouteConstraint Parse(string constraintStr)
    {
        return ConstraintCache.GetOrAdd(constraintStr, key =>
        {
            Match match = ConstraintRegex.Match(key);

            if (!match.Success)
            {
                throw new ArgumentException($"Invalid constraint format: {key}");
            }

            string type = match.Groups["type"].Value.ToLowerInvariant();
            string paramsPart = match.Groups["params"].Success ? match.Groups["params"].Value : "";

            // Handle regex constraints with escaped characters
            if (type is "regex")
            {
                // Ensure the entire parameter is captured, including nested parentheses
                int openParen = key.IndexOf('(');
                if (openParen == -1)
                    return new RouteConstraint(type);

                int closeParen = key.LastIndexOf(')');
                if (closeParen == -1)
                    return new RouteConstraint(type, key[(openParen + 1)..]);

                string regexPattern = key.Substring(openParen + 1, closeParen - openParen - 1);
                return new RouteConstraint(type, regexPattern);
            }

            // Existing logic for other constraints
            List<string> parameters = SplitParams(paramsPart);
            return new RouteConstraint(type, parameters.FirstOrDefault(), parameters.Skip(1).FirstOrDefault());
        });
    }
    
    private static List<string> SplitParams(string paramsPart)
    {
        List<string> parameters = [];
        int start = 0;
        bool inParentheses = false;

        for (int i = 0; i < paramsPart.Length; i++)
        {
            inParentheses = paramsPart[i] switch
            {
                '(' => true,
                ')' => false,
                _ => inParentheses
            };

            if (paramsPart[i] == ',' && !inParentheses)
            {
                parameters.Add(paramsPart.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }
        parameters.Add(paramsPart[start..].Trim());
        return parameters;
    }

    [GeneratedRegex(@"^(?<type>\w+)(?:\((?<params>.*)\))?$", RegexOptions.Compiled)]
    private static partial Regex GeneratedConstraintRegex();
}

public static class RouteConstraintValidator
{
    public static readonly Dictionary<string, Func<string, RouteConstraint, bool>> Validators = new Dictionary<string, Func<string, RouteConstraint, bool>>(StringComparer.OrdinalIgnoreCase)
    {
        ["int"] = (value, _) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int _),
        ["bool"] = (value, _) => bool.TryParse(value, out bool _),
        ["datetime"] = (value, _) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime _),
        ["decimal"] = (value, _) => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal _),
        ["double"] = (value, _) => double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double _),
        ["float"] = (value, _) => float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out float _),
        ["guid"] = (value, _) => Guid.TryParse(value, out Guid _),
        ["long"] = (value, _) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long _),
        ["minlength"] = (value, constraint) =>
        {
            if (!int.TryParse(constraint.Value, out int minLength))
                return false;
            return value.Length >= minLength;
        },
        ["maxlength"] = (value, constraint) => 
        {
            if (!int.TryParse(constraint.Value, out int maxLength))
                return false;
            return value.Length <= maxLength;
        },
        ["length"] = (value, constraint) => 
        {
            if (constraint.Value2 == null)
            {
                if (!int.TryParse(constraint.Value, out int exactLength))
                    return false;
                return value.Length == exactLength;
            }
            
            if (!int.TryParse(constraint.Value, out int minLength) || 
                !int.TryParse(constraint.Value2, out int maxLength))
                return false;
                
            return value.Length >= minLength && value.Length <= maxLength;
        },
        ["min"] = (value, constraint) => 
        {
            if (!int.TryParse(value, out int numValue) || 
                !int.TryParse(constraint.Value, out int minValue))
                return false;
            return numValue >= minValue;
        },
        ["max"] = (value, constraint) => 
        {
            if (!int.TryParse(value, out int numValue) || 
                !int.TryParse(constraint.Value, out int maxValue))
                return false;
            return numValue <= maxValue;
        },
        ["range"] = (value, constraint) => 
        {
            if (!int.TryParse(value, out int numValue) || 
                !int.TryParse(constraint.Value, out int minValue) ||
                !int.TryParse(constraint.Value2, out int maxValue))
                return false;
            return numValue >= minValue && numValue <= maxValue;
        },
        ["alpha"] = (value, _) => !string.IsNullOrEmpty(value) && value.All(char.IsLetter),
        ["regex"] = (value, constraint) => 
        {
            if (constraint.Value is null)
            {
                return false;
            }

            
            try
            {
                return Regex.IsMatch(value, constraint.Value);
            }
            catch (ArgumentException)
            {
                return false;
            }
        },
        ["required"] = (value, _) => !string.IsNullOrEmpty(value)
    };
}

public enum RouteSegmentTypes
{
    Root,
    Method,
    Static,
    Dynamic,
    Wildcard,
    CatchAll
}

public class RouteSegment
{
    public string LiteralValue { get; set; }
    public string RawSegment { get; set; }
    public RouteSegmentTypes Type { get; set; }
    public List<string>? Constraints { get; set; }
    public bool IsOptional { get; set; }
    public string? DefaultValue { get; set; }
}


public class RadixTreeNode
{
    public Dictionary<string, RadixTreeNode?>? Childs { get; set; }
    public Dictionary<string, List<RadixTreeNode>>? DynamicNodes { get; set; }
    public Route? Handler { get; set; }
    public string Text { get; set; }
    public string? ParamName { get; set; }
    public RouteSegmentTypes Type { get; set; }
    public List<RouteConstraint> Constraints { get; set; } = [];
    public string? DefaultValue { get; set; } 

    public RadixTreeNode(string text, RouteSegmentTypes type, Route? handler)
    {
        Handler = handler;
        Text = text;
        Type = type;
    }

    public void AddConstraint(string constraintStr)
    {
        RouteConstraint constraint = RouteConstraintParser.Parse(constraintStr);
        Constraints.Add(constraint);
    }
}


public class ResolvedRouteResult
{
    public RadixTreeNode? Node { get; set; }
    public Dictionary<string, string> Params { get; set; } = [];
    public bool IsExactMatch { get; set; }
    public RadixTreeNode? LastMatchedNode { get; set; }
}

public class ParseContext
{
    public List<string> Segments { get; set; } = [];
    public int CurrentIndex { get; set; }
    public Dictionary<string, string> Params { get; set; } = [];
    public RadixTreeNode? BestMatch { get; set; }
    public int BestMatchIndex { get; set; }
}

public class ParametrizedRouteResult
{
    public ResolvedRouteResult? NativeResult { get; set; }
    public string? Route { get; set; }
    public Dictionary<string, string>? Params { get; set; }
    public bool Success { get; set; }
}

public class RouteContainer
{
    public List<RouteSegment> Segments { get; set; } = [];
    public Route Route { get; set; }

    public RouteContainer(Route route)
    {
        Route = route;
    }
}

public class RadixTree
{
    private List<Route> Routes { get; set; } = [];
    private List<RouteContainer> Containers { get; set; } = [];
    private RadixTreeNode RootNode { get; set; } = new RadixTreeNode(string.Empty, RouteSegmentTypes.Root, null);
    
    public RadixTree(List<Route> routes)
    {
        foreach (Route route in routes)
        {
            AddRoute(route);
        }
    }

    public void AddRoute(Route route)
    {
        Routes.Add(route);

        /* resolve optional segments as all valid variations of the route, for example:
           /api/{arg1?}/{arg2} is resolved as: /api, /api/{arg1}, /api/{arg1}/{arg2}
        */
        List<List<RouteSegment>> truncations = GenerateTruncations(route.Segments);

        foreach (List<RouteSegment> trunc in truncations)
        {
            RouteContainer container = new RouteContainer(route)
            {
                Segments = trunc
            };
            
            Containers.Add(container);
            InsertRoute(container, RootNode);
        }

        int z = 0;
    }
    
    
    private static List<List<RouteSegment>> GenerateTruncations(List<RouteSegment> segments)
    {
        List<List<RouteSegment>> truncations = [];

        // Generate valid truncation points
        for (int i = 0; i < segments.Count; i++)
        {
            bool allSubsequentOptional = true;
            // Check if all segments after current index are optional or have defaults
            for (int j = i + 1; j < segments.Count; j++)
            {
                if (!segments[j].IsOptional && segments[j].DefaultValue == null)
                {
                    allSubsequentOptional = false;
                    break;
                }
            }

            if (allSubsequentOptional)
            {
                truncations.Add(segments.Take(i + 1).ToList());
            }
        }

        // Always include the full route
        truncations.Add(segments);

        return truncations.Distinct().ToList();
    }
    
    private static void InsertRoute(RouteContainer route, RadixTreeNode rootNode)
    {
        RadixTreeNode ptr = rootNode;

        for (int index = 0; index < route.Segments.Count; index++)
        {
            RouteSegment segment = route.Segments[index];
            // Mark as routable if this is the last segment in the truncation
            bool isRoutable = index == route.Segments.Count - 1;
            ptr = InsertRouteSegment(segment, ptr, route.Route, isRoutable);
        }
    }
    
    // Compare constraints by predefined priority
    private static int CompareConstraintPriority(RadixTreeNode a, RadixTreeNode b)
    {
        // Higher route priority comes first
        int priorityCompare = GetTypePriority(a.Constraints).CompareTo(GetTypePriority(b.Constraints));
        return priorityCompare != 0 ? priorityCompare : (b.Handler?.Priority ?? 0).CompareTo(a.Handler?.Priority ?? 0);

        int GetTypePriority(List<RouteConstraint> constraints)
        {
            if (constraints.Count == 0) return int.MaxValue;
            return constraints.Min(c => c.Type switch {
                "int" => 1,
                "guid" => 2,
                "long" => 3,
                _ => 10
            });
        }
    }
    
    private class ConstraintComparer : IEqualityComparer<RouteConstraint>
    {
        public bool Equals(RouteConstraint? x, RouteConstraint? y) 
            => x?.Type == y?.Type && x?.Value == y?.Value && x?.Value2 == y?.Value2;
    
        public int GetHashCode(RouteConstraint obj) 
            => HashCode.Combine(obj.Type, obj.Value, obj.Value2);
    }

    private static RadixTreeNode InsertRouteSegment(RouteSegment segment, RadixTreeNode parent, Route handler, bool isRoutable)
    {
        parent.Childs ??= [];

        if (segment.Type is RouteSegmentTypes.Dynamic)
        {
            RadixTreeNode dynamicNode = new RadixTreeNode("__dynamic", RouteSegmentTypes.Dynamic, isRoutable ? handler : null)
            {
                ParamName = segment.LiteralValue,
                DefaultValue = segment.DefaultValue
            };
            
            // Add constraints
            if (segment.Constraints != null)
            {
                foreach (string constraint in segment.Constraints)
                {
                    dynamicNode.AddConstraint(constraint);
                }
            }
            
            string paramName = dynamicNode.ParamName!;
            parent.DynamicNodes ??= [];
            
            if (!parent.DynamicNodes.TryGetValue(paramName, out List<RadixTreeNode>? nodes))
            {
                nodes = [];
                parent.DynamicNodes[paramName] = nodes;
            }
            
            RadixTreeNode? existingNode = nodes.FirstOrDefault(n => 
                n.Constraints.SequenceEqual(dynamicNode.Constraints, new ConstraintComparer()));
        
            if (existingNode != null)
            {
                // Replace if higher priority
                if (handler.Priority > existingNode.Handler?.Priority)
                {
                    existingNode.Handler = handler;
                    existingNode.DefaultValue = dynamicNode.DefaultValue;
                }
                return existingNode;
            }
        
            nodes.Add(dynamicNode);
            nodes.Sort(CompareConstraintPriority);
            return dynamicNode;
        }
        
        if (segment.Type is RouteSegmentTypes.CatchAll)
        {
            // Handle CatchAll parameter
            RadixTreeNode catchAllNode = new RadixTreeNode("__catchall", RouteSegmentTypes.CatchAll, isRoutable ? handler : null)
            {
                ParamName = segment.LiteralValue,
                DefaultValue = segment.DefaultValue
            };

            // Add constraints
            if (segment.Constraints != null)
            {
                foreach (string constraint in segment.Constraints)
                {
                    catchAllNode.AddConstraint(constraint);
                }
            }

            parent.DynamicNodes ??= new Dictionary<string, List<RadixTreeNode>>();
            string paramName = catchAllNode.ParamName;

            if (!parent.DynamicNodes.TryGetValue(paramName, out List<RadixTreeNode>? nodes))
            {
                nodes = [];
                parent.DynamicNodes[paramName] = nodes;
            }

            RadixTreeNode? existingNode = nodes.FirstOrDefault(n => n.Constraints.SequenceEqual(catchAllNode.Constraints, new ConstraintComparer()));
            if (existingNode != null)
            {
                if (handler.Priority > existingNode.Handler?.Priority)
                {
                    existingNode.Handler = handler;
                    existingNode.DefaultValue = catchAllNode.DefaultValue;
                }
                return existingNode;
            }

            nodes.Add(catchAllNode);
            nodes.Sort(CompareConstraintPriority);
            return catchAllNode;
        }

        RadixTreeNode newNode = new RadixTreeNode(segment.RawSegment, segment.Type, isRoutable ? handler : null);
        
        if (parent.Childs.TryGetValue(segment.RawSegment, out RadixTreeNode? node) && node is not null)
        {
            if (isRoutable)
            {
                // check for priority, ignore if new route has lower priority
                if (node.Handler is null || handler.Priority >= node.Handler.Priority)
                {
                    node.Handler = handler;

                    // update descendants
                    if (segment is { Type: RouteSegmentTypes.Dynamic })
                    {
                        node.DefaultValue = segment.DefaultValue;
                        
                        if (segment.Constraints?.Count > 0)
                        {
                            node.Constraints.Clear();
                            foreach (string constraint in segment.Constraints)
                            {
                                node.AddConstraint(constraint);
                            }
                        }
                    }
                }
            }

            return node;
        }

        parent.Childs.Add(segment.RawSegment, newNode);
        return newNode;
    }

    public ParametrizedRouteResult ResolveRoute(string[] segments)
    {
        ResolvedRouteResult x = FindNode(segments);
        ParametrizedRouteResult y = ParametrizeResolvedRoute(x);

        return y;
    }

    public ParametrizedRouteResult ParametrizeResolvedRoute(ResolvedRouteResult node)
    {
        if (node.Node == RootNode)
        {
            return new ParametrizedRouteResult
            {
                Success = false
            };
        }
    
        RoutePrototype pr = new RoutePrototype(node.Node?.Text ?? string.Empty);

        return new ParametrizedRouteResult
        {
            Success = true, 
            Route = pr.Route, 
            NativeResult = node, 
            Params = node.Params
        };
    }
    
    public class RoutePrototype
    {
        public string Route { get; set; }
        public List<RouteSegment> Segments { get; set; }

        public RoutePrototype(string route)
        {
            route = route.Trim();
            
            Route = route;
            string[] segments = route.Split('/');
            Segments = [];

            foreach (string segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment))
                {
                    continue;
                }

                RouteSegment sg = new RouteSegment();

                if (segment.StartsWith('{') && segment.EndsWith('}'))
                {
                    sg.Type = RouteSegmentTypes.Dynamic;
                    sg.RawSegment = segment.Substring(1, segment.Length - 2);
                }
                else if (segment is "*")
                {
                    sg.Type = RouteSegmentTypes.Wildcard;
                    sg.RawSegment = segment;
                }
                else
                {
                    sg.RawSegment = segment;
                }

                Segments.Add(sg);
            }
        }
    }
    
    public ResolvedRouteResult FindNode(string[] segments)
    {
        ParseContext context = new ParseContext
        {
            Segments = segments.ToList()
        };

        return FindNodeRecursiveDescent(RootNode, context);
    }
    
    private static void CollectDefaultValues(RadixTreeNode node, Dictionary<string, string> parameters)
    {
        // Add current node's default value if applicable
        if (node is { ParamName: not null, DefaultValue: not null } && !parameters.ContainsKey(node.ParamName))
        {
            parameters[node.ParamName] = node.DefaultValue;
        }

        // Recurse into child nodes
        if (node.Childs != null)
        {
            foreach (RadixTreeNode? child in node.Childs.Values)
            {
                if (child != null) CollectDefaultValues(child, parameters);
            }
        }

        // Recurse into dynamic nodes
        if (node.DynamicNodes != null)
        {
            foreach (List<RadixTreeNode> dynamicList in node.DynamicNodes.Values)
            {
                foreach (RadixTreeNode dynamicNode in dynamicList)
                {
                    CollectDefaultValues(dynamicNode, parameters);
                }
            }
        }
    }

    private static ResolvedRouteResult FindNodeRecursiveDescent(RadixTreeNode currentNode, ParseContext context)
    {
        // Before starting, populate default values from current node
        CollectDefaultValues(currentNode, context.Params);
        
        // update best match
        if (currentNode.Handler is not null && (context.BestMatch is null || currentNode.Handler.Priority > context.BestMatch.Handler?.Priority))
        {
            context.BestMatch = currentNode;
            context.BestMatchIndex = context.CurrentIndex;
        }

        // are we finished?
        if (context.CurrentIndex >= context.Segments.Count)
        {
            Dictionary<string, string> resultParams = new Dictionary<string, string>(context.Params);
            CollectDefaultValues(currentNode, resultParams);
        
            return new ResolvedRouteResult
            {
                Node = currentNode,
                Params = resultParams,
                IsExactMatch = true,
                LastMatchedNode = currentNode
            };
        }

        string currentSegment = context.Segments[context.CurrentIndex];
        ResolvedRouteResult result = new ResolvedRouteResult
        {
            Node = currentNode,
            Params = new Dictionary<string, string>(context.Params),
            IsExactMatch = false,
            LastMatchedNode = context.BestMatch
        };

        if (currentNode.Childs is null)
        {
            return result;
        }

        // 1. try static match
        if (currentNode.Childs.TryGetValue(currentSegment, out RadixTreeNode? staticNode) && staticNode is not null)
        {
            context.CurrentIndex++;
            ResolvedRouteResult staticResult = FindNodeRecursiveDescent(staticNode, context);
            context.CurrentIndex--;

            if (staticResult.IsExactMatch)
                return staticResult;
        }

        // 2. try dynamic match
        if (currentNode.DynamicNodes != null)
        {
            foreach ((string? paramName, List<RadixTreeNode>? value) in currentNode.DynamicNodes)
            {
                foreach (RadixTreeNode dynamicNode in value)
                {
                    bool isValid;
                    string paramValue;

                    if (dynamicNode.Type is RouteSegmentTypes.CatchAll)
                    {
                        paramValue = string.Join("/", context.Segments.Skip(context.CurrentIndex));
                        isValid = dynamicNode.Constraints.Count == 0 || dynamicNode.Constraints.All(c => RouteConstraintValidator.Validators[c.Type](paramValue, c));
                    }
                    else
                    {
                        paramValue = context.Segments[context.CurrentIndex];
                        isValid = dynamicNode.Constraints.Count == 0 || dynamicNode.Constraints.All(c => RouteConstraintValidator.Validators[c.Type](paramValue, c));
                    }

                    if (isValid)
                    {
                        context.Params[paramName] = paramValue;
                        int previousIndex = context.CurrentIndex;
                        if (dynamicNode.Type is RouteSegmentTypes.CatchAll)
                        {
                            context.CurrentIndex = context.Segments.Count;
                        }
                        else
                        {
                            context.CurrentIndex++;
                        }

                        ResolvedRouteResult result2 = FindNodeRecursiveDescent(dynamicNode, context);
                        context.CurrentIndex = previousIndex;

                        if (result2.IsExactMatch)
                        {
                            return result2;
                        }

                        context.Params.Remove(paramName);
                    }
                }
            }
        }

        // 3. try wildcard
        if (currentNode.Childs.TryGetValue("*", out RadixTreeNode? wildcardNode))
        {
            int segmentCount = context.Segments.Count - context.CurrentIndex;
            if (segmentCount > 0)
            {
                int totalLength = 1; // Initial '/'
                
                for (int i = context.CurrentIndex; i < context.Segments.Count; i++)
                {
                    totalLength += context.Segments[i].Length + 1; // Segment + '/'
                }

                char[] buffer = ArrayPool<char>.Shared.Rent(totalLength);
                int position = 0;
                buffer[position++] = '/';

                for (int i = context.CurrentIndex; i < context.Segments.Count; i++)
                {
                    ReadOnlySpan<char> segment = context.Segments[i].AsSpan();
                    segment.CopyTo(buffer.AsSpan(position, segment.Length));
                    position += segment.Length;
                    buffer[position++] = '/';
                }

                context.Params["wildcard"] = new string(buffer, 0, position);
                ArrayPool<char>.Shared.Return(buffer);
            }
            else
            {
                context.Params["wildcard"] = "/";
            }

            return new ResolvedRouteResult
            {
                Node = wildcardNode,
                Params = new Dictionary<string, string>(context.Params),
                IsExactMatch = true,
                LastMatchedNode = wildcardNode
            };
        }

        // if we don't have exact match, try to return best prev. match
        if (context.BestMatch is not null && !result.IsExactMatch)
        {
            result.Node = context.BestMatch;
            result.LastMatchedNode = context.BestMatch;
            result.Params = context.Params;
        }

        return result;
    }
}