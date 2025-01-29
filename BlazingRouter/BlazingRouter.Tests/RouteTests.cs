using System.Reflection;

namespace BlazingRouter.Tests;

class Page1
{
    public int Arg1 { get; set; }
}

class ComplexPage
{
    public string Section { get; set; }
    public int CategoryId { get; set; }
    public string SubCategory { get; set; }
}

class ApiEndpoint
{
    public string Version { get; set; }
    public string Resource { get; set; }
    public Guid Id { get; set; }
    public string Action { get; set; }
}

class BlogPost
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string Slug { get; set; }
}

class DocumentPage
{
    public string Department { get; set; }
    public string Category { get; set; }
    public string SubCategory { get; set; }
    public string DocumentId { get; set; }
}

class ProductsPage
{
    public string Category { get; set; }
    public int? Id { get; set; }
}

class UsersPage
{
    public Guid UserId { get; set; }
}

class FilesPage
{
    public string Filename { get; set; }
}

class SearchPage
{
    public string Query { get; set; }
    public int Page { get; set; }
}


public class Tests
{
    enum Roles
    {
        
    }
    
    private static void AssertRouteResolvesTo<T>(string route)
    {
        Assert.That(RouteManager.Match(route).MatchedRoute?.Handler, Is.EqualTo(typeof(T)));
    }
    
    private static void AssertRouteResolvesToNone(string route)
    {
        MatchResult match = RouteManager.Match(route);
        Assert.That(match.IsMatch, Is.EqualTo(false));
    }
    
    private static void AssertRouteHasParam(string route, string paramName, string expectedValue)
    {
        MatchResult match = RouteManager.Match(route);
        
        using (Assert.EnterMultipleScope())
        {
            Assert.That(match.IsMatch, Is.True);
            Assert.That(match.Params?[paramName], Is.EqualTo(expectedValue));
        }
    }
    
    [SetUp]
    public void Setup()
    {
        RouteManager.InitRouteManager(Assembly.GetExecutingAssembly(), new BlazingRouterBuilder<Roles>() );
        
        // base
        RouteManager.AddRoute(new Route("/test/{arg1:int}", typeof(Page1)));
        
        // products
        RouteManager.AddRoute(new Route("/products/{category:alpha}", typeof(ProductsPage)));
        RouteManager.AddRoute(new Route("/products/{category:alpha}/{id:int}", typeof(ProductsPage)));
        
        // users
        RouteManager.AddRoute(new Route("/users/{userId:guid}", typeof(UsersPage)));
        
        // files
        RouteManager.AddRoute(new Route("/files/{filename:length(1,100)}", typeof(FilesPage)));
        
        // search
        RouteManager.AddRoute(new Route("/search/{query}/{page:int:min(1)}", typeof(SearchPage)));
    }

    [Test]
    public void TestDynamicSegment()
    {
        AssertRouteResolvesTo<Page1>("/test/4");
    }
    
    [Test]
    public void TestNonexistingRoute()
    {
        AssertRouteResolvesToNone("/test/4/test");
    }

    [Test]
    public void TestInvalidIntParameter()
    {
        AssertRouteResolvesToNone("/test/abc");
    }

    [Test]
    public void TestProductCategoryRoute()
    {
        AssertRouteResolvesTo<ProductsPage>("/products/electronics");
        AssertRouteResolvesToNone("/products/123"); // not alpha
    }
    
    [Test]
    public void TestProductDetailRoute()
    {
        AssertRouteResolvesTo<ProductsPage>("/products/electronics/123");
        AssertRouteResolvesToNone("/products/electronics/abc");  // not int
    }

    [Test]
    public void TestUserRoute()
    {
        AssertRouteResolvesTo<UsersPage>("/users/550e8400-e29b-41d4-a716-446655440000");
        AssertRouteResolvesToNone("/users/invalid-guid");
    }

    [Test]
    public void TestFileRoute()
    {
        AssertRouteResolvesTo<FilesPage>("/files/document.txt");
    }
    
    [Test]
    public void TestFileRouteNone()
    {
        AssertRouteResolvesToNone("/files/");
    }

    [Test]
    public void TestSearchRoute()
    {
        AssertRouteResolvesTo<SearchPage>("/search/phones/1");
        AssertRouteResolvesToNone("/search/phones/0");  // must be > 1 to resolve
    }
    
    [Test]
    public void TestConventionRouting()
    {
        RouteManager.AddController("products");
        RouteManager.AddRoute(new Route("/products/index", typeof(ProductsPage)));
        
        AssertRouteResolvesTo<ProductsPage>("/products");
    }
    
    [Test]
    public void TestParameterValuesFromSetup()
    {
        AssertRouteHasParam("/test/2", "arg1", "2");
    }

    [Test]
    public void TestParameterValues()
    {
        RouteManager.AddRoute(new Route("/c1/{arg1:int}", typeof(Page1)));
        AssertRouteHasParam("/c1/42", "arg1", "42");
    }

    [Test]
    public void TestParameterValues2()
    {
        AssertRouteHasParam("/products/electronics", "category", "electronics");
        AssertRouteHasParam("/products/electronics/123", "id", "123");
    }
    
    [Test]
    public void TestMultipleConstraints()
    {
        RouteManager.AddRoute(new Route("/age/{value:int:min(0):max(120)}", typeof(Page1)));
        
        AssertRouteResolvesTo<Page1>("/age/25");
        AssertRouteResolvesToNone("/age/150");  // too big
        AssertRouteResolvesToNone("/age/-1");   // too small
        AssertRouteResolvesToNone("/age/abc");  // not a number
    }
    
    [Test]
    public void TestWildcardRoute()
    {
        RouteManager.AddRoute(new Route("/docs/*", typeof(DocumentPage)));
    
        AssertRouteResolvesTo<DocumentPage>("/docs/anything");
        AssertRouteResolvesTo<DocumentPage>("/docs/anything/else");
        AssertRouteResolvesTo<DocumentPage>("/docs/multiple/nested/paths");
    }

    [Test]
    public void TestWildcardRouteCapture()
    {
        RouteManager.AddRoute(new Route("/docs/*", typeof(DocumentPage)));
    
        AssertRouteHasParam("/docs/user/manual", "wildcard", "/user/manual/");
        AssertRouteHasParam("/docs/api/v1/users", "wildcard", "/api/v1/users/");
    }

    [Test]
    public void TestWildcardWithPrefixRoute()
    {
        RouteManager.AddRoute(new Route("/api/v1/*", typeof(ApiEndpoint)));
    
        AssertRouteResolvesTo<ApiEndpoint>("/api/v1/users");
        AssertRouteResolvesTo<ApiEndpoint>("/api/v1/users/123");
        AssertRouteResolvesTo<ApiEndpoint>("/api/v1/products/categories");
    }

    [Test]
    public void TestMultipleWildcardRoutes()
    {
        RouteManager.AddRoute(new Route("/api/v1/*", typeof(ApiEndpoint)));
        RouteManager.AddRoute(new Route("/docs/*", typeof(DocumentPage)));
    
        AssertRouteResolvesTo<ApiEndpoint>("/api/v1/users");
        AssertRouteResolvesTo<DocumentPage>("/docs/manual");
    }

    [Test]
    public void TestWildcardPriority()
    {
        RouteManager.AddRoute(new Route("/docs/special", typeof(Page1)));
        RouteManager.AddRoute(new Route("/docs/*", typeof(DocumentPage)));
    
        AssertRouteResolvesTo<Page1>("/docs/special");
        AssertRouteResolvesTo<DocumentPage>("/docs/other");
    }
    
    [Test]
    public void TestWildcardPriorityOrderDoesntMatter()
    {
        RouteManager.AddRoute(new Route("/docs2/*", typeof(DocumentPage)));
        RouteManager.AddRoute(new Route("/docs2/special", typeof(Page1)));
    
        AssertRouteResolvesTo<Page1>("/docs2/special");
        AssertRouteResolvesTo<DocumentPage>("/docs2/other");
    }

    [Test]
    public void TestComplexWildcardCapture()
    {
        RouteManager.AddRoute(new Route("/api/*", typeof(ApiEndpoint)));
    
        AssertRouteHasParam("/api/users/123/edit", "wildcard", "/users/123/edit/");
        AssertRouteHasParam("/api/products/categories/list", "wildcard", "/products/categories/list/");
    }
    
    [Test]
    public void TestRoutePriority()
    {
        RouteManager.AddRoute(new Route("/products/{id:int}", typeof(ProductsPage)));
        RouteManager.AddRoute(new Route("/products/{id:int}", typeof(UsersPage), 10));

        AssertRouteResolvesTo<UsersPage>("/products/123");
    }

    [Test]
    public void TestMultipleRoutesPriority()
    {
        RouteManager.AddRoute(new Route("/api/v1/*", typeof(ApiEndpoint)));
        RouteManager.AddRoute(new Route("/api/v1/users", typeof(UsersPage), 10));
        RouteManager.AddRoute(new Route("/api/v1/users/special", typeof(FilesPage), 20));

        AssertRouteResolvesTo<FilesPage>("/api/v1/users/special");
        AssertRouteResolvesTo<UsersPage>("/api/v1/users");
        AssertRouteResolvesTo<ApiEndpoint>("/api/v1/other");
    }

    [Test]
    public void TestEqualPriorityLastMatchWins()
    {
        RouteManager.AddRoute(new Route("/content/{id:int}", typeof(ProductsPage), 5));
        RouteManager.AddRoute(new Route("/content/{id:int}", typeof(UsersPage), 5));
        
        AssertRouteResolvesTo<UsersPage>("/content/123");
    }

    [Test]
    public void TestPriorityOverridesOrder()
    {
        RouteManager.AddRoute(new Route("/data/{id:int}", typeof(ProductsPage)));
        RouteManager.AddRoute(new Route("/data/{id:int}", typeof(UsersPage), 10));
        RouteManager.AddRoute(new Route("/data/{id:int}", typeof(FilesPage), 5));

        AssertRouteResolvesTo<UsersPage>("/data/123");
    }
}