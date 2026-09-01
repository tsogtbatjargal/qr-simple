# Plan 0001: photo/document file upload for Equipment

Status: Implemented

## Why

`/app/equipment/{id}`'s Photo and Documents sections previously took a **URL** — the admin pasted a link to wherever the file already lived (SharePoint, Google Drive, etc.). `CONTEXT.md` and `AGENTS.md` both documented that as a deliberate v1 boundary ("v1 does not host files itself" / "built-in file upload/hosting for Documents" was a non-goal from issue #1).

This plan reverses that decision: admins can **upload** the photo/document file directly instead of hosting it elsewhere first. Reached after a grilling session with the project owner — the settled decisions are below. Scoped as **upload-only for now**; a future phase may add a separate "link to internal storage" mode for internal docs, which is why the data model keeps room for `Url` alongside the new upload fields rather than deleting it.

## Settled decisions

1. **Storage**: file bytes stored as a blob in Postgres (`Content: byte[]`), not local disk or cloud object storage. Reasoning: no Fly.io volume is configured, and the app isn't deployed yet, so local disk isn't durable across deploys; cloud storage is premature for a "for now" feature.
2. **Data model**: `Document.Url` becomes nullable and stays in the model for a future link-only mode; new nullable `Content: byte[]?`, `ContentType: string?`, `FileName: string?` columns are added. A row is either URL-based (`Url` set) or upload-based (`Content` set) — inferred from which is non-null, **no new discriminator column**.
3. **Same mechanism for both**: the single Photo section and the generic Documents section both switch to file upload, not just one of them.
4. **File constraints**: photos — `.jpg`/`.jpeg`/`.png`/`.webp`, 5MB cap. Documents — `.pdf`/`.doc`/`.docx`/`.xls`/`.xlsx`, 20MB cap. Validated by extension + browser-reported `ContentType` only (no magic-byte sniffing — internal tool, trusted Admin/Operator roles).
5. **No per-equipment document count cap** — unbounded, same as before.
6. **UI**: the URL text field is removed from both forms and replaced by a file picker. (`Url` capability stays in the data model, just unused by the UI for now.)
7. **Documents section Label**: auto-derived from the uploaded filename — no manual Label text input anymore.
8. **Content serving**: new `GET /documents/{id}/content` endpoint, **no auth**, streams the bytes with the stored `ContentType`. This matches the prior exposure model — a `Document.Url` could already point anywhere public, and the public `/e/{id}` scan page needs to load photos/docs without a login, so this doesn't reduce privacy, it just moves the bytes in-house.
9. **API surface**: the old `POST /equipment/{id}/documents` endpoint accepted JSON `{Label, Url}`. That JSON/URL creation path is **removed**, not just unused — it'll be redesigned properly when the future link-only mode is built, rather than leaving a half-tested path lying around.
10. **Docs**: `CONTEXT.md`'s Document definition and `AGENTS.md`'s non-goals list both get updated to stop stating something the app no longer does.

## Current state (as of planning, 2026-08-17 — see `AGENTS.md`'s `DocumentCatalog` bullet for the as-built shape)

- `Document.cs`: `{ Id, EquipmentId, Label, Url }` — all required, no `ContentType`/`Content`/`FileName`.
- `DocumentCatalog.cs`: `AddAsync(equipmentId, label, url, db)` (generic insert), `SetPhotoAsync(equipmentId, url, db)` (overwrites the existing photo-labeled row's `Url` in place — the *only* place the one-photo-per-equipment invariant was enforced), `DeleteAsync(id, db)`, `ListAsync(equipmentId, db)`. Photo-ness inferred by `IsPhotoLabel(label)` string-matching — no boolean/discriminator column.
- `Program.cs`: `POST /equipment/{id}/documents` took `AddDocumentRequest(string Label, string Url)` as JSON. `DELETE /equipment/{id}/documents/{documentId}` ignores its own `{id}` path segment (pre-existing, out of scope here). Both `RequireRoleFilter(Roles.Admin, Roles.Operator)`.
- **Important**: `EquipmentDetail.razor`'s Blazor code-behind does not call the HTTP endpoints above — it calls `DocumentCatalog` methods directly in-process via `IDbContextFactory<AppDbContext>` (see `AGENTS.md`: "Pages call the modules above in-process ... rather than the JSON HTTP endpoints"). The HTTP endpoint is a separate API surface, used only by tests and any external caller. The Blazor upload UI does not need to round-trip through the HTTP multipart endpoint.
- `ScanPage.cs`: rendered `photo.Url`/`document.Url` directly into `<img src>`/`<a href>`, HTML-encoded only, no rewriting.
- Existing precedent for `IFormFile` + multipart in this codebase: `POST /equipment/import` (CSV bulk import) — `.DisableAntiforgery()`.
- Zero prior usage of Blazor's `InputFile`/`IBrowserFile` anywhere in the codebase before this plan.

## Design

See the "as built" description in `AGENTS.md`'s `DocumentCatalog` bullet and Blazor-gotchas list — it now documents the final shape (`AddUploadAsync`/`SetPhotoUploadAsync`, `DocumentUpload.Validate`, `GET /documents/{id}/content`, the `@key`-based `InputFile` reset trick, the EF-translation gotcha with `IsPhotoLabel` in a `Where`) in more accurate detail than the original sketch here would after the fact. Original design sketch, for planning-session context:

### 1. `Document.cs`

```csharp
public class Document
{
    public Guid Id { get; set; }
    public required Guid EquipmentId { get; set; }
    public required string Label { get; set; }
    public string? Url { get; set; }          // now nullable
    public byte[]? Content { get; set; }       // new
    public string? ContentType { get; set; }   // new
    public string? FileName { get; set; }      // new
}
```

### 2. EF Core migration

`Url` becomes nullable; add `Content` (bytea), `ContentType`, `FileName` (all nullable). No FK/index changes, no check constraint enforcing "exactly one of Url/Content" — trust application logic per decision #2.

```
dotnet tool restore   # once, if not already done
dotnet dotnet-ef migrations add AddDocumentFileUpload --project src/QrSimple.Api --startup-project src/QrSimple.Api
```

No production data existed yet, so no backfill concern.

### 3. `DocumentCatalog.cs`

Validation constants (extensions/content-types/size caps per decision #4) plus `AddUploadAsync(...)` and `SetPhotoUploadAsync(...)` mirroring the old URL-based methods but taking file bytes; new `DocumentResult.InvalidFile(string Reason)` case for validation failures.

### 4. `Program.cs`

Replace `POST /equipment/{id}/documents`'s JSON contract with multipart `IFormFile`, following the `/equipment/import` pattern. Add `GET /documents/{id}/content` (no auth). Leave `DELETE` unchanged.

### 5. `EquipmentDetail.razor`

Replace both URL text inputs with `InputFile` pickers; read bytes via `IBrowserFile.OpenReadStream(maxAllowedSize: ...)` in-process and call the new catalog methods directly. Documents section: Label auto-derived from filename.

### 6. `ScanPage.cs`

Branch photo/document rendering on `Content != null` (serve via the new endpoint) vs `Url != null` (old behavior).

### 7. Docs

`CONTEXT.md`'s Document definition and `AGENTS.md`'s non-goals list both said something that stopped being true — update both.

### 8. Tests

Tests seeding a photo/document via the JSON `{Label, Url}` POST body needed to move to multipart uploads or direct catalog calls.

## Verification checklist

1. `dotnet build` clean.
2. Full test suite passes inside the devcontainer.
3. Live-tested in the browser via Playwright MCP (per the `qr-simple-ui` skill): upload/replace/remove a photo (still one row, not two), add/delete a document, confirm the reserved-photo-label guard's upload-era equivalent still blocks a second photo through the generic form, confirm the public `/e/{id}` scan page renders correctly for both Active and Retired equipment.
4. `CONTEXT.md` and `AGENTS.md` updated.

## Log

2026-08-17: Plan drafted and grilled with the project owner (`/grilling` session) — decisions above settled with the user picking the recommended option on every question. Handed off via a copy-paste prompt to an agent session running inside the devcontainer.
2026-08-17: Implemented. Working tree shows `Document.cs`, `DocumentCatalog.cs`, `Program.cs`, `EquipmentDetail.razor`, `ScanPage.cs` changed; new migration `20260817213350_AddDocumentFileUpload`; new tests `DocumentUploadTests.cs`/`TestUploads.cs`; `AdminUiTests.cs`/`RoleGatingTests.cs`/`ScanPageTests.cs` updated; `AGENTS.md`/`CONTEXT.md` updated. `AGENTS.md`'s `DocumentCatalog` bullet and Blazor-gotchas list now carry the detailed as-built record (including gotchas hit: an old shared-dev-DB row with `Url` set and `Content` null needing every render site to branch correctly, an EF LINQ-translation error from calling `IsPhotoLabel` inside a `Where`, and the native `InputFile` picker not clearing itself after a successful upload without a `@key` bump) — this plan's own Log does not duplicate that detail, see `AGENTS.md` for it. Build/test verification and the live-browser checklist above were not independently re-run from this coordinating session; take the "Implemented" status as reported by the implementing work, not as independently re-verified here.
2026-08-17 (later same day): Build/test/live-browser verification independently completed in the devcontainer. `dotnet build` clean (0 warnings/errors). Full suite: 73/73 passing (up from 65 pre-plan; +8 new: `DocumentUploadTests.cs` x7, `RoleGatingTests.Reader_cannot_upload_an_equipment_document`). Migration applied cleanly to the shared local dev Postgres on a real `dotnet run --launch-profile https` restart (`ALTER TABLE "Documents" ALTER COLUMN "Url" DROP NOT NULL` + three `ADD COLUMN`s, no data loss). Live-verified in the visible Chrome via Playwright MCP against `Dewatering Pump P-08` (existing equipment, pre-migration `Url`-based documents) and a fresh `Upload Verify Loader` test equipment: uploaded/replaced/removed a photo (replace confirmed same `Document.Id` before/after both via DOM and a direct `curl` of `/documents/{id}/content` showing the new 2x2 PNG bytes — genuinely one row, not two); added/deleted a document (byte-for-byte + `Content-Type` verified via `curl`); the reserved-photo-label guard blocked uploading a file literally named "Equipment Photo.pdf" through the generic Documents form with a clear inline error and no stray row; confirmed the public `/e/{id}` scan page renders the uploaded photo (`naturalWidth` "1" matching the 1x1 test PNG, confirmed loaded not broken) and document link for both Active and Retired status (retired via the real Retire flow, confirm dialog and "no longer in service" notice both correct); phone-width (390px) responsive check passed on both surfaces with the documented `@media (max-width: 600px)` rules engaging correctly; zero new browser console errors. All test equipment/documents cleaned up afterward except the `Upload Verify Loader` shell record, left Retired with no photo/documents (equipment has no hard-delete in this app — same throwaway-fixture pattern as the pre-existing `Sanity Test Loader`/`QA-TEST-001` record). One real bug found and fixed during this pass, not caught by the automated suite: `EquipmentDetail.razor` originally linked every document unconditionally to `/documents/{id}/content`, 404ing for the shared dev DB's pre-migration `Url`-based rows (`ScanPage.cs` already branched correctly, the admin page didn't) — fixed with a `DocumentHref(document)` helper mirroring `ScanPage.Render`'s branch, now documented in `AGENTS.md`'s `DocumentCatalog` bullet. Also hit and worked around two Playwright/MCP environment quirks (not app bugs, not yet in `AGENTS.md`/`local-browser-testing.md`): `browser_click` intermittently timed out on Blazor-rendered buttons/inputs despite them being visible/enabled (worked around via `browser_evaluate` + native `.click()`); `browser_file_upload` needs the container-internal `/output/...` path, not the devcontainer's `/workspaces/qr-simple/.playwright-mcp-output/...` path, even though the latter is listed as an "allowed root" in the tool's own error message.

2026-08-31: The admin UI section this plan calls "Documents" was renamed to **"User Manual"** (heading, upload-field label, submit button, toast, empty state and delete-confirm title). Copy only — decision 3 (same upload mechanism for the photo and the generic list), decision 7 (label auto-derived from the filename), decision 8 (`GET /documents/{id}/content`, no auth) and decision 9 (multipart-only creation) all still hold exactly as written, and no entity, table, catalog method or route was renamed. The list also stays a *list*: a maintenance instruction or safety data sheet still sits under that heading beside the manual. The scan page deliberately did **not** follow: its `aria-label="Equipment documents"` and its "No documents are available for this equipment." empty state cover the manual *plus* the OEM QA/QC report panel *plus* the rebuild-history link, so narrowing either to "user manual" would report no manual when what is actually missing is all three — see the comment at `ScanPage.cs`'s `documentsSection`. Two `AdminUiTests` assertions had to be strengthened rather than search-replaced: they asserted `Contains("User manual")` to prove a document row rendered, which "User manual" being the new upload-field label would have satisfied on its own, so both now pin the row's `/documents/{id}/content` link. 138/138 passing.

2026-09-01: The section renamed to "User Manual" yesterday is now **"Manuals"** — heading `Manuals`, upload label `Manual file`, button `Add manual`, empty state `No manuals yet.`, delete-confirm `Delete this manual?`, toast `Manual added.`. The singular lasted one day because it described the section wrongly: this list takes the user manual *and* the maintenance manual *and* anything of that kind, which is the same "it is still a *list*" caveat the 2026-08-31 entry above had to add in prose. The plural says it in the heading instead. Copy only again — decisions 3, 7, 8 and 9 are untouched, no entity/table/catalog method/route renamed, and the scan page still deliberately does not follow (same collective-wording reason, comment still at `ScanPage.cs`'s `documentsSection`, now with the extra date). **`ScanPage.cs`'s `OrderBy` was deliberately left alone**: its `"User manual"`/`"Maintenance instruction"` strings are `Document.Label` data that existing rows carry, not UI copy, so renaming them would have dropped every existing "User manual" row to the bottom of the scan page's document order with no visible cause — there is now a comment at that `OrderBy` saying so, since a third rename pass would otherwise be very likely to sweep them up. `AdminUiTests`' two heading assertions moved to `"Manuals"`/`"Add manual"`; the row-rendering proof stays pinned to `/documents/{id}/content` for the reason the previous entry gives, and its comment was corrected — it blamed the collision on the upload field's label, when the actual collision is with this test's own upload label `"User manual"`, which is still valid data after the rename. 138/138 passing.

2026-09-01: Unrelated to this plan's decisions, same sitting, recorded here because it changes how this page's form renders: **every `<label>` in the admin UI is now a two-column grid** (`--label-col` 132px + `--label-gap` 18px, the same measures `<dl>` uses), so a form field's input starts at the same x as a read-only row's value instead of wherever its label text happened to end — "Name" and "Serial number" previously put their inputs about 40px apart. `align-items: baseline` lines the label up with the text inside the field, which also keeps the rebuild Note's label at the top of its textarea rather than floating half-way down. `.validation-message` is indented into the field column to match, and both stack back to one column under the existing `@media (max-width: 600px)`. This relies on every label in the app being "text, then exactly one control" — `.checkbox-label` is the sole opt-out, via its own class rule. Also on this page, the Retire/Reactivate row moved to the right edge (`.button-row-end`): it sat two rows straight below the edit form's Save button in the same column, which is a mis-click from Save onto a destructive action.

2026-09-01: **Correction to the entry above, found in the browser, not in review.** Sharing `--label-col` between `<label>` and `<dl>` did *not* line the two up, and the entry above (and a code comment on this page) claimed it did. A form sits inside `.form-card`, which insets its content by `clamp(16px, 3vw, 24px)` of padding plus a 1px border; a bare `<dl>` sits flush on the page background. So the equipment detail page's Status row rendered 25px to the left of the four inputs directly above it — the exact stagger the shared column was added to remove. The card padding is now the `--card-pad` token, `dl` carries `padding-inline: calc(var(--card-pad) + 1px)` to match it, and `.card dl, .form-card dl, .table-card dl` resets that to `0` so a list *inside* a card is not indented twice (no such list exists today; the rule is there because the next one added would be silently wrong without it). Fixing it on `dl` rather than on the one page fixes the Operator view's read-only Serial number, the Reader view's whole list, and `/app/users/{id}`'s meta line at the same time, none of which needed a card wrapper.

Second correction, same cause — a claim checked against the rendering instead of the source. `<ValidationSummary>` renders its errors as `<li class="validation-message">` inside `<ul class="validation-errors">`, i.e. **the same class as the per-field message**. The field-column indent above therefore landed on the summary too, on top of the `<ul>`'s own padding, putting whole-form errors ~150px in from the card edge where they read as belonging to some field to their left. Scoped off with `.validation-errors .validation-message { margin-left: 0 }`. The per-field indent is unchanged and still drops to 0 at the phone breakpoint.

Both fixes verified in a browser: on `/app/equipment/{id}` the four inputs and the Status `<dd>` now report an identical `getBoundingClientRect().left`, and the `<dt>` column starts exactly where a sibling `.card`'s content starts; summary bullets sit at the form's left edge on all five pages that can actually populate one, while per-field messages stay under their input. The Operator and Reader views were reasoned about from the markup rather than observed — there is no way to render a second role in a live browser session, since `TestAuthHandler` exists only inside `ApiFactory` for the test suite — but both use the identical bare-`<dl>` shape the fix keys on, with no role-dependent CSS anywhere in the path.

Two pre-existing quirks surfaced by that pass, neither caused by this work and neither fixed: `/app/users/{id}`'s Role `<select>` has no blank option, so its `[Required]` can never fail through the UI; and the rebuild table's inline-edit form (`EquipmentRebuilds.razor`) binds to an `EditRebuildModel` carrying no DataAnnotations at all, so its `<ValidationSummary>` is dead markup and "Note is required." arrives from `RebuildCatalog.EditAsync` as a plain `<p class="error">` instead. Anyone tidying that should expect the validation styling to start applying where it currently doesn't.
