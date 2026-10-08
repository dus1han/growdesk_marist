namespace DoctorCrm.Api.Entities;

/// <summary>
/// A record an admin can delete to the recycle bin and restore. Deleted records are hidden from
/// every query by a global filter (AppDbContext); the recycle bin reads them with
/// IgnoreQueryFilters. Deleting follows the record hierarchy, lowest level first: payment, then
/// booking, then customer; admin lists only while nothing uses them (RecycleBinService).
/// </summary>
public interface ISoftDeletable
{
    DateTime? DeletedAt { get; set; }
    int? DeletedById { get; set; }
}
