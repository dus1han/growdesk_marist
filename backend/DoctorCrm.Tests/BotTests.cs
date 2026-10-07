using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DoctorCrm.Api.DTOs;
using DoctorCrm.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DoctorCrm.Tests;

/// <summary>The WhatsApp BOT API: leads, bookings with a fixed length, opening hours, clashes, updates and live events.</summary>
public class BotTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _seq;

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

    private static async Task<ApiResponse<JsonElement>> BodyAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!;

    /// <summary>A connection of the given kind, and a client already holding its token.</summary>
    private async Task<HttpClient> ConnectAsync(HttpClient admin, string kind = "Bot")
    {
        var created = await DataAsync<CaptureClientCreatedDto>(await admin.PostAsJsonAsync("/api/admin/capture-clients",
            new CreateCaptureClientRequest($"{kind} {Interlocked.Increment(ref _seq)}", kind)));
        Assert.Equal(kind, created.Client.Kind);
        var client = factory.CreateClient();
        var path = kind == "Bot" ? "/api/bot/token" : "/api/capture/token";
        var token = await DataAsync<CaptureTokenDto>(await client.PostAsJsonAsync(path, new CaptureTokenRequest(created.Client.ClientId, created.ClientSecret)));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    /// <summary>Open every day 08:00–20:00 with 45-minute bookings, so tests don't depend on the weekday.</summary>
    private static async Task OpenAllWeekAsync(HttpClient admin, int minutes = 45)
    {
        string[] days = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];
        await DataAsync<BookingHoursDto>(await admin.PutAsJsonAsync("/api/admin/booking-hours",
            new BookingHoursDto(minutes, days.Select(d => new OpeningDayDto(d, true, "08:00", "20:00")).ToList())));
    }

    /// <summary>A different future day per test (Dubai time), within the 180-day booking window.</summary>
    private static string Day() =>
        DateOnly.FromDateTime(DateTime.UtcNow.AddHours(4)).AddDays(2 + Interlocked.Increment(ref _seq) % 150).ToString("yyyy-MM-dd");

    private static string Number() => $"+97150{Interlocked.Increment(ref _seq) + 1000000:0000000}";

    /// <summary>Saves an interested customer, as the bot does on a new contact's first message; returns the number.</summary>
    private static async Task<string> SavedAsync(HttpClient bot, string name, int[] treatments, string? number = null)
    {
        number ??= Number();
        await DataAsync<BotLeadResultDto>(await bot.PostAsJsonAsync("/api/bot/customers", new BotLeadRequest(name, number, treatments, null)));
        return number;
    }

    private async Task<(HttpClient Admin, HttpClient Bot, int[] Treatments)> SetUpAsync()
    {
        var admin = await AdminAsync();
        await OpenAllWeekAsync(admin);
        var bot = await ConnectAsync(admin);
        var treatments = await DataAsync<List<BotTreatmentDto>>(await bot.GetAsync("/api/bot/treatments"));
        Assert.True(treatments.Count >= 2);
        return (admin, bot, treatments.Select(t => t.Id).ToArray());
    }

    [Fact]
    public async Task Bot_and_toolbar_tokens_only_work_on_their_own_API()
    {
        var admin = await AdminAsync();
        var bot = await ConnectAsync(admin);
        var toolbar = await ConnectAsync(admin, "Toolbar");

        Assert.Equal(HttpStatusCode.OK, (await bot.GetAsync("/api/bot/treatments")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await toolbar.GetAsync("/api/capture/config")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bot.GetAsync("/api/capture/config")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await toolbar.GetAsync("/api/bot/treatments")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/bot/treatments")).StatusCode);
        // A signed-in user's session can't call the bot API either.
        Assert.NotEqual(HttpStatusCode.OK, (await admin.GetAsync("/api/bot/treatments")).StatusCode);
    }

    [Fact]
    public async Task Lead_is_created_once_per_WhatsApp_number_as_Interested_from_WhatsApp_BOT()
    {
        var (admin, bot, t) = await SetUpAsync();
        var number = Number();

        var first = await DataAsync<BotLeadResultDto>(await bot.PostAsJsonAsync("/api/bot/customers",
            new BotLeadRequest("Sarah Bot", number, [t[0]], "Asked about prices")));
        Assert.Equal("created", first.Action);

        // Same number written differently: the same customer, the treatment added, the name kept.
        var spaced = number[..4] + " " + number[4..6] + " " + number[6..];
        var second = await DataAsync<BotLeadResultDto>(await bot.PostAsJsonAsync("/api/bot/customers",
            new BotLeadRequest("Someone Else", spaced, [t[1]], null)));
        Assert.Equal("updated", second.Action);
        Assert.Equal(first.CustomerId, second.CustomerId);
        Assert.Equal("Sarah Bot", second.CustomerName);

        var customer = await DataAsync<JsonElement>(await admin.GetAsync($"/api/customers/{first.CustomerId}"));
        Assert.Equal(BotService.SourceName, customer.GetProperty("leadSource").GetProperty("name").GetString());
        Assert.Equal(2, customer.GetProperty("treatments").GetArrayLength());
        Assert.Equal("interested", customer.GetProperty("stage").GetProperty("systemKey").GetString());
    }

    [Fact]
    public async Task Lead_needs_a_name_and_a_valid_number_but_the_treatment_can_wait()
    {
        var (admin, bot, t) = await SetUpAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await bot.PostAsJsonAsync("/api/bot/customers", new BotLeadRequest("", Number(), [t[0]], null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bot.PostAsJsonAsync("/api/bot/customers", new BotLeadRequest("A", "12", [t[0]], null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bot.PostAsJsonAsync("/api/bot/customers", new BotLeadRequest("A", Number(), [999999], null))).StatusCode);

        // A new contact before they say what they want: saved with no treatment.
        var none = await DataAsync<BotLeadResultDto>(await bot.PostAsJsonAsync("/api/bot/customers", new BotLeadRequest("No Treatment Nia", Number(), [], null)));
        Assert.Equal("created", none.Action);
        var noField = await DataAsync<BotLeadResultDto>(await bot.PostAsJsonAsync("/api/bot/customers", new BotLeadRequest("No Field Nuha", Number(), null, null)));
        Assert.Empty((await DataAsync<CustomerDetailDto>(await admin.GetAsync($"/api/customers/{noField.CustomerId}"))).Treatments);
    }

    [Fact]
    public async Task A_booking_without_a_treatment_needs_one_chosen_when_completing()
    {
        var (admin, bot, t) = await SetUpAsync();
        var number = Number();
        var lead = await DataAsync<BotLeadResultDto>(await bot.PostAsJsonAsync("/api/bot/customers", new BotLeadRequest("Unsure Umar", number, [], null)));
        var booked = await DataAsync<BotBookingResultDto>(await bot.PostAsJsonAsync("/api/bot/bookings", new BotBookingRequest(number, [], Day(), "11:00", null)));
        Assert.Empty(booked.Booking.Treatments);
        var id = booked.Booking.BookingId;

        // The bot can still change the note on it.
        await DataAsync<BotBookingResultDto>(await bot.PatchAsJsonAsync($"/api/bot/bookings/{id}", new BotBookingUpdateRequest(number, null, null, null, "Wants advice")));

        var methods = await DataAsync<List<LookupItemDto>>(await admin.GetAsync("/api/payment-methods"));
        var missing = await admin.PostAsJsonAsync($"/api/bookings/{id}/complete", new CompleteBookingRequest(100, "Paid", methods[0].Id, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("treatmentIds", (await BodyAsync(missing)).Errors.Single().Field);

        var done = await DataAsync<BookingDetailDto>(await admin.PostAsJsonAsync($"/api/bookings/{id}/complete",
            new CompleteBookingRequest(100, "Paid", methods[0].Id, null, null, null, [t[1]])));
        Assert.Equal("Completed", done.Status);
        Assert.Equal(t[1], Assert.Single(done.Treatments).Id);
        var customer = await DataAsync<CustomerDetailDto>(await admin.GetAsync($"/api/customers/{lead.CustomerId}"));
        Assert.Equal(t[1], Assert.Single(customer.Treatments).Id);
    }

    [Fact]
    public async Task Booking_a_saved_customer_gives_a_45_minute_consultation_and_moves_them_to_Booked()
    {
        var (admin, bot, t) = await SetUpAsync();
        var day = Day();

        var result = await DataAsync<BotBookingResultDto>(await bot.PostAsJsonAsync("/api/bot/bookings",
            new BotBookingRequest(await SavedAsync(bot, "Maria Bot", [t[0], t[1]]), [t[0], t[1]], day, "16:00", "First visit")));
        Assert.Equal("booked", result.Action);
        Assert.Equal(day, result.Booking.Date);
        Assert.Equal("16:00", result.Booking.StartTime);
        Assert.Equal("16:45", result.Booking.EndTime);
        Assert.Equal(2, result.Booking.Treatments.Count);

        var booking = await DataAsync<BookingDetailDto>(await admin.GetAsync($"/api/bookings/{result.Booking.BookingId}"));
        Assert.Null(booking.Doctor);
        Assert.Equal(BotService.SourceName, booking.Source);
        Assert.Equal("First visit", booking.Notes);
        Assert.Equal("interested", booking.CustomerStage.SystemKey); // booking never changes the status
    }

    [Fact]
    public async Task Only_a_saved_customer_can_be_booked()
    {
        var (_, bot, t) = await SetUpAsync();
        var unknown = await bot.PostAsJsonAsync("/api/bot/bookings", new BotBookingRequest(Number(), [t[0]], Day(), "10:00", null));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("whatsapp", (await BodyAsync(unknown)).Errors.Single().Field);
    }

    [Fact]
    public async Task An_interested_lead_who_books_later_is_the_same_customer()
    {
        var (_, bot, t) = await SetUpAsync();
        var number = Number();
        var lead = await DataAsync<BotLeadResultDto>(await bot.PostAsJsonAsync("/api/bot/customers", new BotLeadRequest("Lena Bot", number, [t[0]], null)));

        var result = await DataAsync<BotBookingResultDto>(await bot.PostAsJsonAsync("/api/bot/bookings",
            new BotBookingRequest(number, [t[0]], Day(), "10:00", null)));
        Assert.Equal(lead.CustomerId, result.Booking.CustomerId);

        var upcoming = await DataAsync<List<BotBookingDto>>(await bot.GetAsync($"/api/bot/bookings?whatsapp={Uri.EscapeDataString(number)}"));
        Assert.Equal(result.Booking.BookingId, Assert.Single(upcoming).BookingId);
    }

    [Fact]
    public async Task A_taken_time_answers_409_with_the_free_times_and_availability_skips_it()
    {
        var (_, bot, t) = await SetUpAsync();
        var day = Day();
        await DataAsync<BotBookingResultDto>(await bot.PostAsJsonAsync("/api/bot/bookings", new BotBookingRequest(await SavedAsync(bot, "First", [t[0]]), [t[0]], day, "11:00", null)));

        // 11:30 overlaps 11:00–11:45.
        var clash = await bot.PostAsJsonAsync("/api/bot/bookings", new BotBookingRequest(await SavedAsync(bot, "Second", [t[0]]), [t[0]], day, "11:30", null));
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        var free = (await BodyAsync(clash)).Data.GetProperty("freeTimes").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("11:45", free);
        Assert.DoesNotContain("11:30", free);

        var availability = await DataAsync<BotAvailabilityDto>(await bot.GetAsync($"/api/bot/availability?date={day}"));
        Assert.True(availability.Open);
        Assert.Equal(45, availability.DurationMinutes);
        Assert.Equal("08:00", availability.FreeTimes[0]);
        Assert.Equal("19:15", availability.FreeTimes[^1]); // the last start that ends by 20:00
        Assert.DoesNotContain("10:30", availability.FreeTimes); // would run into 11:00
        Assert.DoesNotContain("11:00", availability.FreeTimes);
        Assert.Contains("10:15", availability.FreeTimes);
        Assert.Contains("11:45", availability.FreeTimes);
    }

    [Fact]
    public async Task Bookings_stay_inside_opening_hours_and_the_future()
    {
        var (admin, bot, t) = await SetUpAsync();
        var day = Day();
        Assert.Equal(HttpStatusCode.BadRequest, (await bot.PostAsJsonAsync("/api/bot/bookings", new BotBookingRequest(await SavedAsync(bot, "A", [t[0]]), [t[0]], day, "07:30", null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bot.PostAsJsonAsync("/api/bot/bookings", new BotBookingRequest(await SavedAsync(bot, "A", [t[0]]), [t[0]], day, "19:30", null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bot.PostAsJsonAsync("/api/bot/bookings", new BotBookingRequest(await SavedAsync(bot, "A", [t[0]]), [t[0]], "2020-01-01", "10:00", null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bot.PostAsJsonAsync("/api/bot/bookings", new BotBookingRequest(await SavedAsync(bot, "A", [t[0]]), [t[0]], "05/10/2026", "10:00", null))).StatusCode);

        // A closed day: no free times, and no bookings.
        var date = DateOnly.Parse(day);
        string[] week = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];
        var closed = date.DayOfWeek.ToString().ToLowerInvariant();
        await DataAsync<BookingHoursDto>(await admin.PutAsJsonAsync("/api/admin/booking-hours",
            new BookingHoursDto(45, week.Select(d => d == closed ? new OpeningDayDto(d, false, null, null) : new OpeningDayDto(d, true, "08:00", "20:00")).ToList())));
        var availability = await DataAsync<BotAvailabilityDto>(await bot.GetAsync($"/api/bot/availability?date={day}"));
        Assert.False(availability.Open);
        Assert.Empty(availability.FreeTimes);
        Assert.Equal(HttpStatusCode.BadRequest, (await bot.PostAsJsonAsync("/api/bot/bookings", new BotBookingRequest(await SavedAsync(bot, "A", [t[0]]), [t[0]], day, "10:00", null))).StatusCode);
    }

    [Fact]
    public async Task Opening_hours_must_make_sense()
    {
        var admin = await AdminAsync();
        string[] week = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];
        var backwards = await admin.PutAsJsonAsync("/api/admin/booking-hours",
            new BookingHoursDto(45, week.Select(d => new OpeningDayDto(d, true, d == "monday" ? "18:00" : "09:00", d == "monday" ? "09:00" : "18:00")).ToList()));
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
        var tooShort = await admin.PutAsJsonAsync("/api/admin/booking-hours",
            new BookingHoursDto(45, week.Select(d => new OpeningDayDto(d, true, "09:00", "09:30")).ToList()));
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/admin/booking-hours",
            new BookingHoursDto(7, week.Select(d => new OpeningDayDto(d, true, "09:00", "18:00")).ToList()))).StatusCode);

        // A 60-minute length changes the end time the bot gets.
        await OpenAllWeekAsync(admin, 60);
        var bot = await ConnectAsync(admin);
        var t = (await DataAsync<List<BotTreatmentDto>>(await bot.GetAsync("/api/bot/treatments")))[0].Id;
        var result = await DataAsync<BotBookingResultDto>(await bot.PostAsJsonAsync("/api/bot/bookings", new BotBookingRequest(await SavedAsync(bot, "Hour", [t]), [t], Day(), "09:00", null)));
        Assert.Equal("10:00", result.Booking.EndTime);
        await OpenAllWeekAsync(admin);
    }

    [Fact]
    public async Task Update_moves_a_booking_by_rescheduling_and_changes_treatments_and_notes()
    {
        var (admin, bot, t) = await SetUpAsync();
        var number = Number();
        var day = Day();
        await SavedAsync(bot, "Mover", [t[0]], number);
        var booked = await DataAsync<BotBookingResultDto>(await bot.PostAsJsonAsync("/api/bot/bookings",
            new BotBookingRequest(number, [t[0]], day, "09:00", "Original note")));
        var id = booked.Booking.BookingId;

        // Someone else's number can't touch it.
        Assert.Equal(HttpStatusCode.NotFound, (await bot.PatchAsJsonAsync($"/api/bot/bookings/{id}",
            new BotBookingUpdateRequest(Number(), null, null, null, "x"))).StatusCode);
        // Date without time is refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await bot.PatchAsJsonAsync($"/api/bot/bookings/{id}",
            new BotBookingUpdateRequest(number, day, null, null, null))).StatusCode);

        var edited = await DataAsync<BotBookingResultDto>(await bot.PatchAsJsonAsync($"/api/bot/bookings/{id}",
            new BotBookingUpdateRequest(number, null, null, [t[1]], "Changed note")));
        Assert.Equal("updated", edited.Action);
        Assert.Equal(id, edited.Booking.BookingId);
        Assert.Equal(t[1], Assert.Single(edited.Booking.Treatments).Id);
        Assert.Equal("Changed note", edited.Booking.Notes);

        // Moving it to 09:30 overlaps its own old slot, which is fine.
        var moved = await DataAsync<BotBookingResultDto>(await bot.PatchAsJsonAsync($"/api/bot/bookings/{id}",
            new BotBookingUpdateRequest(number, day, "09:30", null, null)));
        Assert.Equal("rescheduled", moved.Action);
        Assert.Equal(id, moved.PreviousBookingId);
        Assert.NotEqual(id, moved.Booking.BookingId);
        Assert.Equal("10:15", moved.Booking.EndTime);
        Assert.Equal("Changed note", moved.Booking.Notes);

        var old = await DataAsync<BookingDetailDto>(await admin.GetAsync($"/api/bookings/{id}"));
        Assert.Equal("Rescheduled", old.Status);
        Assert.Equal(0, old.ConsultationCharge);
        Assert.Equal(moved.Booking.BookingId, old.RescheduledTo!.Id);

        // The old one can't be changed again.
        Assert.Equal(HttpStatusCode.Conflict, (await bot.PatchAsJsonAsync($"/api/bot/bookings/{id}",
            new BotBookingUpdateRequest(number, null, null, null, "again"))).StatusCode);
    }

    [Fact]
    public async Task The_bot_can_cancel_its_customers_booking_which_frees_the_time()
    {
        var (admin, bot, t) = await SetUpAsync();
        var day = Day();
        var number = await SavedAsync(bot, "Canceller", [t[0]]);
        var booked = await DataAsync<BotBookingResultDto>(await bot.PostAsJsonAsync("/api/bot/bookings", new BotBookingRequest(number, [t[0]], day, "15:00", null)));
        var id = booked.Booking.BookingId;

        // Someone else's number can't cancel it.
        Assert.Equal(HttpStatusCode.NotFound, (await bot.PostAsJsonAsync($"/api/bot/bookings/{id}/cancel", new BotCancelRequest(Number(), null))).StatusCode);

        var cancelled = await DataAsync<BotBookingResultDto>(await bot.PostAsJsonAsync($"/api/bot/bookings/{id}/cancel", new BotCancelRequest(number, "Can't make it")));
        Assert.Equal("cancelled", cancelled.Action);
        Assert.Equal("Cancelled", cancelled.Booking.Status);

        var detail = await DataAsync<BookingDetailDto>(await admin.GetAsync($"/api/bookings/{id}"));
        Assert.Equal("Cancelled", detail.Status);
        Assert.Equal("Customer request", detail.CancellationReason!.Name);
        Assert.Equal("Can't make it (via WhatsApp BOT)", detail.CancellationNote);

        // The time is free again, it no longer shows as upcoming, and it can't be cancelled twice.
        Assert.Contains("15:00", (await DataAsync<BotAvailabilityDto>(await bot.GetAsync($"/api/bot/availability?date={day}"))).FreeTimes);
        Assert.Empty(await DataAsync<List<BotBookingDto>>(await bot.GetAsync($"/api/bot/bookings?whatsapp={Uri.EscapeDataString(number)}")));
        Assert.Equal(HttpStatusCode.Conflict, (await bot.PostAsJsonAsync($"/api/bot/bookings/{id}/cancel", new BotCancelRequest(number, null))).StatusCode);
        var customer = await DataAsync<CustomerDetailDto>(await admin.GetAsync($"/api/customers/{detail.Customer.Id}"));
        Assert.Equal("cancelled", customer.Consultation.State);
    }

    [Fact]
    public async Task A_bot_booking_is_pushed_live_to_open_GrowDesk_screens()
    {
        var (admin, bot, t) = await SetUpAsync();

        using var stream = await admin.GetAsync("/api/live/stream", HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
        Assert.Equal("text/event-stream", stream.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync());
        Assert.Equal("retry: 5000", await reader.ReadLineAsync());

        var live = factory.Services.GetRequiredService<LiveEvents>();
        for (var i = 0; i < 50 && live.SubscriberCount == 0; i++) await Task.Delay(50);

        var result = await DataAsync<BotBookingResultDto>(await bot.PostAsJsonAsync("/api/bot/bookings",
            new BotBookingRequest(await SavedAsync(bot, "Live Bot", [t[0]]), [t[0]], Day(), "12:00", null)));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        // Skip the "ready" message every connection starts with; collect the booking event.
        var lines = new List<string>();
        var sawBooking = false;
        while (!(sawBooking && lines[^1].StartsWith("data:")))
        {
            var line = await reader.ReadLineAsync(timeout.Token);
            if (string.IsNullOrEmpty(line)) continue;
            if (line == "event: booking.created") sawBooking = true;
            if (sawBooking || line.StartsWith("id:")) lines.Add(line);
        }
        Assert.Contains($"id: {result.Booking.BookingId}", lines);
        Assert.Contains("event: booking.created", lines);
        var data = JsonDocument.Parse(lines.Last(l => l.StartsWith("data:"))["data:".Length..]).RootElement;
        Assert.Equal("Live Bot", data.GetProperty("customerName").GetString());
        Assert.True(data.GetProperty("newCustomer").GetBoolean());

        // A user without the bookings permission gets no stream.
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/live/stream")).StatusCode);
    }

    [Fact]
    public async Task A_reconnecting_screen_catches_up_on_bookings_it_missed()
    {
        var (admin, bot, t) = await SetUpAsync();
        var first = await DataAsync<BotBookingResultDto>(await bot.PostAsJsonAsync("/api/bot/bookings",
            new BotBookingRequest(await SavedAsync(bot, "Seen", [t[0]]), [t[0]], Day(), "13:00", null)));
        var missed = await DataAsync<BotBookingResultDto>(await bot.PostAsJsonAsync("/api/bot/bookings",
            new BotBookingRequest(await SavedAsync(bot, "Missed", [t[0]]), [t[0]], Day(), "13:00", null)));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/live/stream");
        request.Headers.Add("Last-Event-ID", first.Booking.BookingId.ToString());
        using var stream = await admin.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        string? line;
        do line = await reader.ReadLineAsync(timeout.Token);
        while (line is not null && !line.StartsWith("id:"));
        Assert.Equal($"id: {missed.Booking.BookingId}", line);
    }

    // ---- Blocked time ---------------------------------------------------------------------------

    [Fact]
    public async Task Blocked_time_is_skipped_by_the_bot_and_refused_for_staff_until_removed()
    {
        var (admin, bot, t) = await SetUpAsync();
        var day = Day();
        var date = DateOnly.Parse(day);
        var lead = await DataAsync<BotLeadResultDto>(await bot.PostAsJsonAsync("/api/bot/customers", new BotLeadRequest("Blocked", Number(), [t[0]], null)));

        // A consultation already booked in the time to block is reported, and stays booked.
        await DataAsync<BotBookingResultDto>(await bot.PostAsJsonAsync("/api/bot/bookings", new BotBookingRequest(await SavedAsync(bot, "Early", [t[0]]), [t[0]], day, "14:15", null)));
        var created = await DataAsync<CalendarBlockCreatedDto>(await admin.PostAsJsonAsync("/api/calendar/blocks",
            new CreateCalendarBlockRequest(date, null, new TimeOnly(14, 0), new TimeOnly(16, 0), "Doctor away")));
        Assert.Equal(1, created.BookedConsultations);

        var availability = await DataAsync<BotAvailabilityDto>(await bot.GetAsync($"/api/bot/availability?date={day}"));
        Assert.Contains("13:15", availability.FreeTimes); // ends 14:00, just before the block
        Assert.DoesNotContain("13:30", availability.FreeTimes);
        Assert.DoesNotContain("15:00", availability.FreeTimes);
        Assert.Contains("16:00", availability.FreeTimes);

        var botClash = await bot.PostAsJsonAsync("/api/bot/bookings", new BotBookingRequest(await SavedAsync(bot, "Late", [t[0]]), [t[0]], day, "15:00", null));
        Assert.Equal(HttpStatusCode.Conflict, botClash.StatusCode);
        Assert.Contains("16:00", (await BodyAsync(botClash)).Data.GetProperty("freeTimes").EnumerateArray().Select(e => e.GetString()));

        var staff = await admin.PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(lead.CustomerId, null, date, new TimeOnly(15, 0), new TimeOnly(15, 30), [t[0]], null));
        Assert.Equal(HttpStatusCode.Conflict, staff.StatusCode);
        Assert.Contains("Doctor away", (await BodyAsync(staff)).Message);

        var listed = await DataAsync<List<CalendarBlockDto>>(await admin.GetAsync($"/api/calendar/blocks?from={day}&to={day}"));
        Assert.Contains(listed, b => b.Id == created.Block.Id);

        (await admin.DeleteAsync($"/api/calendar/blocks/{created.Block.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(lead.CustomerId, null, date, new TimeOnly(15, 0), new TimeOnly(15, 30), [t[0]], null))).StatusCode);
    }

    [Fact]
    public async Task A_whole_day_block_over_several_days_leaves_no_free_times()
    {
        var (admin, bot, t) = await SetUpAsync();
        var start = DateOnly.Parse(Day());
        await DataAsync<CalendarBlockCreatedDto>(await admin.PostAsJsonAsync("/api/calendar/blocks",
            new CreateCalendarBlockRequest(start, start.AddDays(2), null, null, "Holiday")));

        var middle = start.AddDays(1).ToString("yyyy-MM-dd");
        var availability = await DataAsync<BotAvailabilityDto>(await bot.GetAsync($"/api/bot/availability?date={middle}"));
        Assert.Empty(availability.FreeTimes);
        Assert.Equal(HttpStatusCode.Conflict, (await bot.PostAsJsonAsync("/api/bot/bookings",
            new BotBookingRequest(await SavedAsync(bot, "Holiday", [t[0]]), [t[0]], middle, "10:00", null))).StatusCode);
        var after = start.AddDays(3).ToString("yyyy-MM-dd");
        Assert.NotEmpty((await DataAsync<BotAvailabilityDto>(await bot.GetAsync($"/api/bot/availability?date={after}"))).FreeTimes);
    }

    [Fact]
    public async Task Blocks_need_sensible_dates_and_times()
    {
        var admin = await AdminAsync();
        var day = DateOnly.Parse(Day());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/calendar/blocks",
            new CreateCalendarBlockRequest(day, day.AddDays(-1), null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/calendar/blocks",
            new CreateCalendarBlockRequest(day, null, new TimeOnly(10, 0), null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/calendar/blocks",
            new CreateCalendarBlockRequest(day, null, new TimeOnly(10, 0), new TimeOnly(9, 0), null))).StatusCode);
    }
}
