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
    public void TestNonExistingRoute()
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
    
    [Test]
    public void TestDateTimeParameter()
    {
        RouteManager.AddRoute(new Route("/events/{date:datetime}", typeof(Page1)));
    
        AssertRouteResolvesTo<Page1>("/events/2023-12-25");
        AssertRouteResolvesToNone("/events/invalid-date");
    }

    [Test]
    public void TestGuidParameter()
    {
        RouteManager.AddRoute(new Route("/items/{id:guid}", typeof(Page1)));
    
        AssertRouteResolvesTo<Page1>("/items/550e8400-e29b-41d4-a716-446655440000");
        AssertRouteResolvesToNone("/items/not-a-guid");
    }

    [Test]
    public void TestLengthConstraints()
    {
        RouteManager.AddRoute(new Route("/code/{value:length(5)}", typeof(Page1)));
        RouteManager.AddRoute(new Route("/name/{value:minlength(3)}", typeof(Page1)));
        RouteManager.AddRoute(new Route("/desc/{value:maxlength(10)}", typeof(Page1)));
        RouteManager.AddRoute(new Route("/key/{value:length(2,4)}", typeof(Page1)));
    
        AssertRouteResolvesTo<Page1>("/code/12345");
        AssertRouteResolvesToNone("/code/1234"); // not exactly 5
    
        AssertRouteResolvesTo<Page1>("/name/john");
        AssertRouteResolvesToNone("/name/jo"); // less than 3
    
        AssertRouteResolvesTo<Page1>("/desc/short");
        AssertRouteResolvesToNone("/desc/verylongtext"); // more than 10
    
        AssertRouteResolvesTo<Page1>("/key/abc");
        AssertRouteResolvesToNone("/key/a"); // too short
        AssertRouteResolvesToNone("/key/abcde"); // too long
    }

    [Test]
    public void TestNumericConstraints()
    {
        RouteManager.AddRoute(new Route("/temperature/{value:int:range(-50,50)}", typeof(Page1)));
        RouteManager.AddRoute(new Route("/percentage/{value:int:range(0,100)}", typeof(Page1)));
    
        AssertRouteResolvesTo<Page1>("/temperature/25");
        AssertRouteResolvesToNone("/temperature/100");
        AssertRouteResolvesToNone("/temperature/-51");
    
        AssertRouteResolvesTo<Page1>("/percentage/50");
        AssertRouteResolvesToNone("/percentage/101");
        AssertRouteResolvesToNone("/percentage/-1");
    }
    
    [Test]
    public void TestAlphaConstraint()
    {
        RouteManager.AddRoute(new Route("/category/{name:alpha}", typeof(Page1)));
    
        AssertRouteResolvesTo<Page1>("/category/electronics");
        AssertRouteResolvesTo<Page1>("/category/Books");
        AssertRouteResolvesToNone("/category/123");
        AssertRouteResolvesToNone("/category/games-and-toys");
    }

    [Test]
    public void TestRegexConstraint()
    {
        RouteManager.AddRoute(new Route("/email/{address:regex([a-z]+@[a-z]+\\.[a-z]{2,})}", typeof(Page1)));
    
        AssertRouteResolvesTo<Page1>("/email/test@example.com");
        AssertRouteResolvesToNone("/email/invalid-email");
    }

    [Test]
    public void TestRequiredConstraint()
    {
        RouteManager.AddRoute(new Route("/comment/{text:required}", typeof(Page1)));
    
        AssertRouteResolvesTo<Page1>("/comment/hello");
        AssertRouteResolvesToNone("/comment/");
    }

    [Test]
    public void TestMultipleSegmentsWithConstraints()
    {
        RouteManager.AddRoute(new Route("/blog/{year:int:range(2000,2023)}/{month:int:range(1,12)}/{slug:required}", typeof(BlogPost)));
    
        AssertRouteResolvesTo<BlogPost>("/blog/2023/12/my-first-post");
        AssertRouteResolvesToNone("/blog/1999/12/post"); // year too low
        AssertRouteResolvesToNone("/blog/2023/13/post"); // invalid month
        AssertRouteResolvesToNone("/blog/2023/12/"); // missing slug
    }

    [Test]
    public void TestComplexRouteWithMultipleConstraints()
    {
        RouteManager.AddRoute(new Route(
            "/docs/{department:alpha}/{category:length(2,10)}/{subcategory:regex([a-z-]+)}/{docId:guid}", 
            typeof(DocumentPage)));
    
        AssertRouteResolvesTo<DocumentPage>(
            "/docs/sales/marketing/social-media/550e8400-e29b-41d4-a716-446655440000");
        AssertRouteResolvesToNone(
            "/docs/123/marketing/social-media/550e8400-e29b-41d4-a716-446655440000"); // department not alpha
        AssertRouteResolvesToNone(
            "/docs/sales/a/social-media/550e8400-e29b-41d4-a716-446655440000"); // category too short
    }
}