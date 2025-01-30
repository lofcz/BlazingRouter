using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using BlazingRouter.Shared;

namespace BlazingRouter;


/// <summary>
/// A route. Routes use the following syntax:<br/>
/// <i>1.</i> Routes consist of segments, which are divided by <c>/</c> (forward slash)<br/>
/// <i>2.</i> a route consists of zero or more segments, for example <c>/test/ping</c> uses two segments<br/>
/// <i>3.</i> a segment is defined as:<br/>
/// <i>3a)</i> alphanumeric literal, for example <c>test</c><br/>
/// <i>3b)</i> alphanumeric literal enclosed in curly brackets <c>{test}</c> (query argument), for example <c>/product/{name}</c><br/>
/// <i>3c)</i> star symbol <c>*</c> (wildcard), captures anything. <c>/blog/{id}/*</c> makes <c>/blog/2/my-book</c> a valid route. If wildcard is used, no further segments can be used for the route.<br/>
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
        UriSegments = SplitPattern(pattern.AsSpan());
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
        UriSegments = SplitPattern(pattern.AsSpan());
        Handler = handler;
        Priority = priority;
        AuthorizedRoles = authorizedRoles;
        ParseSegments();
    }
    
    internal Route(string pattern)
    {
        UriSegments = SplitPattern(pattern.AsSpan());
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
    
    private static List<string> SplitPattern(ReadOnlySpan<char> pattern)
    {
        List<string> segments = [];
        int start = 0;
        bool inSegment = false;

        for (int i = 0; i <= pattern.Length; i++)
        {
            if (i == pattern.Length || pattern[i] == '/')
            {
                if (inSegment)
                {
                    segments.Add(pattern.Slice(start, i - start).ToString());
                    inSegment = false;
                }
            }
            else if (!inSegment)
            {
                start = i;
                inSegment = true;
            }
        }

        return segments;
    }
    
    private void ParseSegments()
    {
        if (UriSegments is null)
        {
            return;
        }

        RouteSegmentTypes maxSegment = RouteSegmentTypes.Static;
        bool hasOptional = false;

        foreach (string routeSegment in UriSegments)
        {
            RouteSegment parsedSegment = new RouteSegment();
            ReadOnlySpan<char> segmentSpan = routeSegment.AsSpan().Trim();
            RouteSegmentTypes localSegment = RouteSegmentTypes.Static;

            if (segmentSpan.Length >= 2 && segmentSpan[0] == '{' && segmentSpan[^1] == '}')
            {
                ReadOnlySpan<char> innerSpan = segmentSpan.Slice(1, segmentSpan.Length - 2);
                List<string> parts = new List<string>();
                int partStart = 0;

                for (int i = 0; i <= innerSpan.Length; i++)
                {
                    if (i == innerSpan.Length || innerSpan[i] == ':')
                    {
                        if (partStart < i)
                        {
                            ReadOnlySpan<char> part = innerSpan.Slice(partStart, i - partStart).Trim();
                            if (!part.IsEmpty)
                            {
                                parts.Add(part.ToString());
                            }
                        }
                        partStart = i + 1;
                    }
                }

                if (parts.Count == 0)
                    continue;

                string paramName = parts[0];
                List<string> constraints = parts.Skip(1).ToList();
                bool isOptional = false;

                if (constraints.Count > 0)
                {
                    string lastConstraint = constraints[^1];
                    if (lastConstraint.EndsWith('?'))
                    {
                        constraints[^1] = lastConstraint[..^1];
                        isOptional = true;
                    }
                }
                else
                {
                    if (paramName.EndsWith('?'))
                    {
                        paramName = paramName[..^1];
                        isOptional = true;
                    }
                }

                constraints = constraints.Where(c => !string.IsNullOrEmpty(c)).ToList();

                parsedSegment.LiteralValue = paramName.ToLowerInvariant();
                parsedSegment.Constraints = constraints.Count > 0 ? constraints : null;
                parsedSegment.IsOptional = isOptional;
                parsedSegment.RawSegment = "__reservedDynamic";
                localSegment = RouteSegmentTypes.Dynamic;
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

            if (maxSegment is RouteSegmentTypes.Wildcard)
            {
                break;
            }

            if (hasOptional && !parsedSegment.IsOptional && localSegment != RouteSegmentTypes.Wildcard)
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
            ReadOnlySpan<char> paramsSpan = match.Groups["params"].ValueSpan;
            List<string> parameters = [];
            int start = 0;

            for (int i = 0; i <= paramsSpan.Length; i++)
            {
                if (i == paramsSpan.Length || paramsSpan[i] == ',')
                {
                    if (start < i)
                    {
                        ReadOnlySpan<char> param = paramsSpan.Slice(start, i - start).Trim();
                        if (!param.IsEmpty)
                            parameters.Add(param.ToString());
                    }
                    start = i + 1;
                }
            }

            return type switch
            {
                "regex" => new RouteConstraint(type, paramsSpan.IsEmpty ? null : paramsSpan.ToString()),
                _ => new RouteConstraint(
                    type,
                    parameters.Count > 0 ? parameters[0] : null,
                    parameters.Count > 1 ? parameters[1] : null)
            };
        });
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
    Wildcard
}

public class RouteSegment
{
    public string LiteralValue { get; set; }
    public string RawSegment { get; set; }
    public RouteSegmentTypes Type { get; set; }
    public List<string>? Constraints { get; set; }
    public bool IsOptional { get; set; }
}


public class RadixTreeNode
{
    public Dictionary<string, RadixTreeNode?>? Childs { get; set; }
    public Route? Handler { get; set; }
    public string Text { get; set; }
    public string? ParamName { get; set; }
    public RouteSegmentTypes Type { get; set; }
    public List<RouteConstraint> Constraints { get; set; } = new();

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
    }
    
    private static List<List<RouteSegment>> GenerateTruncations(List<RouteSegment> segments)
    {
        List<List<RouteSegment>> truncations = [];
        int firstOptionalIndex = -1;

        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i].IsOptional)
            {
                firstOptionalIndex = i;
                break;
            }
        }

        if (firstOptionalIndex is -1)
        {
            truncations.Add(segments);
            return truncations;
        }

        int requiredCount = firstOptionalIndex;
        
        if (requiredCount > 0)
        {
            truncations.Add(segments.Take(requiredCount).ToList());
        }

        for (int i = firstOptionalIndex; i < segments.Count; i++)
        {
            truncations.Add(segments.Take(i + 1).ToList());
        }

        return truncations;
    }
    
    private static void InsertRoute(RouteContainer route, RadixTreeNode rootNode)
    {
        RadixTreeNode ptr = rootNode;

        for (int index = 0; index < route.Segments.Count; index++)
        {
            RouteSegment segment = route.Segments[index];
            ptr = InsertRouteSegment(segment, ptr, route.Route, index == route.Segments.Count - 1);
        }
    }

    private static RadixTreeNode InsertRouteSegment(RouteSegment segment, RadixTreeNode parent, Route handler, bool isRoutable)
    {
        parent.Childs ??= [];
        RadixTreeNode newNode = new RadixTreeNode(segment.RawSegment, segment.Type, isRoutable ? handler : null);

        if (segment is { Type: RouteSegmentTypes.Dynamic })
        {
            newNode.ParamName = segment.LiteralValue;
            
            if (segment.Constraints is not null)
            {
                foreach (string constraint in segment.Constraints)
                {
                    newNode.AddConstraint(constraint);
                }   
            }
        }

        if (parent.Childs.TryGetValue(segment.RawSegment, out RadixTreeNode? node) && node is not null)
        {
            if (isRoutable)
            {
                // check for priority, ignore if new route has lower priority
                if (node.Handler is null || handler.Priority >= node.Handler.Priority)
                {
                    node.Handler = handler;

                    // update descendants
                    if (segment is { Type: RouteSegmentTypes.Dynamic, Constraints.Count: > 0 })
                    {
                        node.Constraints.Clear();
                        foreach (string constraint in segment.Constraints)
                        {
                            node.AddConstraint(constraint);
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
            return new ParametrizedRouteResult {Success = false};
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

    private static ResolvedRouteResult FindNodeRecursiveDescent(RadixTreeNode currentNode, ParseContext context)
    {
        // update best match
        if (currentNode.Handler is not null && (context.BestMatch is null || currentNode.Handler.Priority > context.BestMatch.Handler?.Priority))
        {
            context.BestMatch = currentNode;
            context.BestMatchIndex = context.CurrentIndex;
        }

        // are we finished?
        if (context.CurrentIndex >= context.Segments.Count)
        {
            return new ResolvedRouteResult
            {
                Node = currentNode,
                Params = new Dictionary<string, string>(context.Params),
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
        if (currentNode.Childs.TryGetValue("__reservedDynamic", out RadixTreeNode? paramNode) && paramNode is not null)
        {
            bool isValid = true;
            
            if (paramNode.Constraints.Count > 0)
            {
                isValid = paramNode.Constraints.All(constraint =>
                {
                    if (RouteConstraintValidator.Validators.TryGetValue(constraint.Type, out Func<string, RouteConstraint, bool>? validator))
                    {
                        return validator(currentSegment, constraint);
                    }
                    
                    return false; // unk constrain
                });
            }

            if (isValid)
            {
                string paramName = paramNode.ParamName ?? string.Empty;
                context.Params[paramName] = currentSegment;
        
                context.CurrentIndex++;
                ResolvedRouteResult paramResult = FindNodeRecursiveDescent(paramNode, context);
                context.CurrentIndex--;

                if (paramResult.IsExactMatch)
                    return paramResult;

                context.Params.Remove(paramName);
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