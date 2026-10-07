# GrowDesk — Development Plan

_As of 2026-09-30 · Source of truth: [Claude.md](Claude.md) (section numbers below are "§" references to it)_

The CRM is built in **8 milestones**. Each one ends with both apps building, migrations applied, and the app running locally before the next one starts (§60, §67).

---

## Status

| Milestone | State |
| --- | --- |
| 1 — Foundation and login | **Done** (2026-09-30) |
| 2 — Administration | **Done** (2026-09-30) |
| 3 — Customers | **Done** (2026-09-30) |
| 4 — Bookings and calendar | **Done** (2026-09-30) |
| 5 — Payments | **Done** (2026-09-30) |
| Change password (added) | **Done** (2026-09-30) |
| 6 — Dashboard | **Done** (2026-09-30) |
| 7 — Capture API + toolbar | **Done** (2026-09-30) |
| 8 — Polish | Next |

### Environment as built

| Item | Value |
| --- | --- |
| Product name | GrowDesk (code namespaces remain `DoctorCrm`) |
| Repository | `git@github-personal:dus1han/growdesk_marist.git` (public) |
| VPS stack | `/home/deploy/sites/growdesk_marist` (compose project `growdesk_marist`) |
| Containers | `growdesk_marist-db`, `growdesk_marist-api`, `growdesk_marist-web` |
| Database | PostgreSQL 18, database `growdesk_marist`, bound to `127.0.0.1:5440` on the VPS only |
| App URL | `http://169.58.92.105:3110` (plain HTTP until a domain + Caddy TLS) |
| Dev database access | SSH tunnel: `ssh -N -L 5440:127.0.0.1:5440 deploy@169.58.92.105` |
| Dev admin login | username `Dev_Admin` (accounts sign in with a username, not an email) |

**Development uses the VPS database.** It must be cleared (drop and re-seed) before go-live.

---

## 1. Starting point

The folder contains only `Claude.md`. The machine already has everything needed:

| Tool | Version | Note |
| --- | --- | --- |
| .NET SDK | 10.0.301 | Backend |
| Node.js | 24.17 | Frontend |
| PostgreSQL | 18 | Installed locally (`C:\Program Files\PostgreSQL\18`), not on PATH |
| Docker | 29.5 | Optional, for a throwaway dev database |

---

## 2. Decisions to confirm before coding

The spec leaves these open or contradicts itself. Change any you disagree with before Milestone 1.

| # | Question | Recommendation | Why |
| --- | --- | --- | --- |
| 1 | How do users stay logged in? | JWT in a secure HTTP-only cookie | Scripts in the page can't read it; Next.js middleware can protect routes with it |
| 2 | Where does payment data live? (§28 vs §40) | `payments` table is the single source of truth; `bookings` keeps only `consultation_charge` | Avoids two copies of payment status disagreeing |
| 3 | How does automation find stages admins can rename? | Each stage gets a fixed `system_key` (`interested`, `booked`, `consultation_completed`, …) | Labels can be renamed without breaking automation (§18) |
| 4 | Currency and timezone | System settings; dates stored in UTC, shown in the clinic's timezone | AED in the spec shouldn't be hard-coded |
| 5 | One doctor or several? | Several: calendar filters by doctor, overlap check is per doctor | `bookings.doctor_id` already implies it |
| 6 | How does the capture tool log in? | Client ID + secret exchanged for a short-lived token; rate limited and audit-logged | Meets §36 without a long-lived key on every request |

---

## 3. Architecture

```text
WhatsApp Capture Tool ──HTTPS──┐
                               ▼
Next.js frontend ──HTTPS──► ASP.NET Core Web API ──► PostgreSQL
```

Neither the frontend nor the capture tool ever touches the database directly.

### Folder structure

```text
BasicCRM/
├── Claude.md
├── DEVELOPMENT_PLAN.md
├── docker-compose.yml          (optional dev Postgres)
├── frontend/                   Next.js App Router, TypeScript
│   ├── app/(auth)/login
│   ├── app/(dashboard)/{dashboard,customers,calendar,bookings,payments,administration}
│   ├── components/{ui,layout,dashboard,customers,bookings,calendar,forms}
│   ├── lib/{api,auth,permissions,utils}
│   ├── hooks/
│   └── types/
├── backend/
│   ├── DoctorCrm.Api/          Controllers, Services, DTOs, Entities, Data,
│   │                           Validators, Middleware, Authentication, Authorization
│   └── DoctorCrm.Tests/        xUnit + Testcontainers
└── database/                   migration notes, seed docs, ERD
```

### Libraries

| Layer | Packages |
| --- | --- |
| Frontend | Next.js, Tailwind, shadcn/ui, Framer Motion, Lucide, TanStack Query, React Hook Form, Zod, FullCalendar, dnd-kit (drag-to-reorder), sonner (toasts) |
| Backend | EF Core + Npgsql, FluentValidation, BCrypt.Net, libphonenumber-csharp, Serilog, Swashbuckle / OpenAPI |
| Tests | xUnit, Testcontainers.PostgreSql, Playwright |

### Backend conventions
- Controllers stay thin; business rules live in services (§55). No repository layer (§57 allows it).
- DTOs only; entities are never returned.
- Every response uses `{ success, data, message, errors }` (§37).
- One error middleware logs technical detail and returns friendly messages (§47).

---

## 4. Milestones

### Milestone 1 — Foundation and login (§60 Phase 1, §68)
**Goal:** Login → animated transition → dashboard shell → authenticated API → PostgreSQL.

Backend
- [x] Create solution and `DoctorCrm.Api` project; add EF Core + Npgsql
- [x] `ApiResponse<T>`, error middleware, Swagger, health check, Serilog (no CORS: the frontend proxies /api)
- [x] Entities: `users`, `roles`, `permissions`, `user_roles`, `stages` (with `system_key`), `treatments`, `system_settings`
- [x] First migration + seed (roles, admin user, default stages, sample treatments)
- [x] Auth: BCrypt hashing, `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me`
- [x] HTTP-only cookie JWT, session expiry, inactive users blocked
- [x] Role/permission policies

Frontend
- [x] Create Next.js app; Tailwind + shadcn/ui; theme tokens (light/dark)
- [x] Central API client (sends cookie, handles 401 → login)
- [x] Auth context + route-protection middleware; `lib/permissions` map
- [x] **Premium login (§7, §65):** moving gradient, floating orbs/particles, logo scale-in, card and fields staggered in, button → progress animation → dashboard transition, reduced-motion support
- [x] Layout: collapsible sidebar (remembers state, tooltips when collapsed), mobile drawer/bottom nav, page transitions
- [x] Dashboard and admin shells; unauthorized page

**Done when:** both apps build with no TS or C# errors and the full login → dashboard flow runs locally.

### Milestone 2 — Administration (§43, §44)
- [x] Shared admin table component: search, add/edit drawer, activate/deactivate, drag-to-reorder
- [x] Users: create, edit, assign role, reset password, last login
- [x] Treatments, Stages (with colour), Lead Sources, Cancellation Reasons, Payment Methods
- [x] Custom fields: 9 types (§32), options for dropdown/multi-select, required, enabled, order
- [x] Capture tool configuration: built-in + custom fields, enabled/required/order, drag-and-drop (§31)
- [x] System settings: branding (logo, name, tagline), currency, timezone
- [x] Stage colours served by the API, never hard-coded in components

**Notes:** capture fields are previewed as the Chrome side panel beside WhatsApp Web. Currency must be a real ISO code (default AED).

**Done when:** every admin list saves, reorders and deactivates; inactive items are hidden from new forms.

### Milestone 3 — Customers (§12–§15, §50–§52)
- [x] Tables: `customers`, `customer_treatments`, `customer_custom_field_values`
- [x] Indexes: `whatsapp_number` (**unique**), `stage_id`, `created_at`, `next_followup_date`
- [x] Single WhatsApp normaliser (E.164, libphonenumber)
- [x] `GET /api/customers`: server-side pagination, debounced search (name / WhatsApp / Instagram), combinable filters
- [x] Customer list: filters in the URL (`/customers?stage=interested&treatment=botox`), cards on mobile
- [x] Add/edit customer drawer with treatments and custom fields
- [x] Customer profile: header, treatment badges, upcoming booking, booking history, activity timeline
- [x] Audit log service called from every mutating service method (§42)

**Notes:** a customer needs a name plus a WhatsApp number or Instagram name (matching the capture toolbar). WhatsApp and Instagram are both normalised and unique when present. Numbers from any country are accepted with their country code (+94…, 0094… or 94…); UAE numbers also work without it (Phone:DefaultRegion = AE).

**Done when:** duplicate WhatsApp numbers are rejected in any format, and filters combine correctly.

### Milestone 4 — Bookings and calendar (§19–§27) — most business-critical
- [x] Tables: `bookings`, `booking_treatments` (unique `booking_id + treatment_id`), `original_booking_id`
- [x] One booking state machine: `Booked → Completed | Rescheduled | Cancelled | NoShow`; all other transitions rejected
- [x] Booking drawer: customer autocomplete, date/time, multiple treatments, that day's existing bookings, overlap check
- [x] Calendar (FullCalendar): day / **week (default)** / month / agenda; status colours; click opens details
- [x] Booking details with Complete / Reschedule / Cancel / No-show actions
- [x] Complete consultation: charge, payment status/method, optional next treatment, notes
- [x] **Next-treatment rule:** date and treatment both empty or both filled — Zod (frontend) **and** FluentValidation (backend)
- [x] Reschedule: original → `Rescheduled`, charge 0; new `Booked` booking copies customer, treatments, doctor; linked via `original_booking_id`
- [x] Cancel with admin-configured reason; No-show; nothing is ever deleted
- [x] Stage automation: booking → Booked; completion → Consultation Completed

**Notes:** the payments table is created here (completion records the payment); Milestone 5 adds the payments screens. Doctor is optional; bookings without a doctor share one calendar for the overlap check. No-show is only possible from the booking's date.

**Done when:** every state transition and the next-treatment rule have passing backend tests.

### Milestone 5 — Payments (§28)
- [x] `payments` table: booking, customer, amount, status (Paid / Pending / Waived), method, date, created by
- [x] Payment recorded on consultation completion
- [x] Payments page with filters; payment history on the customer profile
- [x] Record a payment against a pending consultation (from the Payments page or the booking drawer)
- [x] Summary cards (collected, outstanding, waived) and Excel export of the filtered rows

**Notes:** payments are append-only. Settling a pending consultation adds a new Paid entry; the latest entry is the booking's current state. The list shows current entries by default, with a "Full history" switch for superseded ones.

### Added — Change password
- [x] Any user can change their own password (key icon in the sidebar user area); current password always required
- [x] New accounts and admin password resets get a temporary password: the user must choose their own on next sign-in (second step on the login screen, `/change-password`)
- [x] Enforced by the API (`PasswordChangeGate`): until changed, every endpoint answers 403 except session check, change password and anonymous ones; an admin reset also gates sessions already open
- [x] Users list shows a "Temporary password" badge

### Milestone 6 — Dashboard (§10, §11, §29)
- [x] `GET /api/dashboard` — real data only, clinic time zone; sections the user can't see are left out
- [x] Top cards: Today's Consultations, Upcoming, Follow-ups, Potential Customers (animated counters, change vs yesterday); each links to its list
- [x] Today's appointments (click → booking details)
- [x] Stage summary (click → filtered customer list)
- [x] Follow-ups: overdue, today and the next 7 days
- [x] Recent activity from the audit log (timeline animation), without automatic stage moves or duplicate payment entries
- [x] Skeleton loaders for every section; refreshes every minute and after any change

**Notes:** cancelled and rescheduled bookings are not counted as consultations. "Potential" means the Interested and Follow-up stages.

### Milestone 7 — Capture API (§30–§36)
- [x] Connections (Administration → Capture Tool): one per PC, client ID + secret shown once, revoke takes effect on the next request
- [x] Client-credential token endpoint (`POST /api/capture/token`, 15-minute Bearer token, own audience); rate limiting; audit logging
- [x] `GET /api/capture/config | treatments | stages | sources | custom-fields`
- [x] `POST /api/capture/customers`:
    1. Validate fields against capture configuration (switched-off fields are ignored)
    2. Normalise WhatsApp number and Instagram name
    3. Find by WhatsApp, then Instagram, or create → return `action: "created" | "updated"`
    4. Merge treatment interests (don't replace); append notes; never clear recorded data
    5. Apply stage (only while still a lead); save custom fields; set last contact
    6. Write audit entry naming the connection; return `customerId` and any warnings
- [x] Examples for every capture endpoint: [docs/CAPTURE_API.md](docs/CAPTURE_API.md)

**Toolbar (CHExt, branch `growdesk-capture`, v1.0.0):** GrowDesk-branded, connects with a
connection's client ID + secret over **http or https**, builds its fields from
`/api/capture/config` and refreshes them automatically (load, tab focus, every minute, before
saving). Verified end to end against the live server over plain HTTP.

**Toolbar in this repo:** `extension/` (history kept). `npm run publish:growdesk` puts the download and
signed package into `frontend/public/`; GrowDesk serves `/downloads/growdesk-capture.zip`,
`/capture/growdesk-capture.crx` and `/capture/update.xml` without sign-in. Every user has a
**GrowDesk Capture** page (account menu, or the toolbar's ?) with the animated guide and install
steps. IT steps: [docs/CAPTURE_TOOLBAR_IT.md](docs/CAPTURE_TOOLBAR_IT.md).

**Later:** move GrowDesk to a domain with HTTPS. Over plain HTTP the client secret and leads are
not encrypted in transit (the toolbar's settings page warns about this).

### Milestone 8 — Polish (§46–§49, §54, §64)
- [ ] Every screen: loading, empty, error and success states
- [ ] Toasts used sparingly
- [ ] Responsive check at 375 / 768 / 1024 / 1440 px
- [ ] Accessibility: keyboard, focus, ARIA, contrast, reduced motion
- [ ] Performance: query caching, indexes, bundle size
- [ ] Production builds of both apps; confirm no secrets exposed

### Effort summary

| # | Milestone | Rough effort |
| --- | --- | --- |
| 1 | Foundation and login | Large |
| 2 | Administration | Medium |
| 3 | Customers | Medium |
| 4 | Bookings and calendar | Large |
| 5 | Payments | Small |
| 6 | Dashboard | Small–Medium |
| 7 | Capture API | Medium |
| 8 | Polish | Medium |

---

## 5. Testing strategy

**Backend (xUnit + Testcontainers PostgreSQL)** — must pass before a milestone closes:
- Every booking state transition, including: rescheduled bookings have charge 0; bookings are never deleted
- Duplicate WhatsApp detection across formats (`+971 50 123 4567`, `0501234567`, `971501234567`)
- Next-treatment validation — all 4 cases (§24)
- Capture-config validation (required / disabled fields)
- Permission checks per role; inactive user cannot log in

**Frontend**
- Unit tests for Zod schemas (next-treatment rule especially)
- Playwright smoke test after Milestone 4: login → book → complete → reschedule

---

## 6. Getting started (daily development)

1. Open the database tunnel (leave it running):
   ```bash
   ssh -N -L 5440:127.0.0.1:5440 deploy@169.58.92.105
   ```
2. Backend — `backend/DoctorCrm.Api/appsettings.Development.json` holds the connection string, JWT key and seed admin (gitignored; copy `appsettings.Development.example.json` on a new machine):
   ```bash
   cd backend/DoctorCrm.Api && dotnet run        # http://localhost:5080, Swagger at /swagger
   ```
3. Frontend:
   ```bash
   cd frontend && npm install && npm run dev      # http://localhost:3000
   ```
4. Tests: `cd backend && dotnet test DoctorCrm.slnx` (needs Docker for the PostgreSQL test container).
5. Deploy: push to `main`. GitHub Actions tests, builds both images to GHCR and deploys to the VPS.
