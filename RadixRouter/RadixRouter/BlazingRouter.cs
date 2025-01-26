using System.Text;

namespace RadixRouter;

public class BlazingRouter
{
    private List<Route> Routes { get; set; }
    private RadixTree Tree { get; set; }

    public BlazingRouter(List<Route> routes)
    {
        Routes = routes;
        Tree = new RadixTree(routes);
    }

    public MatchResult Match(string[] segments)
    {
        ParametrizedRouteResult x = Tree.ResolveRoute(segments);
        return new MatchResult(x.Success, x.NativeResult?.Node?.Handler, x.NativeResult?.Params);
    }
    
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
    public string Segment { get; set; }
    public RouteSegmentTypes Type { get; set; }
}

public class RadixTreeNode
{
    public Dictionary<string, RadixTreeNode?>? Childs { get; set; }
    public Route? Handler { get; set; }
    public string Text { get; set; }
    public RouteSegmentTypes Type { get; set; }

    public RadixTreeNode(string text, RouteSegmentTypes type, Route? handler)
    {
        Handler = handler;
        Text = text;
        Type = type;
    }
}

public class ResolvedRouteResult
{
    public RadixTreeNode? Node { get; set; }
    public Dictionary<string, string> Params { get; set; }

    public ResolvedRouteResult()
    {
        Params = new Dictionary<string, string>();
    }
}

public class ResolvedRouteSegment
{
    public string Segment { get; set; }
    public string ParamValue { get; set; }
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
    private List<Route> Routes { get; set; }
    private List<RouteContainer> Containers { get; set; } = [];
    private RadixTreeNode RootNode { get; set; } = new RadixTreeNode("", RouteSegmentTypes.Root, null);
    
    public RadixTree(List<Route> routes)
    {
        Routes = routes;
        
        // 1. routes -> route containers
        foreach (Route route in routes)
        {
            RouteContainer container = new RouteContainer(route);
            RouteSegmentTypes maxSegment = RouteSegmentTypes.Static;
            string[] routeSegments = route.UriSegments ?? [];
            
            foreach (string routeSegment in routeSegments)
            {
                RouteSegmentTypes localSegment = RouteSegmentTypes.Static;
                RouteSegment parsedSegment = new RouteSegment();
                
                string segmentCopy = routeSegment.ToLowerInvariant().Trim();
                if ((segmentCopy.StartsWith('{') && segmentCopy.EndsWith('}')) || segmentCopy.StartsWith(':'))
                {
                    segmentCopy = "__reservedDynamic";
                    localSegment = RouteSegmentTypes.Dynamic;
                }
                else if (segmentCopy == "*")
                {
                    localSegment = RouteSegmentTypes.Wildcard;
                }
                
                // pokud už jsme zadali dynamický segment, není možné zadat další statický, pokud už wildkartu, není možné zadat žádný další segment
                if ((int) localSegment < (int)maxSegment)
                {
                    // [todo] handle error
                }

                parsedSegment.Segment = segmentCopy;
                parsedSegment.Type = localSegment;

                if (!string.IsNullOrWhiteSpace(parsedSegment.Segment))
                {
                    container.Segments.Add(parsedSegment);   
                }

                maxSegment = localSegment;
            }
            
            Containers.Add(container);
        }
        
        // 2. insert
        foreach (RouteContainer container in Containers)
        {
            InsertRoute(container, RootNode);
        }
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
        RadixTreeNode newNode = new RadixTreeNode(segment.Segment, segment.Type, isRoutable ? handler : null);
        
        if (parent.Childs.TryGetValue(segment.Segment, out RadixTreeNode? node) && node is not null)
        {
            if (isRoutable)
            {
                node.Handler = handler;
            }
            
            return node;
        }
        
        parent.Childs.Add(segment.Segment, newNode);
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
        
        RoutePrototype pr = new RoutePrototype(node.Node?.Text ?? "");
        Dictionary<string, string> pars = pr.Resolve(node.Params);

        return new ParametrizedRouteResult {Success = true, Route = pr.Route, NativeResult = node, Params = pars};
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
                    sg.Segment = segment.Substring(1, segment.Length - 2);
                }
                else if (segment is "*")
                {
                    sg.Type = RouteSegmentTypes.Wildcard;
                    sg.Segment = segment;
                }
                else
                {
                    sg.Segment = segment;
                }

                Segments.Add(sg);
            }
        }

        public Dictionary<string, string> Resolve(Dictionary<string, string> pars)
        {
            int paramIndex = 0;
            Dictionary<string, string> resolvedDict = new Dictionary<string, string>();
            
            foreach (RouteSegment segment in Segments)
            {
                if (segment.Type == RouteSegmentTypes.Dynamic)
                {
                    if (pars.TryGetValue($"par_{paramIndex}", out string? val))
                    {
                        resolvedDict[segment.Segment] = val;
                        paramIndex++;
                    }
                }
                else if (segment.Type == RouteSegmentTypes.Wildcard)
                {
                    if (pars.TryGetValue($"wildcard", out string? val))
                    {
                        resolvedDict["wildcard"] = val;
                    }
                }
            }

            return resolvedDict;
        }
    }
   
    public ResolvedRouteResult FindNode(string[] segments)
    {
        ResolvedRouteResult result = new ResolvedRouteResult();
        List<ResolvedRouteSegment> finalPathParts = (from pathPart in segments select new ResolvedRouteSegment {Segment = pathPart, ParamValue = null}).ToList();
        
        if (finalPathParts.Count == 1 && finalPathParts[0].Segment != "index")
        {
            finalPathParts.Add(new ResolvedRouteSegment {Segment = "index"});
        }
        
        RadixTreeNode? currentCandidate = RootNode;
        RadixTreeNode? lastWildcardNode = null;
        RouteSegmentTypes currentSegmentType = RouteSegmentTypes.Static;
        int pars = 0;
        int wildcardIndex = 0;
        
        for (int i = 0; i < finalPathParts.Count; i++)
        {
            ResolvedRouteSegment pathPart = finalPathParts[i];
            
            if (currentCandidate?.Childs != null)
            {
                if (currentCandidate?.Childs != null && currentCandidate.Childs.TryGetValue(pathPart.Segment, out RadixTreeNode? node))
                {
                    currentCandidate = node;
                }
                else if (currentCandidate?.Childs != null && currentCandidate.Childs.TryGetValue("__reservedDynamic", out RadixTreeNode? paramNode))
                {
                    currentCandidate = paramNode;
                    currentSegmentType = RouteSegmentTypes.Dynamic;
                    pathPart.ParamValue = pathPart.Segment;
                    result.Params[$"par_{pars}"] = pathPart.Segment;
                    pars++;
                }
                else
                {
                    if (lastWildcardNode != null)
                    {
                        currentCandidate = lastWildcardNode;
                        result.Node = currentCandidate;
                        return result;
                    }
                }
                
                if (currentCandidate?.Childs != null && currentCandidate.Childs.TryGetValue("*", out RadixTreeNode? wildcardNode))
                {
                    lastWildcardNode = wildcardNode;
                    wildcardIndex = i + 1;
                }
            }
            else if (lastWildcardNode != null)
            {
                currentCandidate = lastWildcardNode;
                result.Node = currentCandidate;

                StringBuilder sb = new StringBuilder();

                sb.Append('/');
                for (int j = wildcardIndex; j < finalPathParts.Count; j++)
                {
                    sb.Append(finalPathParts[j].Segment);
                    sb.Append('/');
                }

                result.Params["wildcard"] = sb.ToString();
                return result;
            }
        }
        
        if (currentCandidate == RootNode)
        {
            if (currentCandidate.Childs?.TryGetValue("__reservedDynamic", out RadixTreeNode? nextDynamic) is not null)
            {
                currentCandidate = nextDynamic;
                result.Params[$"par_{pars}"] = "";
            }
        }
        
        result.Node = currentCandidate;
        return result;
    }
}