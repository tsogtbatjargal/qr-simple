# Plan 0002: Periodic inspection records for Equipment

Status: Implemented

## Why

Field technicians who scan an Equipment QR code today see quick info plus a flat list of reference documents (user manual, maintenance instruction). There is no way to see **whether that machine has actually been inspected recently**, or to read the inspection report.

This plan adds a second class of attachment: a dated, noted, attributed inspection PDF that Operators upload per Equipment, and that anyone scanning the QR can browse — newest first, without logging in.

Two existing statements need amending when this ships, both repeated in "Docs to update":

- `CONTEXT.md` defines **Document** as the only kind of file attached to Equipment. An Inspection is a separate concept with its own table, not a `Document` with a special label — see decision 1.
- `AGENTS.md`'s v1 non-goals list includes **"audit/change history."** This feature records who uploaded each inspection and when, and who last edited it. That is a deliberate, narrow exception: provenance fields on one record type, not general change history across Equipment/Users. Amend the line to say so rather than deleting it.

## Settled decisions

Decisions below were settled with the project owner across a planning round and three grilling rounds on 2026-08-29. Each one is something a reasonable implementer could otherwise guess differently. **Do not silently change any of them** — if one turns out to be wrong once you're in the code, stop and flag it.

### Shape of the record

1. **Inspections are a new `Inspection` entity in a new table — not `Document` rows with a special `Label`.** Reusing `Document` would put every inspection PDF into the scan page's existing document-links nav (`ScanPage.Render` renders *every* non-photo document as a panel), and `Document` has nowhere to put a date, note, or uploader. Overloading `Label` would also entangle this with `DocumentCatalog`'s already-delicate single-photo invariant (see the `DocumentCatalog` bullet in AGENTS.md). A separate table means **zero risk to any existing document or photo behaviour**.

2. **`Inspection.Content` is non-nullable.** `Document` keeps a nullable `Content` alongside a nullable `Url` for a never-implemented link mode, which is why AGENTS.md warns — from real breakage — that every render site must branch `Content is not null ? ... : Url`. There are no legacy inspection rows, so requiring `Content` makes that entire bug class impossible here. **Do not mirror `Document`'s nullable shape "for consistency."**

3. **One PDF per inspection record.** Real inspections sometimes produce a report plus photos plus a checklist; merging to a single PDF is normal field practice, and a child table for multiple attachments roughly doubles this feature. Revisit only if it actually bites.

4. **PDF only.** `DocumentUpload.Validate` currently allows `.pdf/.doc/.docx/.xls/.xlsx` for documents; inspections accept `.pdf` / `application/pdf` alone. An inspection report is a signed-off artifact, and accepting an editable `.docx` invites "which copy is the real one."

5. **10MB cap**, below the 20MB document cap.

6. **`Kind` is required, and is a fixed list in code** (`InspectionKinds`, shaped exactly like `Roles.cs`) — not a DB-managed table like `Category`. Categories live in the DB because equipment types genuinely vary per site; inspection periods are standard and finite. Confirmed values: **Weekly, Monthly, Quarterly, Annual, Ad-hoc**. It is a pure display label (see decision 7) but stays required, because it is the only structured field that makes a long list scannable and a free-text note cannot group or filter later. Unknown kinds are rejected at the catalog boundary. Note these are stored as plain strings per row: adding a value later is trivial, renaming one strands every historical row on the old string.

7. **The app does not know inspection schedules and never flags anything overdue.** No interval field on `Equipment`, no next-due computation, no overdue badge. Due-date tracking is a plausible follow-up plan; nothing here should be contorted "so overdue is easy later" at the cost of being simple now.

8. **The operator enters the inspection date; the upload timestamp is recorded separately.** The list sorts and groups by the operator-entered `InspectionDate`, so an inspection performed in June and uploaded in July files under June. `UploadedByEmail` + `UploadedAtUtc` are stored alongside.

9. **Note is optional, capped at 1000 characters**, and rendered in full on the public page. A technician filing a clean routine check shouldn't be forced to type something.

### Visibility and permissions

10. **Inspections are fully public, exactly like documents are today.** The list page and the PDF bytes are served with no auth and no role filter, matching `GET /documents/{id}/content` and `GET /e/{id}`. A field technician scans and reads; no account, no login.

11. **`UploadedByEmail` is stored always but rendered only in the admin UI — never on the public page.** The public page is loadable by anyone holding the QR code, and by anything that crawls the URL; putting staff email addresses there is a needless exposure. The technician at the machine doesn't need to know who filed the report; the auditor who does has a login. The public row shows **date, kind, note, and the PDF link** only. This also kills the branch where `User` would need a display-name field — it doesn't.

12. **Uploading is Admin + Operator. Deleting is Admin only.** An Operator being able to permanently erase a failed or missed inspection they filed is precisely what the provenance field exists to prevent. Hard delete is consistent with existing precedent (`CategoryCatalog` and `DocumentCatalog` both call `.Remove`) — note that `CONTEXT.md`'s "nothing is ever hard-deleted" is scoped to Equipment records, not attachments. **If these records ever acquire legal or contractual weight, revisit this as a soft delete** (`DeletedAtUtc`, hidden from public, visible to Admin); a hard delete is unrecoverable if someone later has to prove an inspection happened.

13. **Operators can correct metadata on records they uploaded; the file is immutable.** Editable: `Note` and `InspectionDate`. Not editable by anyone: the PDF itself — replacing the file changes what the record attests to, so that path is delete-and-re-upload, which is Admin-gated by decision 12. Admins may edit any record's metadata. Ownership check is `UploadedByEmail == caller's email`. Because this makes records mutable, add **`LastEditedAtUtc`** and **`LastEditedByEmail`** (both nullable, null until first edit) — without them "uploaded by X" quietly stops being the whole truth. Edits are visible to Admins only; **no public "edited" indicator**, consistent with decision 11.

14. **Reader role gets read-only access** to the admin inspections page, matching how Reader gets a read-only Equipment detail view today.

15. **Retired equipment: the inspections page stays readable, new uploads are blocked** with a clear message. `/e/{id}` deliberately still resolves for Retired equipment so old labels aren't dead links, and the inspection history is exactly the thing worth keeping. But you don't inspect a decommissioned machine, and allowing it silently corrupts the record. Enforce in `InspectionCatalog`, and hide/disable the form in the UI — both, not just the UI.

### Presentation

16. **The list shows the last 6 months expanded, with everything older inside a native collapsed `<details>` element** labelled "Older inspections (N)". **No JavaScript** — the public scan surface is server-rendered static HTML with no JS today and must stay that way.

17. **The expanded set is "last 6 months, but never fewer than the 3 most recent."** For Annual or Quarterly regimes the last 6 months can legitimately contain zero records while several reports sit hidden under "Older," and the page would read as empty. One extra clause in the split function removes the failure mode instead of explaining it to the user.

18. **No cap on how many records the page renders.** An earlier draft capped the collapsed section at 24 to bound page weight. At the real volume (decision 20) a single machine accumulates roughly 50 records over five years, so the cap solved a problem that doesn't exist while introducing a count that lies — the scan-page panel would promise `(50)` and the page would render 24. Render everything below the 6-month split; the panel count is simply the true total. A **retention policy** (keep N years, then purge) is a real future need but is a compliance decision, not an implementation detail — it belongs in its own plan, not here.

19. **The PDF is served with a generated, self-describing download name** — `{EquipmentName}-{Kind}-{InspectionDate}.pdf`, e.g. `Pump-7-Monthly-2026-08-12.pdf`. The operator's original filename is whatever their phone or scanner app produced (`scan0001.pdf`, a camera timestamp) and is the one string in this feature nobody controls. These PDFs end up in downloads folders and email attachments, where a bare GUID or `scan0001.pdf` is useless. **Implementation trap: do not use `Results.File(..., fileDownloadName:)`** — that sets `Content-Disposition: attachment`, which forces a download instead of letting the phone's browser display the PDF inline. Set `Content-Disposition: inline; filename="..."` on the response manually so you get both the name and in-browser viewing. Equipment names may contain Cyrillic, so non-ASCII needs RFC 5987 `filename*=UTF-8''...` encoding alongside an ASCII-sanitised `filename` fallback, and path separators must be stripped.

20. **Volume is under 20 uploads/week across all equipment.** Therefore: no cross-equipment "recent uploads" view, no paging on the admin per-equipment list, no bulk upload. Single-equipment upload is the right and sufficient shape. If this grows past ~50/week, revisit both.

21. **The admin upload UI is a new page at `/app/equipment/{id}/inspections`, linked from `EquipmentDetail.razor`** — not another section bolted onto that page. `EquipmentDetail.razor` is already ~500 lines carrying an edit form, a QR panel, a photo section, and a documents section; inspections are an unbounded growing list, and a table inside an edit form is the wrong shape. Editing is **inline on that page**, not a separate detail route.

22. **No uniqueness constraint** on `(EquipmentId, InspectionDate, Kind)`. Two inspections of the same kind on one day is unusual but legitimate. Double-submit is prevented by the existing `IsBusy` button-disable pattern the other admin pages already use, not by the database.

23. **The inspections page shows equipment name + serial number** as a subheading, so someone opening a shared link knows which machine they're looking at.

### Dates and language

24. **`Asia/Ulaanbaatar` is the business timezone, held as one hardcoded constant.** The server runs in Fly's `ord` region (Chicago, UTC−5/−6) and users are in Mongolia (UTC+8) — a 13–14 hour gap. With "today" taken from `DateTime.UtcNow`, for roughly the **first 8 hours of every Mongolian working day** the upload form would default to *yesterday's* date and the 6-month boundary would shift by a day. Store `UploadedAtUtc` in UTC; derive "today" and render timestamps in business time.

    Verified 2026-08-29: `/usr/share/zoneinfo/Asia/Ulaanbaatar` **is present** in the production runtime image (`mcr.microsoft.com/dotnet/aspnet:10.0`, checked with `podman run`), and `InvariantGlobalization` is not set in the csproj — so `TimeZoneInfo.FindSystemTimeZoneById("Asia/Ulaanbaatar")` resolves. This is the usual way this bites and it does not bite here.

    A single constant, not config and not a per-Organization column: ADR 0001 models Organization from day one for eventual multi-tenancy, but a per-org timezone is speculative work for a tenant that doesn't exist, and a config knob nobody turns adds a production failure mode if it's ever set to a bad ID. Leave a comment naming the multi-org path.

25. **The new page is English, matching the rest of the app** (`lang="en"` throughout, no localization infrastructure today). Notes typed in Cyrillic work regardless — UTF-8, and the 2026-08-28 rebrand's `cyrillic-ext` subset already covers Mongolian Ө/Ү.

    **Note for a future plan:** proper localization (Mongolian UI for the field-facing surfaces at minimum) is wanted and should get its own plan. This page is the one technicians actually read on a phone at the machine, so it is a natural first target. Do not attempt it as part of this work — it needs `IStringLocalizer` wiring, resource files, a language-selection story for an anonymous public page, and decisions about which surfaces are translated.

## Storage growth

Inspection bytes go in Postgres `bytea`, reusing the exact mechanism `Document` already uses, per the project owner's instruction ("no need to worry about storing it, can be same as existing doc storage and process").

At the confirmed volume — under 20 uploads/week, ~1,000/year — this is roughly **1–3GB/year** at realistic inspection-PDF sizes, with the 10MB cap as the ceiling. Over five years that is a "resize the Fly volume once" item, not an architecture concern.

(An earlier draft of this plan sized it at ~10GB/year by assuming 100 machines on weekly inspection. That was roughly 20× the real rate. Corrected here so nobody makes a design decision against the wrong number.)

The design keeps `InspectionCatalog.GetContentAsync` as the single read path for bytes and `AddAsync` as the single write path, so a later move to object storage is a two-method change plus a migration. **Do not build an abstraction for that now** — just don't scatter direct `db.Inspections` byte access across call sites.

## Current state

Verified against the code at commit `6b3a47c` on 2026-08-29 — **re-verify before implementing**, it may have drifted.

- **`src/QrSimple.Api/Document.cs`** — `Id`, `EquipmentId`, `Label`, plus the nullable `Url` / `Content` / `ContentType` / `FileName` set added by the 2026-08-17 upload rework.
- **`src/QrSimple.Api/DocumentCatalog.cs`** — three things in one file: the `DocumentResult` abstract-record hierarchy with its `ToHttpResult(onSuccess)` mapper (`Success` / `NotFound` / `EquipmentNotFound` / `InvalidFile`); the `DocumentUpload` static class (size caps, extension/content-type allowlists, `Validate(fileName, contentType, sizeBytes, bool isPhoto)`); and `DocumentCatalog` itself (`AddUploadAsync`, `SetPhotoUploadAsync`, `DeleteAsync`, `GetContentAsync`, `ListAsync`). **This result/catalog shape is the house pattern — mirror it closely.**
- **`src/QrSimple.Api/ScanPage.cs`** — one static `Render(Equipment, IReadOnlyList<Document>)` returning a complete HTML document as a raw string literal. Inlines Lucide icons as `const string` fields (`FileTextIcon`, `ExternalLinkIcon`, `AlertIcon`), links `/brand/tokens.css`, holds all CSS in a `const string styles`. Renders `.site-header` with the ICS logo, a photo panel, a name panel, an optional retired notice, the document-links `<nav class="documents">`, and an "Equipment details" `<dl>`.
- **`src/QrSimple.Api/Program.cs`** — minimal-API endpoints. Shapes to copy: `GET /e/{id}` (public, loads equipment + documents, `Results.Content(..., "text/html")`); `GET /documents/{id}/content` (public, no role filter); `POST /equipment/{id}/documents` (`IFormFile` + `request.Form["label"]`, `.DisableAntiforgery().RequireAuthorization().AddEndpointFilter(new RequireRoleFilter(Roles.Admin, Roles.Operator))`); `DELETE /equipment/{id}/documents/{documentId}`.
- **`src/QrSimple.Api/AppDbContext.cs`** — `DbSet`s for Equipment / Documents / Categories / Users. `OnModelCreating` currently only configures the `Status` string conversion, the unique `User.Email` index, and the `User.IsActive` default.
- **`src/QrSimple.Api/Components/Pages/EquipmentDetail.razor`** — `@inherits RoleGatedComponentBase`. It **re-resolves the caller itself** (`AuthenticationStateTask` → `ClaimTypes.Email` → `UserAuthorization.FindAsync`) into a local `CallerRole` field, duplicating the base class's work, because the base exposes neither role nor email to subclasses. The new page needs the caller's **email** (decisions 11, 13) and should follow the same local pattern. **Do not refactor `RoleGatedComponentBase` as part of this feature** — separate change, separate blast radius across every admin page.
- **`src/QrSimple.Api/Migrations/`** — three migrations, latest `20260817213350_AddDocumentFileUpload`. `Program.cs` calls `Database.Migrate()` at startup. See `docs/database-migrations.md`.
- **No date or timezone handling exists anywhere in `src/`** — no `DateTime`, `DateOnly`, or `TimeZoneInfo`. This feature introduces the app's first, so it sets the convention (decision 24).
- **Everything is `lang="en"`**; the only `CultureInfo` use is `InvariantCulture` in `EquipmentImport.cs`'s CSV reader. No localization infrastructure.
- **`tests/QrSimple.Api.Tests/`** — real-HTTP integration tests via `ApiFactory` (`WebApplicationFactory<Program>` + Testcontainers). `factory.CreateClientAs("Operator")` gives a role-authenticated client. `ScanPageTests.cs` asserts on text/href substrings only. `DocumentUploadTests.cs` + `TestUploads.cs` are the closest model for the new upload tests.

## Design

### `src/QrSimple.Api/Inspection.cs` (new)

```csharp
public class Inspection
{
    public Guid Id { get; set; }
    public required Guid EquipmentId { get; set; }
    public required string Kind { get; set; }              // one of InspectionKinds.All
    public required DateOnly InspectionDate { get; set; }  // operator-entered, decision 8
    public string? Note { get; set; }                      // optional, <=1000 chars
    public required byte[] Content { get; set; }           // non-nullable, decision 2
    public required string ContentType { get; set; }
    public required string FileName { get; set; }          // as uploaded; NOT what's served, see decision 19
    public required string UploadedByEmail { get; set; }   // admin-visible only, decision 11
    public required DateTimeOffset UploadedAtUtc { get; set; }
    public DateTimeOffset? LastEditedAtUtc { get; set; }   // null until first edit, decision 13
    public string? LastEditedByEmail { get; set; }
}
```

`DateOnly` maps to Postgres `date` under Npgsql with no extra configuration.

### `src/QrSimple.Api/InspectionKinds.cs` (new)

Mirror `Roles.cs` exactly — `const string` per kind, `static readonly string[] All`, `static bool IsKnown(string)`. Values per decision 6: `Weekly`, `Monthly`, `Quarterly`, `Annual`, `AdHoc` (displayed "Ad-hoc").

### `src/QrSimple.Api/BusinessTime.cs` (new)

Small static class implementing decision 24. Roughly:

- `static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ulaanbaatar");`
- `static DateOnly Today()` → `DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone).DateTime)`
- `static DateTimeOffset ToBusiness(DateTimeOffset utc)` for rendering stored timestamps.

Comment it with why (the ord/Mongolia gap) and with the multi-org path, so the next person doesn't "simplify" it back to `DateTime.Today`.

### `src/QrSimple.Api/InspectionCatalog.cs` (new)

Mirror `DocumentCatalog.cs`'s structure. `InspectionResult` abstract record with `ToHttpResult(onSuccess)`; cases `Success(Inspection)` / `NotFound` / `EquipmentNotFound` / `InvalidFile(string Reason)` / `InvalidRequest(string Reason)` (unknown `Kind`, future `InspectionDate`, over-long `Note` — all → 400) / `Forbidden(string Reason)` (retired equipment per decision 15; Operator editing someone else's record per decision 13).

Methods:

- `AddAsync(equipmentId, kind, inspectionDate, note, content, contentType, fileName, uploadedByEmail, db)` — validate file, kind, date (not in the future, per `BusinessTime.Today()`), note length; confirm equipment exists **and is not Retired**; insert with `UploadedAtUtc = DateTimeOffset.UtcNow`.
- `EditAsync(inspectionId, inspectionDate, note, callerEmail, callerRole, db)` — decision 13. Admin may edit any; Operator only where `UploadedByEmail == callerEmail`, else `Forbidden`. Sets `LastEditedAtUtc`/`LastEditedByEmail`. Never touches `Content`/`ContentType`/`FileName`.
- `ListAsync(equipmentId, db)` — `OrderByDescending(i => i.InspectionDate).ThenByDescending(i => i.UploadedAtUtc)`. **Project away `Content`** into a lightweight `InspectionListItem` record — otherwise listing 50 inspections pulls 50 PDFs into memory to render a page that displays none of the bytes. This is the one place the blob-storage decision actually bites; get it right.
- `CountAsync(equipmentId, db)` — for the scan-page panel.
- `GetContentAsync(id, db)` — the only path that loads `Content`.
- `DeleteAsync(id, db)` — Admin-gated at the endpoint/UI layer per decision 12.
- `static (IReadOnlyList<T> Recent, IReadOnlyList<T> Older) SplitByRecency<T>(IReadOnlyList<T> ordered, DateOnly today, Func<T, DateOnly> dateOf, int months = 6, int minimumRecent = 3)` — **pure function, no DB**, so decisions 16–17 are unit-testable without a container. Rules: an inspection dated exactly `today.AddMonths(-months)` counts as **recent** (inclusive); the recent set is never smaller than `minimumRecent` (decision 17); future-dated records can't exist (rejected at write time). Write tests for the exact boundary and for the minimum-recent override — that's where this will be wrong if it's wrong.

Reminder from AGENTS.md, applicable here: **a plain C# static method cannot appear inside a `db.Inspections.Where(...)` predicate** — EF throws at runtime, not compile time. Keep `InspectionKinds.IsKnown` and `SplitByRecency` on in-memory collections.

### `src/QrSimple.Api/DocumentCatalog.cs` (edit)

Add the inspection allowlist (`.pdf` / `application/pdf`) and `MaxInspectionBytes = 10 * 1024 * 1024`. `Validate`'s `bool isPhoto` now has three cases to express — replace it with an enum:

```csharp
public enum UploadKind { Photo, Document, Inspection }
public static string? Validate(string fileName, string contentType, long sizeBytes, UploadKind kind)
```

Call sites: two in `DocumentCatalog` (`AddUploadAsync`, `SetPhotoUploadAsync`), two in `EquipmentDetail.razor` (`OnPhotoFileSelected`, `OnDocumentFileSelected`), plus any in `DocumentUploadTests.cs`. Grep `isPhoto` to confirm that's the whole set. A separate `ValidateInspection(...)` is an acceptable fallback if the refactor churns more than expected, but the enum is the right shape — the boolean was already at its limit.

### `src/QrSimple.Api/AppDbContext.cs` (edit)

Add `public DbSet<Inspection> Inspections => Set<Inspection>();` and in `OnModelCreating`:

- `HasIndex(i => new { i.EquipmentId, i.InspectionDate })` — **this matters.** AGENTS.md notes `Document.EquipmentId` has no index; that's been fine because documents are few. Inspections grow without bound per equipment and every page load filters on exactly this pair.
- `Property(i => i.Note).HasMaxLength(1000)`.

No foreign key on `EquipmentId`, matching `Document` — there is no hard-delete for Equipment (only retire), so nothing to cascade. Recorded deliberately so a reviewer doesn't read it as an oversight.

Then `dotnet ef migrations add AddInspections` — see `docs/database-migrations.md`, and review the generated `Up`/`Down` before committing.

### `src/QrSimple.Api/PublicPageChrome.cs` (new)

`ScanPage.cs` and the new inspections page share the `<head>` block (tokens.css link, favicons, theme-color, viewport), the `.site-header` markup, and a base CSS block (`:root`, `body`, `.icon`, `.site-header`, `main`, `.panel`). Copy-pasting that into a second file guarantees the two public pages drift apart — exactly what `.claude/skills/qr-simple-ui/SKILL.md` exists to prevent.

Extract `HeadTags(string title)`, `Header()`, and `const string BaseStyles`. Both pages compose it and add page-specific CSS. Keep it genuinely small — shared *chrome*, not a template engine.

### `src/QrSimple.Api/ScanPage.cs` (edit)

- Signature becomes `Render(Equipment equipment, IReadOnlyList<Document> documents, int inspectionCount)`. This breaks existing `ScanPageTests.cs` call sites — expected, update them.
- Add a Lucide `clipboard-check` icon `const`, and the matching entry in `Components/Shared/Icon.razor` (that file's comment is explicit that an icon added on one surface belongs on the other).
- When `inspectionCount > 0`, render one more `<a class="panel document" href="/e/{id}/inspections">` inside the existing `.documents` nav, labelled `Inspection records ({inspectionCount})`, reusing the existing panel styling so it reads as a sibling of the manual/maintenance links. The count is the true total (decision 18). When the count is `0`, render nothing — don't send a technician into an empty page.
- The `.empty-documents` fallback currently triggers on `documentLinks.Length == 0`; it must now account for the inspection panel too, or equipment with inspections but no documents renders both the panel and "No documents are available."
- Swap the duplicated head/header/base CSS for `PublicPageChrome`.

### `src/QrSimple.Api/InspectionsPage.cs` (new)

`static string Render(Equipment equipment, IReadOnlyList<InspectionListItem> inspections, DateOnly today)` — same raw-string-literal approach as `ScanPage`, composed on `PublicPageChrome`. `today` is a parameter, not read inside, so the split is testable.

Structure:

1. Header chrome + a back link to `/e/{equipment.Id}` (`arrow-left` icon) — a technician who taps in must be able to get back without the browser's back button.
2. `<h1>` equipment name, with **name + serial number** as a subheading (decision 23).
3. Recent set from `SplitByRecency`. Each row: **inspection date** as the primary line; `Kind` as a small badge; the note below when present, rendered in full; and an "Open PDF" link to `/inspections/{id}/content` (`target="_blank" rel="noopener noreferrer"`, matching existing document links). **No uploader email and no edited indicator** (decisions 11, 13).
4. `<details><summary>Older inspections ({older.Count})</summary>` wrapping the same row markup for the remainder. Omit the whole `<details>` when `older` is empty. Style `summary` to look like the other panels and give it a visible `:focus-visible` outline — it is a real interactive control.
5. Empty state when there are no inspections at all, mirroring `.empty-documents`.

All colours/radii/shadows via `var(--brand-*)` from `tokens.css`. **No literal hex on this page** — per the UI skill that's a bug, not a style choice. HTML-encode every interpolated value (`WebUtility.HtmlEncode`), the note especially; `ScanPage`'s local `Encode` helper is the pattern.

### `src/QrSimple.Api/Program.cs` (edit)

```
GET    /e/{id}/inspections                         public, no auth
GET    /inspections/{id}/content                   public, no auth
POST   /equipment/{id}/inspections                 Admin + Operator
PUT    /inspections/{id}                           Admin + Operator (ownership checked in catalog)
DELETE /equipment/{id}/inspections/{inspectionId}  Admin only
```

The two public routes carry no `RequireAuthorization` and no `RequireRoleFilter` — decision 10, identical to `GET /documents/{id}/content` today. **Comment that explicitly**, the way the existing endpoint does, so it doesn't read as an omission.

`POST` takes `IFormFile file` plus `request.Form["kind"]`, `["inspectionDate"]`, `["note"]`, and `ClaimsPrincipal principal` for `FindFirstValue(ClaimTypes.Email)` → `uploadedByEmail`. Parse the date with `DateOnly.TryParse` on `CultureInfo.InvariantCulture`; return `InvalidRequest` rather than throwing.

`DELETE` uses `new RequireRoleFilter(Roles.Admin)` — **not** Admin+Operator like the documents equivalent (decision 12).

`GET /inspections/{id}/content` implements decision 19: build the name from equipment + kind + date, set `Content-Disposition: inline; filename="<ascii-sanitised>"; filename*=UTF-8''<rfc5987>`, and return the bytes. **Do not** pass `fileDownloadName` to `Results.File` — it forces `attachment` and stops phones displaying the PDF inline.

`GET /e/{id}` additionally calls `InspectionCatalog.CountAsync` for the new `Render` argument.

### `src/QrSimple.Api/Components/Pages/EquipmentInspections.razor` (new)

`@page "/app/equipment/{Id:guid}/inspections"`, `@attribute [Authorize]`, `@inherits RoleGatedComponentBase`, `AllowedRoles` = Admin + Operator + Reader (decision 14).

Resolve `CallerRole` **and `CallerEmail`** locally from `AuthenticationStateTask`, following `EquipmentDetail.razor`'s existing pattern (see Current state — the base class exposes neither).

Upload form, shown when `CanManageInspections` (Admin or Operator) **and the equipment is not Retired** (decision 15):
- `<InputFile accept=".pdf">` with a `@key` `Guid` field **bumped after every successful save** — a Blazor `InputFile`'s displayed filename is native browser state that mutating C# fields does not clear (AGENTS.md documents this).
- Validate via `DocumentUpload.Validate(..., UploadKind.Inspection)` **before** calling `OpenReadStream`, and pass `maxAllowedSize: DocumentUpload.MaxInspectionBytes` explicitly — the default is 512KB and it throws above that (also in AGENTS.md).
- `InputSelect` over `InspectionKinds.All`; `InputDate` defaulting to `BusinessTime.Today()`; `InputTextArea` for the note.
- Call `InspectionCatalog.AddAsync` in-process via the scoped `AppDbContext`, matching how other admin pages call catalogs directly rather than over HTTP.
- Submit button disabled while `IsBusy` (decision 22). Toast on success/failure via the injected `ToastService`.

Below it, the existing-inspections table: date, kind, note, **uploader email** (admin-side only — this is where decision 11's stored value surfaces), last-edited info when present, Open, Edit, Delete.
- **Edit** is inline (decision 21), exposing date + note only, shown when the caller is Admin or the record's uploader.
- **Delete** is Admin-only, behind `ConfirmDialog`, matching the document flow.

Finally, link to this page from `EquipmentDetail.razor`'s Documents section.

## Docs to update

- **`CONTEXT.md`** — add an **Inspection** entry to Language: a dated, noted, attributed PDF record of a periodic check, attached to Equipment, publicly readable via the scan page. Say how it differs from a Document (a Document is durable reference material *about* the machine; an Inspection is a point-in-time record of a check, and they accumulate). Add an _Avoid_ line steering off "audit," which means something else in this codebase.
- **`AGENTS.md`** — (a) a new `InspectionCatalog` bullet in the modules list, in the same detail register as the `DocumentCatalog` one: the public-by-design endpoints, the `Content`-projection requirement in `ListAsync`, the non-nullable `Content` contrast with `Document`, the Admin-only delete, and the `Content-Disposition: inline` trap; (b) a new environment/gotcha bullet for `BusinessTime` — why the app has a business timezone at all (ord vs. Mongolia), and that tzdata is confirmed present in the runtime image; (c) update the "Done" paragraph and its test count; (d) amend the v1 non-goals line so "audit/change history" is qualified rather than silently contradicted (see "Why").
- **`docs/plans/README.md`** — update the index row's Status when implementation finishes.
- **`.claude/skills/qr-simple-ui/SKILL.md`** — most likely to be missed. Both its YAML `description` and its "One product, two surfaces" heading name exactly two surfaces and hardcode `ScanPage.cs` / `/e/{id}`. There is now a third public page plus shared `PublicPageChrome`. Update the description, retitle the section, and note that shared public-page chrome lives in `PublicPageChrome.cs` so a future change lands in one place.

## Tests

New:

- **`InspectionCatalogTests.cs`** — `SplitByRecency` in isolation: empty; all-recent; all-older; mixed; the exact `today.AddMonths(-6)` boundary; and **the minimum-recent override** (decision 17 — e.g. five annual inspections, none within 6 months, must still surface 3 as recent). No container needed; cheapest and most valuable tests here.
- **`InspectionUploadTests.cs`** — modelled on `DocumentUploadTests.cs` / `TestUploads.cs`. Happy path; `.docx` rejected even though it's a valid *document* type (decision 4); over-10MB rejected; unknown `Kind` rejected; future `InspectionDate` rejected; upload to Retired equipment rejected (decision 15); 404 for unknown equipment id; `UploadedByEmail` captured from the authenticated caller; `ListAsync` ordered by inspection date not upload date (upload two out of order, assert the order flips).
- **`InspectionPermissionTests.cs`** — decisions 12–14, the ones most likely to regress: Operator `DELETE` → 403 while Admin succeeds; Operator can edit their own record; Operator editing another's → 403; Admin can edit any; Reader cannot upload, edit, or delete; anonymous → 401/redirect on all write routes.
- **`InspectionsPageTests.cs`** — `/e/{id}/inspections` renders 200 HTML **anonymously** (guards decision 10, the one most likely to be broken by a later well-meaning "add auth everywhere" change); a >6-month-old inspection appears inside `<details>` while a recent one does not; **the uploader email does NOT appear in the public HTML** (guards decision 11 — assert absence explicitly, it's the kind of thing a refactor reintroduces silently); note renders and is HTML-encoded (`<script>` in a note); equipment serial appears; empty state renders; the page still renders for Retired equipment.
- **Content-disposition** — one test asserting `GET /inspections/{id}/content` returns `inline` with the generated filename, not `attachment` (decision 19).

Changed:

- **`ScanPageTests.cs`** — every `ScanPage.Render(...)` call site gains `inspectionCount`. Add: the panel appears with the correct count; it's absent at zero; "No documents are available" does *not* render for equipment with inspections but no documents.
- **`DocumentUploadTests.cs`** — `isPhoto` → `UploadKind` at direct `Validate` call sites.
- **`AdminUiTests.cs`** — auth/role gating for `/app/equipment/{id}/inspections`, matching how the other admin pages are covered.

Run the suite **inside the devcontainer**. Scattered `System.IO.IOException` inotify failures are the documented host-level ceiling, not a regression — AGENTS.md covers it; rerun scoped with `--filter` rather than chasing it.

## Verification checklist

- [x] `dotnet build` clean; full suite green in the devcontainer (a scoped `--filter` rerun is acceptable evidence for inotify flakiness, per AGENTS.md). 112/112 green, no inotify flakiness hit.
- [x] `dotnet ef migrations add AddInspections` generated, `Up`/`Down` reviewed, applies cleanly on a database already carrying the three existing migrations. Applied cleanly from empty on every one of the 112 tests' fresh Testcontainers databases (`Database.Migrate()` runs on every `ApiFactory` startup).
- [x] `TimeZoneInfo.FindSystemTimeZoneById("Asia/Ulaanbaatar")` resolves at runtime in the devcontainer (it's confirmed present in the production image; confirm the dev environment too rather than discovering it in CI). Confirmed via a throwaway `dotnet run` in the devcontainer: resolves to `(UTC+08:00) Ulaanbaatar Standard Time`, converts `DateTimeOffset.UtcNow` correctly.
- [x] Live in a browser via the `qr-simple-ui` skill's Playwright loop — verified 2026-08-30, see Log entry below:
  - [x] Upload a PDF as **Operator** at `/app/equipment/{id}/inspections` — note, kind, and a backdated inspection date all persist and render.
  - [x] The `InputFile` picker clears after a successful upload (the `@key` bump actually works).
  - [x] Operator edits their own record's note/date successfully; the last-edited line appears admin-side.
  - [x] Operator sees **no Delete control**; an Admin does, and it works behind the confirm dialog.
  - [x] `/e/{id}` shows the new panel with the right count, styled as a sibling of the existing document links.
  - [x] `/e/{id}/inspections` in a **logged-out** context (incognito or cleared cookies) — list, PDF open, and back link all work with no login, and **no email address appears anywhere in the page source**. This is decisions 10 and 11 and cannot be verified while signed in.
  - [x] Opening a PDF on a phone-width viewport **displays inline** rather than triggering a download, and the saved name is the generated one (decision 19).
  - [x] Seed one inspection older than 6 months: it sits inside the collapsed `<details>`, the summary count is right, and it expands.
  - [x] Seed equipment whose only inspections are all older than 6 months: **3 still show as recent** (decision 17).
  - [x] Retire an equipment: its inspections page still loads and lists history, and the upload form is gone.
  - [x] Both pages at ~390×844 phone width.
  - [x] `browser_console_messages` clean.
  - [x] `getComputedStyle(document.documentElement).getPropertyValue("--brand-primary")` resolves on the new page — confirms `tokens.css` actually loaded rather than the page silently falling back to unstyled defaults.
- [x] Docs updated per "Docs to update", including the `qr-simple-ui` skill.
- [x] Status flipped to `Implemented` here and in `docs/plans/README.md`'s index row.

## Log

2026-08-29: Plan drafted against commit `6b3a47c`, then grilled with the project owner over three rounds. All 25 decisions above are settled as of this date. Notable outcomes of the grilling: the first draft published uploader email addresses on an anonymously-readable page (fixed, decision 11); gave Operators hard-delete over their own inspection records, defeating the provenance trail (fixed, decision 12) — which in turn opened the correction-path gap (decision 13); had no timezone handling despite a 13–14h server/user gap and no existing convention in the app to inherit (decision 24); and would have rendered an apparently-empty page for annually-inspected equipment (decision 17). The storage-growth section originally projected ~10GB/year from an assumed volume roughly 20× the real one — corrected once actual volume was confirmed at under 20 uploads/week. A public render cap introduced to bound page weight was then dropped as solving a non-problem (decision 18). Status set to Ready for implementation.

2026-08-29: Implemented inside the VS Code devcontainer (confirmed at the start: `user=vscode`, Ubuntu 24.04 image, `dotnet` baked in, `REMOTE_CONTAINERS=true`), against commit `d029631` (this plan doc's own commit — no further drift from `6b3a47c` besides the plan doc itself). All 25 Settled decisions implemented as written; none needed to be flagged as wrong.

**New files**: `Inspection.cs`, `InspectionKinds.cs`, `BusinessTime.cs`, `InspectionCatalog.cs`, `ContentDisposition.cs`, `PublicPageChrome.cs`, `InspectionsPage.cs`, `Components/Pages/EquipmentInspections.razor`, migration `20260829220015_AddInspections`. **Edited**: `DocumentCatalog.cs` (`isPhoto: bool` → `UploadKind` enum with a third `Inspection` case), `AppDbContext.cs`, `ScanPage.cs` (new `inspectionCount` parameter, inspection panel, chrome extracted to `PublicPageChrome`), `Program.cs` (5 new routes per the plan's table), `Components/Pages/EquipmentDetail.razor` (link to the new page, `UploadKind` call-site updates), `Components/Shared/Icon.razor` (`clipboard-check`, `arrow-left`).

**Where the Design section had drifted from actual code** (as warned — verified before writing, not assumed):
- The plan's "Current state" said `ScanPage.Render`'s signature change "breaks existing `ScanPageTests.cs` call sites — expected, update them." In the actual code, `ScanPageTests.cs` only ever exercises `ScanPage.Render` indirectly via `GET /e/{id}`, never calling the static method directly — so there were no call sites to fix. Signature change was still made exactly as specified; just no test breakage resulted.
- Same for `DocumentUploadTests.cs`: the plan expected direct `DocumentUpload.Validate(..., isPhoto: ...)` call sites there needing an `UploadKind` update. Grepped and found none — that file only exercises validation indirectly through HTTP. The two real call sites needing the enum swap were both in `DocumentCatalog.cs` itself plus two in `EquipmentDetail.razor`, exactly as the plan's fallback note anticipated ("Grep `isPhoto` to confirm that's the whole set").
- `ScanPage.cs`'s pre-existing CSS already had `grid-template-rows/columns: 1fr` and `min-width/min-height: 0` on `.photo-frame`/`.photo img` (from an unrelated prior session's photo-clipping fix, already committed). `PublicPageChrome` extraction only touched `:root`/`body`/`.icon`/`.site-header`/`main`/`.panel` per the plan's explicit list — the photo-frame fix was left untouched and unrelated.

**Test-writing gotcha hit (worth flagging for future SplitByRecency tests)**: my first `InspectionsPageTests` case for the recent/older `<details>` split used only 1 recent + 1 old record, expecting the old one to land inside `<details>`. It didn't — decision 17's minimum-recent-of-3 rule promoted the lone old record into the visible set (correctly; that's the point of the rule), so `<details>` never rendered and the test failed. Not a code bug — a test-design bug: any test asserting something *stays* in the older/collapsed bucket needs enough old records that promotion doesn't fully drain it (fixed by using 4 old records so only 2 remain after 2 get promoted to reach the minimum of 3). Worth remembering if this pattern shows up again.

**Tests**: 112 total (73 pre-existing + 39 new), all green — `InspectionCatalogTests.cs` (7, pure `SplitByRecency` unit tests incl. the exact 6-month boundary and minimum-recent override, no Testcontainers fixture), `InspectionUploadTests.cs` (12: happy path, `.docx` rejected, oversized rejected, unknown kind rejected, future date rejected, retired-equipment upload rejected, unknown-equipment 404, `UploadedByEmail` captured, list ordered by `InspectionDate` not upload order, inline `Content-Disposition` with generated filename, anonymous content access, unknown-inspection 404), `InspectionPermissionTests.cs` (6: Operator delete forbidden/Admin delete succeeds, Operator edits own record, Operator blocked from another's, Admin edits any, Reader blocked from all three write routes, anonymous 401 on all three), `InspectionsPageTests.cs` (10: anonymous view, serial number visible, uploader email absent from public HTML, recent/older split incl. the promotion edge case above, note HTML-encoded, empty state, retired equipment still renders, scan-page panel count/link present and absent-at-zero), plus one addition each to `ScanPageTests.cs` (no "no documents" message when only inspections exist) and `AdminUiTests.cs` (3: Operator sees upload form, Reader is read-only with no upload form, unregistered email redirected).

**Not verified in the 2026-08-29 session — live browser step blocked**: Playwright MCP and the `qr_simple` project MCP both showed `ConnectionRefused` for that entire session (checked via `ToolSearch`). Beyond that, the whole host stack was confirmed down by direct `curl`: `127.0.0.1:9222` (Chrome CDP), `127.0.0.1:8931` (Playwright MCP), `127.0.0.1:5078` (API), and `127.0.0.1:5432` (dev Postgres) all refused connections outright when checked from the devcontainer. Everything on the live-browser checklist was implemented and covered by the HTTP-level integration test suite, but not confirmed visually. Status was set to `Implemented, pending live browser verification` pending a session where the host stack was actually up.

2026-08-30: **Live browser verification completed** in a continuation of the same session, once the user brought the host stack up (`./scripts/start-chrome-for-playwright.sh`) and it was confirmed reachable. Two Chrome CDP dropouts occurred mid-session (Chrome exiting when Playwright closed its last tab) — resolved once the user reconfigured Chrome with `--keep-alive-for-test` under a supervisor loop; both times required only reconnecting after a few seconds, not a new Claude Code session, confirming the MCP-resolves-once gotcha only bites at *session* start, not at *Chrome-process* restart with the same MCP endpoint still listening.

Seeded test data directly into the dev Postgres database via a throwaway Npgsql console app (`Host=localhost;Port=5432`, credentials from `dotnet user-secrets`) rather than through the authenticated upload endpoint, because completing real Google OAuth requires credentials this session doesn't have and shouldn't handle — this exercises the render path (`InspectionsPage.Render`, `SplitByRecency`, `ScanPage`'s panel, `Content-Disposition`), not the write path, which the 12 `InspectionUploadTests` already cover at the HTTP level. Seeded: **Hydraulic Excavator EX-17** (7 records spanning the exact 6-month boundary, `2026-02-28` inclusive-recent vs `2026-02-27` older, business "today" independently confirmed as `2026-08-30` via `TZ=Asia/Ulaanbaatar date`, one day ahead of the UTC date — a live, incidental confirmation of decision 24's server/Mongolia gap); **Dewatering Pump P-08** (5 Annual records, all >6 months old, to exercise decision 17's promotion rule); **Transfer Conveyor CV-03** (Retired, 1 record, to check history-stays-visible).

All items on the checklist above were confirmed live and are checked off. Notable evidence, beyond "it rendered":
- Decision 17 promotion rule confirmed **both directions** on real data: the excavator's exact boundary (`02/28` recent, `02/27` older, to the day) and the pump's all-annual case (5 records, all >6 months old, yet exactly 3 — the 3 newest — render as Recent, 2 collapse into `<details>`, matching the "would otherwise look empty" failure mode decision 17 exists to prevent).
- Decision 11 (no email on the public page) confirmed by regex-scanning the full rendered `outerHTML` for `@`-containing strings — zero matches, not just "didn't spot one."
- Decision 19 confirmed at the HTTP level via `curl`, not just a screenshot: `content-disposition: inline; filename="Hydraulic Excavator EX-17-Weekly-2026-08-30.pdf"; filename*=UTF-8''...` — inline, generated name, not the uploaded `scan0001.pdf` and not a GUID. A screenshot of clicking "Open PDF" at 390×844 shows Chrome's built-in PDF viewer opening the file in a new tab in place, no download dialog.
- Decisions 12/13 (Operator: own-record edit only, no Delete; Admin: any-record edit, Delete works) confirmed **both in the UI and server-side**, signed in as each role in turn (`bedoux.hr@gmail.com` / Operator, then `tsoglog.uli@gmail.com` / Admin — switching required signing out of Google entirely via `accounts.google.com/Logout`, since the app's own `/login` silently re-authenticates the last-used account via `prompt=none` and never shows a chooser on its own). As Operator, direct `fetch()` calls to `PUT /inspections/{id}` on someone else's record and `DELETE /equipment/{id}/inspections/{id}` both returned `403` even though the UI already hid those controls — confirmed defense-in-depth, not just UI gating. As Admin, edited a record uploaded by `operator@example.com` (uploader stayed unchanged, `LastEditedByEmail`/`LastEditedAtUtc` correctly showed the editor) and deleted a test record through the real `ConfirmDialog` flow ("Delete this inspection record? The Annual inspection from 7/5/2026 will be permanently removed.").
- Decision 15 (retired equipment: history visible, no new uploads) confirmed **both** at the UI layer (upload form replaced by "This equipment is retired; new inspection records cannot be added.") **and** the server layer (a direct `fetch POST` to the retired conveyor's upload endpoint, signed in as Admin, still returned `403` with that exact message) — matching the plan's explicit "Enforce in InspectionCatalog... hide/disable the form in the UI — both, not just the UI."
- `InputFile` `@key` reset confirmed by the element's accessibility-tree ref changing after a successful upload and the Upload button reverting to disabled (file-required) state, without a page reload.
- Anonymous writes confirmed blocked via `curl` with no cookies at all: `302`/`401`/`302` on POST/PUT/DELETE, with a DB row-count check before/after to rule out a race where the request partially succeeded.

Seeded test data (7 excavator + 5 pump + 1 conveyor inspection rows, one of which was the Operator-uploaded/Admin-deleted record used for the upload/edit/delete checks) was left in the dev database; it's realistic PDF-bearing data consistent with the pre-existing "Sanity Test Loader"/"Upload Verify Loader"-style test fixtures already in that database, and can be removed on request.
