using System.Net;
using System.Net.Http.Json;
using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace DoctorCrm.Tests;

/// <summary>Runs the real API against a throwaway PostgreSQL container.</summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminUsername = "Test_Admin";
    public const string AdminPassword = "Test-Password-123";

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:18").Build();

    public Task InitializeAsync() => _db.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", _db.GetConnectionString());
        builder.UseSetting("Jwt:Key", new string('k', 48));
        builder.UseSetting("AuthCookie:Secure", "false");
        builder.UseSetting("Seed:AdminUsername", AdminUsername);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        // Every test signs in; the production limit of 10 per minute would throttle the suite.
        builder.UseSetting("RateLimiting:LoginPerMinute", "1000");
        // As docker-compose passes it when unset.
        builder.UseSetting("Billing:AppUrl", "");
    }

    public HttpClient CreateCookieClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    /// <summary>
    /// Signs in as a user an admin just created and completes the required first-login password
    /// change, so the client can use the app. The new password is the temporary one plus "-own".
    /// </summary>
    public async Task<HttpClient> SignInNewUserAsync(string username, string temporaryPassword)
    {
        var client = CreateCookieClient();
        (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, temporaryPassword))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest(temporaryPassword, temporaryPassword + "-own"))).EnsureSuccessStatusCode();
        return client;
    }
}

public class AuthIntegrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_reports_database()
    {
        var body = await factory.CreateClient().GetFromJsonAsync<ApiResponse<HealthDtoShape>>("/api/health");
        Assert.True(body!.Data!.Database);
    }

    [Fact]
    public async Task Protected_endpoint_requires_a_session()
    {
        var response = await factory.CreateClient().GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.False(body!.Success);
    }

    [Fact]
    public async Task Wrong_password_is_rejected_with_a_generic_message()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest(ApiFactory.AdminUsername, "wrong"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal("Invalid username or password.", body!.Message);
    }

    [Fact]
    public async Task Invalid_body_returns_field_errors()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("", ""));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Contains(body!.Errors!, e => e.Field == "username");
        Assert.Contains(body.Errors!, e => e.Field == "password");
    }

    [Fact]
    public async Task Login_me_logout_round_trip()
    {
        var client = factory.CreateCookieClient();

        // Usernames are matched case-insensitively.
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(ApiFactory.AdminUsername.ToLowerInvariant(), ApiFactory.AdminPassword));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), c => c.Contains("httponly", StringComparison.OrdinalIgnoreCase));

        var me = await client.GetFromJsonAsync<ApiResponse<SessionDto>>("/api/auth/me");
        Assert.Equal(ApiFactory.AdminUsername, me!.Data!.User.Username);
        Assert.Null(me.Data.User.Email);
        Assert.Contains("Admin", me.Data.User.Roles);
        Assert.Contains("admin.access", me.Data.User.Permissions);

        await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Deactivated_user_loses_access_immediately()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var staffRoleId = await db.Roles.Where(r => r.Name == DoctorCrm.Api.Authorization.Roles.Staff).Select(r => r.Id).SingleAsync();
            db.Users.Add(new DoctorCrm.Api.Entities.User
            {
                FullName = "Temp User",
                Username = "Temp_User",
                NormalizedUsername = "TEMP_USER",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Temp-Password-1", 4),
                UserRoles = { new DoctorCrm.Api.Entities.UserRole { RoleId = staffRoleId } },
            });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateCookieClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("Temp_User", "Temp-Password-1"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var temp = await db.Users.SingleAsync(u => u.Username == "Temp_User");
            temp.IsActive = false;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        var again = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("Temp_User", "Temp-Password-1"));
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
    }

    [Fact]
    public async Task Branding_is_public_and_seeded()
    {
        var body = await factory.CreateClient().GetFromJsonAsync<ApiResponse<BrandingDto>>("/api/settings/branding");
        Assert.Equal("GrowDesk", body!.Data!.CrmName);
    }

    private record HealthDtoShape(string Status, bool Database);
}
