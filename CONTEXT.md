# QR Simple

A tool for mining field operators: generate QR codes for physical Equipment, scan them on-site to see quick info, and click through to fuller documentation.

## Language

**Equipment**:
The physical machine or asset tracked by the system. Has a Name, Category, Serial/Asset Number (unique), Site/Location (freeform text), Notes, and a Status (Active or Retired).
_Avoid_: Device, item, asset (unless a future context needs to distinguish these)

**QR Code**:
A durable pointer (`/e/{id}`) printed on a label and attached to a piece of Equipment. Encodes a URL, not raw data. Reprinting a lost or damaged label reuses the same code — it is never regenerated for the same Equipment. Still resolves for Retired equipment.
_Avoid_: Token, tag ID (the QR *is* the pointer, not a one-time credential)

**Document**:
A labeled file (e.g. "User Manual", "Safety Data Sheet", or the equipment photo) attached to a piece of Equipment for the "more details" flow. Uploaded directly and stored as bytes in Postgres — v1 hosts the file itself rather than linking out to wherever it already lives. The data model keeps room for a URL-based link mode alongside the upload fields for a possible future phase, but nothing in the app creates a URL-based Document today.
_Note_: **"Document" is the entity name, not what the admin UI calls it.** As of 2026-08-31 the generic list on `/app/equipment/{id}` is headed **"User Manual"** (and its upload control reads "User manual" / "Add user manual"). That was a copy rename only — the entity, the `Documents` table, `DocumentCatalog` and the `/equipment/{id}/documents` routes all keep the `Document` name, because the equipment photo and the OEM QA/QC report are stored as `Document` rows too and "user manual" would be wrong for them. The list still holds more than one file, so a Safety Data Sheet filed there appears under the "User Manual" heading.
_Avoid_: External link, hosted-elsewhere (v1 stores the bytes, it doesn't point elsewhere)

**Rebuild**:
A dated, noted record of a rebuild performed on a piece of Equipment, optionally backed by a PDF — a separate entity from Document, not a `Document` with a special label (see `docs/plans/0002-inspection-records.md` decision 1). A Document is durable reference material *about* the machine (a manual, a safety data sheet) that stays roughly static; a Rebuild is a point-in-time record of work that was actually done. The date and note *are* the record; the PDF is supporting evidence that may not exist yet when the record is filed, and can be attached later but never swapped. Rebuilds are rare — years apart — so the whole history renders flat, newest first, rather than collapsing older entries. Browsable publicly from the scan page with no login required, matching Document's exposure. Records who filed it and when (and who last edited it, if edited) — see the _Avoid_ note below.
_Was_: Inspection. This entity shipped as a periodic-inspection record (weekly/monthly/quarterly/annual) and was renamed when the product's actual use turned out to be rebuild history. The `Kind` cadence field went with the rename; the plan file keeps its original `0002-inspection-records.md` name as the historical record.
_Avoid_: Audit, audit trail (this codebase uses "audit/change history" to mean a general, cross-entity history feature that is an explicit v1 non-goal — see AGENTS.md's Status section. A Rebuild's provenance fields are a narrow, deliberate exception scoped to this one record type, not a step toward general auditing.), Overhaul, service record

**OEM QA/QC report**:
The manufacturer's quality-assurance document for a piece of Equipment. Exactly one per Equipment, PDF only. Stored as a `Document` under the reserved label `"OEM QA/QC Report"` rather than as its own entity — it is durable reference material like a manual, and the only thing that sets it apart is the one-per-Equipment rule, which `DocumentCatalog.SetOemReportUploadAsync` enforces by overwriting in place. Gets its own section in the admin UI and its own panel on the scan page, so it is filtered out of the generic document list on both (`DocumentCatalog.IsReservedLabel`) — the equipment photo works the same way. (That generic list is the one headed "User Manual" in the admin UI; see the _Note_ under **Document** above.)
_Avoid_: Certificate, compliance doc (this is the OEM's own QA/QC report, not a certification the operator holds)

**Organization**:
The tenant boundary. Every Equipment and User belongs to exactly one Organization. Modeled from day one even though v1 only ever has a single Organization, so future multi-company support doesn't require a schema migration.
_Avoid_: Company, tenant, account

**Status** (Equipment):
Active or Retired. Retired equipment keeps its record and QR code (so old tags don't become dead links) but is hidden from default equipment lists.
_Avoid_: Deleted, archived (nothing is ever hard-deleted)

## Roles

**Admin**:
Manages Users, bulk CSV import, Equipment, and the Category list within their Organization.

**Operator**:
Adds and edits Equipment (single entry or bulk CSV import), scans QR codes.

**Reader**:
Logged-in, read-only access to the full Equipment list/dashboard. Distinct from an anonymous scanner: a Reader can browse and search across all Equipment, not just view one scanned record.

**Public scanner**:
Anyone who scans a QR code, with no account. Sees the quick-info page for that one Equipment record only. Not a role with a User record — this is the unauthenticated default.

## Category

A managed list of Equipment types (e.g. Pump, Conveyor, Drill) that Admins can extend. Not fully freeform — prevents data fragmenting into "Pump" / "pump" / "Pumps" variants.
