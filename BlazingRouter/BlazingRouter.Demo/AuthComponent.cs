using System.Security.Claims;
using BlazingCore;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlazingRouter.Demo;

public class AuthComponent : ComponentBaseEx
{
    [CascadingParameter]
    private Task<AuthenticationState>? authenticationStateTask { get; set; }
    public ClaimsPrincipal? User { get; set; }
    
    protected override async Task OnInitializedAsync()
    {
        await GetAuthState();
        await base.OnInitializedAsync();
    }

    protected async Task GetAuthState()
    {
        if (authenticationStateTask is not null)
        {
            User = (await authenticationStateTask).User;
        }
    }
}