using DoctorCrm.Api.Authentication;
using DoctorCrm.Api.Authorization;
using DoctorCrm.Api.Data;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoctorCrm.Api.Controllers;

[ApiController]
[Route("api/customers")]
[HasPermission(Permissions.CustomersView)]
public class CustomersController(CustomerService customers) : ControllerBase
{
    /// <summary>Money owed is shown only to users who can see payments.</summary>
    private bool CanSeePayments => User.HasClaim(CrmClaims.Permission, Permissions.PaymentsView);

    /// <summary>Paged, searchable, filterable customer list. Filters combine.</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<CustomerListItemDto>>>> List([FromQuery] CustomerQuery query, CancellationToken ct)
    {
        if (!CanSeePayments) query.HasOutstanding = null;
        var page = await customers.ListAsync(query, ct);
        if (!CanSeePayments) page = page with { Items = page.Items.Select(c => c with { Outstanding = null }).ToList() };
        return Ok(ApiResponse<PagedResult<CustomerListItemDto>>.Ok(page));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApiResponse<CustomerDetailDto>>> Get(int id, CancellationToken ct) =>
        Ok(ApiResponse<CustomerDetailDto>.Ok(await WithoutMoneyUnlessAllowed(customers.GetAsync(id, ct))));

    [HttpGet("{id:int}/activity")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ActivityDto>>>> Activity(int id, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<ActivityDto>>.Ok(await customers.ActivityAsync(id, ct)));

    /// <summary>Creates a customer. 409 with the existing customer when the WhatsApp or Instagram is taken.</summary>
    [HttpPost]
    [HasPermission(Permissions.CustomersManage)]
    public async Task<ActionResult<ApiResponse<CustomerDetailDto>>> Create(SaveCustomerRequest request, CancellationToken ct) =>
        Ok(ApiResponse<CustomerDetailDto>.Ok(await WithoutMoneyUnlessAllowed(customers.CreateAsync(request, User.GetUserId(), ct)), "Customer created."));

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.CustomersManage)]
    public async Task<ActionResult<ApiResponse<CustomerDetailDto>>> Update(int id, SaveCustomerRequest request, CancellationToken ct) =>
        Ok(ApiResponse<CustomerDetailDto>.Ok(await WithoutMoneyUnlessAllowed(customers.UpdateAsync(id, request, User.GetUserId(), ct)), "Customer saved."));

    /// <summary>To the recycle bin. Only once the customer has no bookings left.</summary>
    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.RecordsDelete)]
    public async Task<ActionResult<ApiResponse<object>>> Delete(int id, [FromServices] RecycleBinService bin, CancellationToken ct)
    {
        await bin.DeleteCustomerAsync(id, User.GetUserId()!.Value, ct);
        return Ok(ApiResponse.Ok("Customer deleted."));
    }

    private async Task<CustomerDetailDto> WithoutMoneyUnlessAllowed(Task<CustomerDetailDto> customer) =>
        CanSeePayments ? await customer : await customer with { Outstanding = null };
}

/// <summary>Active users for "Assigned to" pickers. Anyone who can see customers can see this list.</summary>
[ApiController]
[Route("api/user-options")]
[HasPermission(Permissions.CustomersView)]
public class UserOptionsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<NamedRef>>>> List(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<NamedRef>>.Ok(await db.Users.AsNoTracking()
            .Where(u => u.IsActive).OrderBy(u => u.FullName)
            .Select(u => new NamedRef(u.Id, u.FullName)).ToListAsync(ct)));
}
