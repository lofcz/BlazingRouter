using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace BlazingRouter.Demo.Controllers;

public class LoginController : Controller
{
    public async Task<IActionResult> Logout()
    {
        if (User.Identity?.IsAuthenticated ?? false)
        {
            await HttpContext.SignOutAsync();
        }
        
        return Redirect("/");
    }

    public async Task<IActionResult> Login(MyRoles role)
    {
        if (User.Identity?.IsAuthenticated ?? false)
        {
            await HttpContext.SignOutAsync();
        }
        
        ClaimsIdentity identity = new ClaimsIdentity([ new Claim("role", ((int)role).ToString()) ], "Identity", "user", string.Empty);
        ClaimsPrincipal userPrincipal = new ClaimsPrincipal([identity]);
        
        await HttpContext.SignInAsync(userPrincipal, new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.Now.AddDays(365)
        });
        
        return Redirect("/");
    }
}