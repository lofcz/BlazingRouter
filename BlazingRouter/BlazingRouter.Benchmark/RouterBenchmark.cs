using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace BlazingRouter.Benchmark;

public class ProductPage
{
    public int Id { get; set; }
    public string Category { get; set; }
}

public class CategoryPage
{
    public string Name { get; set; }
}

public class UserPage
{
    public Guid UserId { get; set; }
}

public class BlogPage
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string Slug { get; set; }
}

public class ApiEndpoint
{
    public string Version { get; set; }
    public string Resource { get; set; }
}

public class DocumentPage
{
    public string Path { get; set; }
}

public class FilePage
{
    public string Filename { get; set; }
    public string Extension { get; set; }
}

public enum Roles
{
    
}

[SimpleJob(RuntimeMoniker.Net80, iterationCount: 10, invocationCount: 100_000)]
[MemoryDiagnoser]
public class RouterBenchmarks
{
    private static readonly Random Random = new Random();
    private static readonly string[] Categories = ["products", "users", "docs", "api", "blog", "services"];
    private static readonly string[] Actions = ["view", "edit", "delete", "update", "list"];
    private static readonly string[] FileTypes = ["pdf", "doc", "txt", "jpg", "png"];
    
    private List<string> GeneratedRoutes;
    private List<string> TestRequests;

    [GlobalSetup]
    public void Setup()
    {
        RouteManager.InitRouteManager(Assembly.GetExecutingAssembly(), new BlazingRouterBuilder<Roles>());
        
        GeneratedRoutes = [];
        GenerateRoutes();
        GenerateTestRequests(10000);
    }

    private void GenerateRoutes()
    {
        // Basic routes
        foreach (string category in Categories)
        {
            RouteManager.AddRoute(new Route($"/{category}", typeof(CategoryPage)));
            RouteManager.AddRoute(new Route($"/{category}/{{id:int}}", typeof(ProductPage)));
            
            // Nested routes
            foreach (string action in Actions)
            {
                RouteManager.AddRoute(new Route($"/{category}/{action}", typeof(CategoryPage)));
                RouteManager.AddRoute(new Route($"/{category}/{{id:int}}/{action}", typeof(ProductPage)));
            }
            
            // Deep nested routes
            for (int i = 1; i <= 5; i++)
            {
                RouteManager.AddRoute(new Route($"/{category}/section{i}/{{param:alpha}}", typeof(CategoryPage)));
                RouteManager.AddRoute(new Route($"/{category}/section{i}/{{id:int}}/details", typeof(ProductPage)));
            }
        }

        // API routes
        for (int version = 1; version <= 3; version++)
        {
            foreach (string resource in Categories)
            {
                RouteManager.AddRoute(new Route($"/api/v{version}/{resource}", typeof(ApiEndpoint)));
                RouteManager.AddRoute(new Route($"/api/v{version}/{resource}/{{id:guid}}", typeof(ApiEndpoint)));
                
                foreach (string action in Actions)
                {
                    RouteManager.AddRoute(new Route($"/api/v{version}/{resource}/{{id:guid}}/{action}", typeof(ApiEndpoint)));
                }
            }
        }

        // Blog routes
        RouteManager.AddRoute(new Route("/blog/{year:int}/{month:int}/{slug}", typeof(BlogPage)));
        RouteManager.AddRoute(new Route("/blog/{year:int}/{month:int}", typeof(BlogPage)));
        RouteManager.AddRoute(new Route("/blog/{year:int}", typeof(BlogPage)));

        // User routes
        RouteManager.AddRoute(new Route("/users/{userId:guid}", typeof(UserPage)));
        RouteManager.AddRoute(new Route("/users/{userId:guid}/profile", typeof(UserPage)));
        RouteManager.AddRoute(new Route("/users/{userId:guid}/settings", typeof(UserPage)));

        // File routes
        foreach (string type in FileTypes)
        {
            RouteManager.AddRoute(new Route($"/files/{{filename}}.{type}", typeof(FilePage)));
            foreach (string category in Categories)
            {
                RouteManager.AddRoute(new Route($"/files/{category}/{{filename}}.{type}", typeof(FilePage)));
            }
        }

        // Document routes
        RouteManager.AddRoute(new Route("/docs/*", typeof(DocumentPage)));
        RouteManager.AddRoute(new Route("/downloads/{**path}", typeof(DocumentPage)));

        // Store valid routes for testing
        StoreValidRoutes();
    }

    private void StoreValidRoutes()
    {
        GeneratedRoutes = [
            // Basic routes
            .. Categories.Select(c => $"/{c}"),
            .. Categories.Select(c => $"/{c}/123"),
            
            // Nested routes
            .. Categories.SelectMany(c => Actions.Select(a => $"/{c}/{a}")),
            .. Categories.SelectMany(c => Actions.Select(a => $"/{c}/123/{a}")),
            
            // Deep nested routes
            .. Categories.SelectMany(c => Enumerable.Range(1, 5).Select(i => $"/{c}/section{i}/test")),
            .. Categories.SelectMany(c => Enumerable.Range(1, 5).Select(i => $"/{c}/section{i}/123/details")),
            
            // API routes
            .. Enumerable.Range(1, 3).SelectMany(v => 
                Categories.Select(r => $"/api/v{v}/{r}")),
            .. Enumerable.Range(1, 3).SelectMany(v => 
                Categories.Select(r => $"/api/v{v}/{r}/550e8400-e29b-41d4-a716-446655440000")),
            .. Enumerable.Range(1, 3).SelectMany(v => 
                Categories.SelectMany(r => Actions.Select(a => 
                    $"/api/v{v}/{r}/550e8400-e29b-41d4-a716-446655440000/{a}"))),
            
            // Blog routes
            "/blog/2023/12/test-post",
            "/blog/2023/12",
            "/blog/2023",
            
            // User routes
            "/users/550e8400-e29b-41d4-a716-446655440000",
            "/users/550e8400-e29b-41d4-a716-446655440000/profile",
            "/users/550e8400-e29b-41d4-a716-446655440000/settings",
            
            // File routes
            .. FileTypes.Select(t => $"/files/document.{t}"),
            .. Categories.SelectMany(c => FileTypes.Select(t => $"/files/{c}/document.{t}")),
            
            // Document routes
            "/docs/test/path",
            "/downloads/test/nested/path"
        ];
    }

    private void GenerateTestRequests(int count)
    {
        TestRequests = [];
        
        // 70% valid routes from generated ones
        for (int i = 0; i < count * 0.7; i++)
        {
            TestRequests.Add(GeneratedRoutes[Random.Next(GeneratedRoutes.Count)]);
        }

        // 30% invalid routes
        for (int i = 0; i < count * 0.3; i++)
        {
            TestRequests.Add(GenerateInvalidRoute());
        }

        // Shuffle requests
        TestRequests = TestRequests.OrderBy(x => Random.Next()).ToList();
    }

    private static string GenerateInvalidRoute()
    {
        switch (Random.Next(4))
        {
            case 0: // Invalid parameter type
                return $"/{Categories[Random.Next(Categories.Length)]}/{Guid.NewGuid()}/invalid";
                
            case 1: // Non-existent path
                return $"/nonexistent/{Guid.NewGuid()}";
                
            case 2: // Invalid nesting
                return $"/{Categories[Random.Next(Categories.Length)]}/invalid/nested/too/deep";
                
            default: // Malformed path
                return $"/{new string(Enumerable.Range(0, Random.Next(1, 10))
                    .Select(_ => (char)Random.Next(97, 123)).ToArray())}";
        }
    }

    [Benchmark]
    public void RouteMatching()
    {
        string request = TestRequests[Random.Next(TestRequests.Count)];
        RouteManager.Match(request);
    }
}