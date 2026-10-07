using System.Net;
using System.Net.Http.Json;
using DoctorCrm.Api.DTOs;

namespace DoctorCrm.Tests;

/// <summary>Business rules of the administration module, against a real API and database.</summary>
public class AdminIntegrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpClient> AdminAsync()
    {
        var client = factory.CreateCookieClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(ApiFactory.AdminUsername, ApiFactory.AdminPassword));
        login.EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<T> DataAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
        Assert.True(body!.Success, body.Message);
        return body.Data!;
    }

    private static async Task<string?> MessageAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiResponse<object>>())!.Message;

    private async Task<int> RoleIdAsync(HttpClient admin, string name) =>
        (await DataAsync<List<RoleDto>>(await admin.GetAsync("/api/roles"))).Single(r => r.Name == name).Id;

    // ---- Audit log ---------------------------------------------------------------------------

    [Fact]
    public async Task Audit_log_lists_filters_and_names_the_customer_and_is_admin_only()
    {
        var admin = await AdminAsync();
        var name = $"Audit Person {Guid.NewGuid():N}"[..24];
        var customer = await DataAsync<CustomerDetailDto>(await admin.PostAsJsonAsync("/api/customers",
            new SaveCustomerRequest(name, $"050 {Random.Shared.Next(5000000, 5999999)}", null, null, null, null, 1, null, [1], null, null, null, null)));

        var found = await DataAsync<PagedResult<AuditLogDto>>(await admin.GetAsync($"/api/audit-logs?search={Uri.EscapeDataString(name)}"));
        var entry = Assert.Single(found.Items, e => e.Action == "Customer Created");
        Assert.Equal(name, entry.Subject);
        Assert.Equal(customer.Id, entry.CustomerId);
        Assert.NotNull(entry.UserName);

        var logins = await DataAsync<PagedResult<AuditLogDto>>(await admin.GetAsync("/api/audit-logs?action=User%20Logged%20In&pageSize=5"));
        Assert.NotEmpty(logins.Items);
        Assert.All(logins.Items, e => Assert.Equal("User Logged In", e.Action));

        var filters = await DataAsync<AuditFiltersDto>(await admin.GetAsync("/api/audit-logs/filters"));
        Assert.Contains("Customer Created", filters.Actions);
        Assert.Contains(filters.Users, u => u.Id == entry.UserId);

        await DataAsync<UserDto>(await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest("Audit Staff", "Audit_Staff", null, await RoleIdAsync(admin, "Staff"), "Staff-Pass-9")));
        var staff = await factory.SignInNewUserAsync("Audit_Staff", "Staff-Pass-9");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/audit-logs")).StatusCode);
    }

    // ---- Lookup lists -------------------------------------------------------------------------

    [Fact]
    public async Task Treatment_lifecycle_create_rename_deactivate_reorder()
    {
        var admin = await AdminAsync();

        var created = await DataAsync<LookupItemDto>(await admin.PostAsJsonAsync("/api/treatments",
            new SaveLookupItemRequest("Chemical Peel", "Light peel", null)));
        Assert.True(created.IsActive);

        // Names are unique regardless of case.
        var duplicate = await admin.PostAsJsonAsync("/api/treatments", new SaveLookupItemRequest("chemical peel", null, null));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var renamed = await DataAsync<LookupItemDto>(await admin.PutAsJsonAsync($"/api/treatments/{created.Id}",
            new SaveLookupItemRequest("Chemical Peel Pro", "Deeper peel", null)));
        Assert.Equal("Chemical Peel Pro", renamed.Name);
        Assert.Equal("Deeper peel", renamed.Description);

        await DataAsync<LookupItemDto>(await admin.PatchAsJsonAsync($"/api/treatments/{created.Id}/active", new SetActiveRequest(false)));
        var active = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/treatments"));
        var all = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/treatments?includeInactive=true"));
        Assert.DoesNotContain(active, t => t.Id == created.Id);
        Assert.Contains(all, t => t.Id == created.Id && !t.IsActive);

        var reversed = all.Select(t => t.Id).Reverse().ToList();
        var reordered = await DataAsync<List<LookupItemDto>>(await admin.PutAsJsonAsync("/api/treatments/reorder", new ReorderRequest(reversed)));
        Assert.Equal(reversed, reordered.Select(t => t.Id));

        // A partial list means the client is out of date.
        var partial = await admin.PutAsJsonAsync("/api/treatments/reorder", new ReorderRequest(reversed.Skip(1).ToList()));
        Assert.Equal(HttpStatusCode.Conflict, partial.StatusCode);
    }

    [Fact]
    public async Task Automation_stages_cannot_be_deactivated_but_custom_stages_can()
    {
        var admin = await AdminAsync();
        var stages = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/stages?includeInactive=true"));

        var customer = stages.Single(s => s.SystemKey == "customer");
        var refused = await admin.PatchAsJsonAsync($"/api/stages/{customer.Id}/active", new SetActiveRequest(false));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("built in", await MessageAsync(refused));

        // Renaming a built-in status is fine.
        var renamed = await DataAsync<LookupItemDto>(await admin.PutAsJsonAsync($"/api/stages/{customer.Id}",
            new SaveLookupItemRequest("Patient", null, "#0EA5E9")));
        Assert.Equal("customer", renamed.SystemKey);
        await DataAsync<LookupItemDto>(await admin.PutAsJsonAsync($"/api/stages/{customer.Id}", new SaveLookupItemRequest("Customer", null, "#22C55E")));

        var vip = await DataAsync<LookupItemDto>(await admin.PostAsJsonAsync("/api/stages", new SaveLookupItemRequest("VIP", null, "#e11d48")));
        Assert.Equal("#E11D48", vip.Color);
        Assert.Null(vip.SystemKey);
        var off = await DataAsync<LookupItemDto>(await admin.PatchAsJsonAsync($"/api/stages/{vip.Id}/active", new SetActiveRequest(false)));
        Assert.False(off.IsActive);

        var badColor = await admin.PostAsJsonAsync("/api/stages", new SaveLookupItemRequest("Bad", null, "red"));
        Assert.Equal(HttpStatusCode.BadRequest, badColor.StatusCode);
    }

    [Fact]
    public async Task Staff_can_read_lists_but_not_change_them_or_see_users()
    {
        var admin = await AdminAsync();
        await DataAsync<UserDto>(await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest("Staff Member", "Staff_One", null, await RoleIdAsync(admin, "Staff"), "Staff-Pass-1")));

        var staff = await factory.SignInNewUserAsync("Staff_One", "Staff-Pass-1");

        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/lead-sources")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PostAsJsonAsync("/api/lead-sources", new SaveLookupItemRequest("TikTok", null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/settings")).StatusCode);
    }

    [Fact]
    public async Task Default_lists_are_seeded()
    {
        var admin = await AdminAsync();
        Assert.Contains(await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/lead-sources")), x => x.Name == "Instagram");
        Assert.Contains(await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/payment-methods")), x => x.Name == "Bank Transfer");
        Assert.Contains(await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/cancellation-reasons")), x => x.Name == "Other");
    }

    // ---- Users --------------------------------------------------------------------------------

    [Fact]
    public async Task User_create_update_reset_password_and_duplicate_username()
    {
        var admin = await AdminAsync();
        var doctorRole = await RoleIdAsync(admin, "Doctor");

        var user = await DataAsync<UserDto>(await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest("Dr Maria", "Dr_Maria", "Maria@Clinic.com", doctorRole, "Doctor-Pass-1")));
        Assert.Equal("Doctor", user.RoleName);
        Assert.Equal("maria@clinic.com", user.Email);

        var taken = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest("Other", "dr_maria", null, doctorRole, "Doctor-Pass-1"));
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);

        var weak = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest("Weak", "Weak_User", null, doctorRole, "short"));
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

        var badName = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest("Spaces", "has space", null, doctorRole, "Doctor-Pass-1"));
        Assert.Equal(HttpStatusCode.BadRequest, badName.StatusCode);

        var updated = await DataAsync<UserDto>(await admin.PutAsJsonAsync($"/api/users/{user.Id}",
            new UpdateUserRequest("Dr Maria Lopez", "Dr_Maria", null, await RoleIdAsync(admin, "Receptionist"))));
        Assert.Equal("Receptionist", updated.RoleName);
        Assert.Null(updated.Email);

        (await admin.PostAsJsonAsync($"/api/users/{user.Id}/reset-password", new ResetPasswordRequest("New-Pass-99"))).EnsureSuccessStatusCode();
        var client = factory.CreateCookieClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("Dr_Maria", "Doctor-Pass-1"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("Dr_Maria", "New-Pass-99"))).StatusCode);
    }

    // ---- Password changes ---------------------------------------------------------------------

    [Fact]
    public async Task New_user_must_change_password_before_using_the_app()
    {
        var admin = await AdminAsync();
        var user = await DataAsync<UserDto>(await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest("New Starter", "New_Starter", null, await RoleIdAsync(admin, "Receptionist"), "Temp-Pass-1")));
        Assert.True(user.MustChangePassword);

        var client = factory.CreateCookieClient();
        var login = await DataAsync<SessionDto>(await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("New_Starter", "Temp-Pass-1")));
        Assert.True(login.User.MustChangePassword);

        // Everything except the session check and the change itself is refused until then.
        var blocked = await client.GetAsync("/api/customers");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        var body = await blocked.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Contains(body!.Errors!, e => e.Field == "password_change_required");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/settings/branding")).StatusCode);

        // Wrong current password, same password, weak password.
        var wrong = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Nope-1234", "Mine-Pass-22"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("Your current password is incorrect.", await MessageAsync(wrong));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temp-Pass-1", "Temp-Pass-1"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Temp-Pass-1", "short"))).StatusCode);

        var changed = await DataAsync<SessionDto>(await client.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest("Temp-Pass-1", "Mine-Pass-22")));
        Assert.False(changed.User.MustChangePassword);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/customers")).StatusCode);

        // The old password no longer works; the new one does, without another forced change.
        var fresh = factory.CreateCookieClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await fresh.PostAsJsonAsync("/api/auth/login", new LoginRequest("New_Starter", "Temp-Pass-1"))).StatusCode);
        var again = await DataAsync<SessionDto>(await fresh.PostAsJsonAsync("/api/auth/login", new LoginRequest("New_Starter", "Mine-Pass-22")));
        Assert.False(again.User.MustChangePassword);
    }

    [Fact]
    public async Task Admin_reset_requires_a_change_even_in_an_existing_session()
    {
        var admin = await AdminAsync();
        var user = await DataAsync<UserDto>(await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest("Reset Me", "Reset_Me", null, await RoleIdAsync(admin, "Staff"), "Temp-Pass-2")));
        var staff = await factory.SignInNewUserAsync("Reset_Me", "Temp-Pass-2");
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/customers")).StatusCode);

        (await admin.PostAsJsonAsync($"/api/users/{user.Id}/reset-password", new ResetPasswordRequest("Reset-Pass-3"))).EnsureSuccessStatusCode();
        Assert.True((await DataAsync<List<UserDto>>(await admin.GetAsync("/api/users"))).Single(u => u.Id == user.Id).MustChangePassword);

        // The open session is gated straight away, and can only continue with the reset password.
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/customers")).StatusCode);
        (await staff.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Reset-Pass-3", "Mine-Pass-44"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/customers")).StatusCode);
    }

    [Fact]
    public async Task Any_user_can_change_their_own_password()
    {
        var admin = await AdminAsync();
        await DataAsync<UserDto>(await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest("Changer", "Changer_One", null, await RoleIdAsync(admin, "Doctor"), "Temp-Pass-5")));
        var doctor = await factory.SignInNewUserAsync("Changer_One", "Temp-Pass-5");

        var session = await DataAsync<SessionDto>(await doctor.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest("Temp-Pass-5-own", "Second-Pass-6")));
        Assert.False(session.User.MustChangePassword);
        Assert.Equal(HttpStatusCode.OK,
            (await factory.CreateCookieClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("Changer_One", "Second-Pass-6"))).StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_lock_themselves_or_the_system_out()
    {
        var admin = await AdminAsync();
        var me = (await DataAsync<SessionDto>(await admin.GetAsync("/api/auth/me"))).User;

        var self = await admin.PatchAsJsonAsync($"/api/users/{me.Id}/active", new SetActiveRequest(false));
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Contains("your own account", await MessageAsync(self));

        // While Test_Admin is the only active admin, it can't be demoted.
        var admins = (await DataAsync<List<UserDto>>(await admin.GetAsync("/api/users")))
            .Count(u => u.RoleName == "Admin" && u.IsActive);
        if (admins == 1)
        {
            var demote = await admin.PutAsJsonAsync($"/api/users/{me.Id}",
                new UpdateUserRequest(me.FullName, me.Username, null, await RoleIdAsync(admin, "Staff")));
            Assert.Equal(HttpStatusCode.BadRequest, demote.StatusCode);
            Assert.Contains("last active administrator", await MessageAsync(demote));
        }
    }

    // ---- Custom fields ------------------------------------------------------------------------

    [Fact]
    public async Task Dropdown_custom_field_options_and_capture_row()
    {
        var admin = await AdminAsync();

        var noOptions = await admin.PostAsJsonAsync("/api/custom-fields", new SaveCustomFieldRequest("Branch", "Dropdown", false, []));
        Assert.Equal(HttpStatusCode.BadRequest, noOptions.StatusCode);

        var field = await DataAsync<CustomFieldDto>(await admin.PostAsJsonAsync("/api/custom-fields",
            new SaveCustomFieldRequest("Preferred Branch", "Dropdown", true,
                [new SaveCustomFieldOption(null, "Dubai Marina"), new SaveCustomFieldOption(null, "Jumeirah")])));
        Assert.Equal("preferred_branch", field.Key);
        Assert.Equal(["Dubai Marina", "Jumeirah"], field.Options.Select(o => o.Label));

        // It appears in the capture configuration, switched off.
        var capture = await DataAsync<List<CaptureFieldDto>>(await admin.GetAsync("/api/admin/capture-fields"));
        var row = capture.Single(c => c.Key == "preferred_branch");
        Assert.True(row.IsCustom);
        Assert.False(row.IsEnabled);

        // Rename one option, drop the other, add a new one.
        var marina = field.Options.Single(o => o.Label == "Dubai Marina");
        var updated = await DataAsync<CustomFieldDto>(await admin.PutAsJsonAsync($"/api/custom-fields/{field.Id}",
            new SaveCustomFieldRequest("Preferred Branch", "Dropdown", true,
                [new SaveCustomFieldOption(marina.Id, "Marina"), new SaveCustomFieldOption(null, "Downtown")])));
        Assert.Equal(["Marina", "Downtown"], updated.Options.Select(o => o.Label));
        Assert.Equal(marina.Id, updated.Options[0].Id);

        var typeChange = await admin.PutAsJsonAsync($"/api/custom-fields/{field.Id}",
            new SaveCustomFieldRequest("Preferred Branch", "Text", false, null));
        Assert.Equal(HttpStatusCode.BadRequest, typeChange.StatusCode);
    }

    // ---- Capture configuration ----------------------------------------------------------------

    [Fact]
    public async Task Capture_fields_are_admin_controlled_but_need_one_required_field()
    {
        var admin = await AdminAsync();
        var fields = await DataAsync<List<CaptureFieldDto>>(await admin.GetAsync("/api/admin/capture-fields"));
        Assert.All(fields.Where(f => f.Key is "name" or "whatsapp"), f => Assert.True(f.IsEnabled && f.IsRequired));

        // Nothing required: the toolbar could save an empty lead.
        var noneRequired = fields.Select(f => new SaveCaptureField(f.Key, f.IsEnabled, false)).ToList();
        var refused = await admin.PutAsJsonAsync("/api/admin/capture-fields", new SaveCaptureFieldsRequest(noneRequired));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("at least one field", await MessageAsync(refused));

        // WhatsApp can be made optional, e.g. for Instagram leads with Instagram required.
        var instagramLeads = fields.Select(f => new SaveCaptureField(f.Key,
            f.Key == "instagram" || f.IsEnabled, f.Key is "name" or "instagram")).ToList();
        var relaxed = await DataAsync<List<CaptureFieldDto>>(await admin.PutAsJsonAsync("/api/admin/capture-fields",
            new SaveCaptureFieldsRequest(instagramLeads)));
        Assert.False(relaxed.Single(f => f.Key == "whatsapp").IsRequired);
        Assert.True(relaxed.Single(f => f.Key == "instagram").IsRequired);

        // Reverse the order, and ask for "notes" to be required while hidden: required is dropped.
        var request = fields.AsEnumerable().Reverse()
            .Select(f => f.Key == "notes" ? new SaveCaptureField("notes", false, true) : new SaveCaptureField(f.Key, f.IsEnabled, f.IsRequired))
            .ToList();
        var saved = await DataAsync<List<CaptureFieldDto>>(await admin.PutAsJsonAsync("/api/admin/capture-fields", new SaveCaptureFieldsRequest(request)));
        Assert.Equal(request.Select(r => r.Key), saved.Select(s => s.Key));
        Assert.False(saved.Single(s => s.Key == "notes").IsRequired);
    }

    // ---- Settings -----------------------------------------------------------------------------

    [Fact]
    public async Task Settings_validate_and_update_branding()
    {
        var admin = await AdminAsync();
        var current = await DataAsync<SystemSettingsDto>(await admin.GetAsync("/api/admin/settings"));
        Assert.Equal("AED", current.Currency);

        var bad = await admin.PutAsJsonAsync("/api/admin/settings", current with { TimeZone = "Mars/Olympus", Currency = "dirham" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        // Three letters is not enough: it has to be a real currency (this once saved "DIR").
        var fakeCurrency = await admin.PutAsJsonAsync("/api/admin/settings", current with { Currency = "DIR" });
        Assert.Equal(HttpStatusCode.BadRequest, fakeCurrency.StatusCode);

        await DataAsync<SystemSettingsDto>(await admin.PutAsJsonAsync("/api/admin/settings", current with { Tagline = "Test tagline" }));
        var branding = await DataAsync<BrandingDto>(await factory.CreateClient().GetAsync("/api/settings/branding"));
        Assert.Equal("Test tagline", branding.Tagline);

        await DataAsync<SystemSettingsDto>(await admin.PutAsJsonAsync("/api/admin/settings", current));
    }
}
