using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Services;
using Microsoft.Extensions.Configuration;

namespace DoctorCrm.Tests;

public class ContactNormalizerTests
{
    private readonly ContactNormalizer _normalizer = new(new ConfigurationBuilder().Build()); // default region AE

    [Theory]
    [InlineData("050 123 4567", "+971501234567")]
    [InlineData("+971 50 123 4567", "+971501234567")]
    [InlineData("00971501234567", "+971501234567")]
    [InlineData("971501234567", "+971501234567")]
    [InlineData("+94 77 123 4567", "+94771234567")]
    [InlineData("0094 77 123 4567", "+94771234567")]
    [InlineData("94771234567", "+94771234567")]         // international digits without "+"
    [InlineData("+44 7911 123456", "+447911123456")]
    [InlineData("447911123456", "+447911123456")]
    [InlineData("+61 412 345 678", "+61412345678")]
    [InlineData("+1 415 555 2671", "+14155552671")]
    [InlineData("12345", null)]
    [InlineData("not a number", null)]
    public void Normalizes_phone_numbers(string input, string? expected) =>
        Assert.Equal(expected, _normalizer.NormalizePhone(input));

    [Theory]
    [InlineData("@Sarah.Fernando", "sarah.fernando")]
    [InlineData("sarah_f", "sarah_f")]
    [InlineData("https://www.instagram.com/sarah.f/?hl=en", "sarah.f")]
    [InlineData("instagram.com/Sarah.F", "sarah.f")]
    [InlineData("has space", null)]
    [InlineData("@", null)]
    public void Normalizes_instagram_names(string input, string? expected) =>
        Assert.Equal(expected, ContactNormalizer.NormalizeInstagram(input));
}

public class CustomerIntegrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _seq;

    /// <summary>A UAE mobile number that no other test uses.</summary>
    private static string NewNumber() => $"050 {Interlocked.Increment(ref _seq) + 1000000:0000000}";

    private async Task<HttpClient> AdminAsync()
    {
        var client = factory.CreateCookieClient();
        (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(ApiFactory.AdminUsername, ApiFactory.AdminPassword))).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<T> DataAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
        Assert.True(body!.Success, body.Message);
        return body.Data!;
    }

    private static SaveCustomerRequest Customer(string name, string? whatsApp = null, string? instagram = null,
        IReadOnlyList<int>? treatments = null, int? stageId = null, Dictionary<string, JsonElement>? customFields = null) =>
        // Seeded treatment 1 and lead source 1 by default: a customer needs an interested treatment and a lead source.
        new(name, whatsApp, null, instagram, null, stageId, 1, null, treatments ?? [1], null, null, null, customFields);

    [Fact]
    public async Task Lead_source_is_required()
    {
        var admin = await AdminAsync();
        var response = await admin.PostAsJsonAsync("/api/customers", Customer("No Source", NewNumber()) with { LeadSourceId = null });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Treatments_are_required_and_cannot_be_emptied()
    {
        var admin = await AdminAsync();
        var none = await admin.PostAsJsonAsync("/api/customers",
            new SaveCustomerRequest("No Treatment", NewNumber(), null, null, null, null, 1, null, [], null, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        var body = await none.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal("Choose at least one interested treatment.", body!.Message);
        Assert.Equal("treatmentIds", body.Errors![0].Field);

        // An edit can change the interests but not remove them all.
        var created = await DataAsync<CustomerDetailDto>(await admin.PostAsJsonAsync("/api/customers", Customer("Has Treatment", NewNumber())));
        var cleared = await admin.PutAsJsonAsync($"/api/customers/{created.Id}",
            new SaveCustomerRequest("Has Treatment", created.WhatsApp, null, null, null, null, 1, null, [], null, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, cleared.StatusCode);

        // A customer who never had one (captured without) can still be saved, e.g. a stage change.
        int bare;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoctorCrm.Api.Data.AppDbContext>();
            var stage = db.Stages.Single(s => s.SystemKey == "interested");
            var c = new DoctorCrm.Api.Entities.Customer { Name = "Captured Bare", WhatsAppNumber = "+971500009999", StageId = stage.Id };
            db.Customers.Add(c);
            await db.SaveChangesAsync();
            bare = c.Id;
        }
        var followUp = (await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/stages"))).Single(s => s.SystemKey == "follow_up");
        var moved = await DataAsync<CustomerDetailDto>(await admin.PutAsJsonAsync($"/api/customers/{bare}",
            new SaveCustomerRequest("Captured Bare", "+971500009999", null, null, null, followUp.Id, 1, null, [], null, null, null, null)));
        Assert.Equal("follow_up", moved.Stage.SystemKey);
    }

    [Fact]
    public async Task New_customer_starts_as_interested_with_a_normalised_number()
    {
        var admin = await AdminAsync();
        var number = NewNumber();
        var created = await DataAsync<CustomerDetailDto>(await admin.PostAsJsonAsync("/api/customers", Customer("Sarah Fernando", number)));

        Assert.Equal("interested", created.Stage.SystemKey);
        Assert.StartsWith("+971 50", created.WhatsApp);

        var activity = await DataAsync<List<ActivityDto>>(await admin.GetAsync($"/api/customers/{created.Id}/activity"));
        Assert.Contains(activity, a => a.Action == "Customer Created");
    }

    [Fact]
    public async Task Same_whatsapp_in_another_format_is_a_duplicate()
    {
        var admin = await AdminAsync();
        var number = NewNumber();
        var first = await DataAsync<CustomerDetailDto>(await admin.PostAsJsonAsync("/api/customers", Customer("First Person", number)));

        var international = "+971" + number.Replace(" ", "")[1..];
        var response = await admin.PostAsJsonAsync("/api/customers", Customer("Second Person", international));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<DuplicateCustomerDto>>();
        Assert.Equal(first.Id, body!.Data!.ExistingCustomerId);
        Assert.Equal("whatsApp", body.Data.MatchedOn);
        Assert.Contains(body.Errors!, e => e.Field == "whatsApp");
    }

    [Fact]
    public async Task Instagram_only_customers_are_allowed_and_deduplicated()
    {
        var admin = await AdminAsync();
        var handle = $"insta.only{Interlocked.Increment(ref _seq)}";
        var created = await DataAsync<CustomerDetailDto>(await admin.PostAsJsonAsync("/api/customers", Customer("Insta Lead", instagram: "@" + handle.ToUpperInvariant())));
        Assert.Equal(handle, created.Instagram);
        Assert.Null(created.WhatsApp);

        var dup = await admin.PostAsJsonAsync("/api/customers", Customer("Insta Again", instagram: $"https://instagram.com/{handle}/"));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task A_customer_needs_a_valid_contact()
    {
        var admin = await AdminAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/customers", Customer("No Contact"))).StatusCode);

        var badPhone = await admin.PostAsJsonAsync("/api/customers", Customer("Bad Phone", "12345"));
        Assert.Equal(HttpStatusCode.BadRequest, badPhone.StatusCode);
        var body = await badPhone.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Contains(body!.Errors!, e => e.Field == "whatsApp");
    }

    [Fact]
    public async Task Update_records_stage_changes_and_new_treatment_interests()
    {
        var admin = await AdminAsync();
        var treatments = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/treatments"));
        var stages = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/stages"));
        var followUp = stages.Single(s => s.SystemKey == "follow_up");

        var created = await DataAsync<CustomerDetailDto>(await admin.PostAsJsonAsync("/api/customers",
            Customer("Maria Lopez", NewNumber(), treatments: [treatments[0].Id])));

        var updated = await DataAsync<CustomerDetailDto>(await admin.PutAsJsonAsync($"/api/customers/{created.Id}",
            Customer("Maria Lopez", created.WhatsApp, treatments: [treatments[0].Id, treatments[1].Id], stageId: followUp.Id)));
        Assert.Equal(followUp.Id, updated.Stage.Id);
        Assert.Equal(2, updated.Treatments.Count);

        var activity = await DataAsync<List<ActivityDto>>(await admin.GetAsync($"/api/customers/{created.Id}/activity"));
        Assert.Contains(activity, a => a.Action == "Stage Changed");
        // Only stage and treatments changed, so no separate "details updated" entry.
        Assert.DoesNotContain(activity, a => a.Action == "Customer Updated");
        var added = activity.Single(a => a.Action == "Treatment Added");
        Assert.Contains(treatments[1].Name, added.Details!.Value.GetProperty("treatments").GetRawText());
    }

    [Fact]
    public async Task Custom_fields_are_typed_validated_and_required()
    {
        var admin = await AdminAsync();
        var field = await DataAsync<CustomFieldDto>(await admin.PostAsJsonAsync("/api/custom-fields",
            new SaveCustomFieldRequest("Branch", "Dropdown", true, [new(null, "Marina"), new(null, "Downtown")])));
        var vip = await DataAsync<CustomFieldDto>(await admin.PostAsJsonAsync("/api/custom-fields",
            new SaveCustomFieldRequest("VIP", "Boolean", false, null)));
        try
        {
            var missing = await admin.PostAsJsonAsync("/api/customers", Customer("No Branch", NewNumber()));
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

            var badOption = await admin.PostAsJsonAsync("/api/customers", Customer("Bad Branch", NewNumber(),
                customFields: new() { ["branch"] = JsonSerializer.SerializeToElement(999999) }));
            Assert.Equal(HttpStatusCode.BadRequest, badOption.StatusCode);

            var marina = field.Options.Single(o => o.Label == "Marina");
            var created = await DataAsync<CustomerDetailDto>(await admin.PostAsJsonAsync("/api/customers", Customer("With Branch", NewNumber(),
                customFields: new()
                {
                    ["branch"] = JsonSerializer.SerializeToElement(marina.Id),
                    ["vip"] = JsonSerializer.SerializeToElement(true),
                })));

            Assert.Equal("Marina", created.CustomFields.Single(f => f.Key == "branch").Display);
            Assert.Equal("Yes", created.CustomFields.Single(f => f.Key == "vip").Display);
        }
        finally
        {
            // Other tests in this class create customers without these fields.
            await admin.PatchAsJsonAsync($"/api/custom-fields/{field.Id}/active", new SetActiveRequest(false));
            await admin.PatchAsJsonAsync($"/api/custom-fields/{vip.Id}/active", new SetActiveRequest(false));
        }
    }

    [Fact]
    public async Task List_searches_filters_and_pages()
    {
        var admin = await AdminAsync();
        var treatments = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/treatments"));
        var laser = treatments.Single(t => t.Name == "Laser");
        var tag = $"Findme{Interlocked.Increment(ref _seq)}";
        var number = NewNumber();

        var target = await DataAsync<CustomerDetailDto>(await admin.PostAsJsonAsync("/api/customers",
            Customer($"{tag} Alpha", number, treatments: [laser.Id])));
        await DataAsync<CustomerDetailDto>(await admin.PostAsJsonAsync("/api/customers", Customer($"{tag} Beta", NewNumber())));

        var byName = await DataAsync<PagedResult<CustomerListItemDto>>(await admin.GetAsync($"/api/customers?search={tag}"));
        Assert.Equal(2, byName.TotalCount);

        // Local format finds the internationally stored number.
        var byNumber = await DataAsync<PagedResult<CustomerListItemDto>>(await admin.GetAsync($"/api/customers?search={Uri.EscapeDataString(number)}"));
        Assert.Contains(byNumber.Items, c => c.Id == target.Id);

        var combined = await DataAsync<PagedResult<CustomerListItemDto>>(await admin.GetAsync(
            $"/api/customers?search={tag}&treatmentId={laser.Id}&stageId={target.Stage.Id}"));
        Assert.Equal([target.Id], combined.Items.Select(c => c.Id));

        var paged = await DataAsync<PagedResult<CustomerListItemDto>>(await admin.GetAsync($"/api/customers?search={tag}&pageSize=1&page=2"));
        Assert.Single(paged.Items);
        Assert.Equal(2, paged.TotalCount);
    }

    [Fact]
    public async Task Staff_can_view_but_not_create_customers()
    {
        var admin = await AdminAsync();
        var roles = await DataAsync<List<RoleDto>>(await admin.GetAsync("/api/roles"));
        var username = $"viewer{Interlocked.Increment(ref _seq)}";
        await DataAsync<UserDto>(await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest("Viewer", username, null, roles.Single(r => r.Name == "Staff").Id, "Viewer-Pass-1")));

        var staff = await factory.SignInNewUserAsync(username, "Viewer-Pass-1");
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/customers", Customer("Nope", NewNumber()))).StatusCode);
    }
}
