# Plan 0003: Pluggable authentication provider (OIDC alongside Google)

Status: Ready for implementation

## Why

Sign-in is hardcoded to Google. `Program.cs` registers exactly one external scheme (`.AddGoogle(...)`), and `/login` challenges it by name. A prospective customer running on Microsoft 365 cannot use this product at all, and "sign in with Google" on a corporate laptop reads as unserious to an enterprise IT department.

The immediate driver is **a demo for a real prospect**, not a signed customer. The goal is a genuine end-to-end Microsoft sign-in they can watch work — ideally with their own work account — not a mocked button.

This plan reverses or corrects four things existing docs assert:

- **`AGENTS.md`'s v1 non-goals list includes "non-Google sign-in."** That is exactly what this adds. Amend the line rather than deleting it, noting it was reversed by this plan.
- **`docs/adr/0001-organization-modeled-from-day-one.md` and `CONTEXT.md` both state that `Organization` is a modeled entity and that every Equipment and User references one. This is false.** There is no `Organization.cs`, no `OrganizationId` column anywhere, no `DbSet<Organization>`, and no mention of it across any of the four migrations. The only surviving trace is a comment in `BusinessTime.cs:9-12` that reasons from the ADR as though the table existed. The ADR's stated purpose was to avoid an expensive backfill later; the backfill was never avoided because the work was never done.
- **The multi-tenancy strategy that ADR 0001 assumed is being replaced** (decision 2): per-customer deployments rather than one shared instance. That makes the missing `Organization` table moot rather than urgent, but the docs still need to stop describing code that does not exist.
- **`CONTEXT.md`'s Document/Inspection sections are unaffected.** Nothing here touches attachments.

## Settled decisions

Settled with the project owner across four grilling rounds on 2026-08-29. Each is something a reasonable implementer could otherwise guess differently. **Do not silently change any of them** — if one turns out to be wrong once you are in the code, stop and flag it.

### Deployment and tenancy

1. **This is anticipatory work for a demo, not a delivery to a paying customer.** Prefer the smallest thing that genuinely works end-to-end over speculative generality. Nothing here should be built "because a future customer might."

2. **Each customer gets their own deployment — separate Fly app, separate Postgres.** Tenancy is a deployment concern, not a schema concern. There is no shared multi-tenant instance, no tenant-scoped queries, and no per-tenant configuration in the database.

3. **`Organization` is NOT implemented, now or as part of this plan.** Decision 2 removes the reason it existed. Do not add the entity, the column, or the migration. ADR 0001 is superseded by a new ADR (decision 19) rather than being retroactively honoured.

4. **User provisioning is unchanged: invite-only.** An Admin creates each user by email via `UserCatalog.CreateAsync`; nobody self-registers, and there is no domain-based auto-provisioning. This is the property that makes a hostile or mistaken identity assertion land on a 403 instead of an account, and it is deliberately being kept.

### Provider selection and configuration

5. **Generic OIDC, not a Microsoft-specific integration.** Use `Microsoft.AspNetCore.Authentication.OpenIdConnect` (`AddOpenIdConnect`) pointed at an authority URL, configured for Entra first. One code path then also covers Okta, Auth0, and Keycloak with nothing but different config. Do not take a dependency on `Microsoft.Identity.Web` or any Entra-specific SDK.

6. **Exactly one external authentication scheme is registered per deployment.** Not one *offered* out of several registered — one *registered*. A deployment is either Google or OIDC, never both. This keeps `/login` a bare challenge with no provider-picker page (none exists today), and it is the assumption that decision 10 depends on.

7. **The provider is chosen by an explicit `Authentication:Provider` config key** (`"Google"` or `"Oidc"`), not inferred from which settings happen to be populated. **An absent, empty, or unrecognised value must throw at application startup.** Inference or a silent default is unacceptable here: a typo in a customer's config would otherwise boot a healthy-looking app that accepts *our* Google identity provider instead of theirs. A crash on boot is the correct failure mode.

8. **SAML 2.0 and local email+password accounts are explicitly out of scope.** Add them only when a paying customer names one as a condition of sale.

### Identity and claims

9. **Email remains the identity primary key.** No `Provider` or `ProviderSubjectId` column, no schema change to `User`. Decision 6 makes email safe: with a single registered scheme, only one issuer can ever assert a claim.

10. **Record that invariant in code, next to the thing it protects.** A comment on `UserAuthorization.FindAsync` stating: *exactly one external authentication scheme is registered per deployment (see plan 0003 decision 6); email is a safe identity key only because of that. Registering a second scheme without also binding users to a provider would let either issuer assert any email.* This note is the entire mitigation — it is what a future implementer needs to see before breaking the assumption.

11. **Map the email claim defensively, and fail closed.** Google always emits `ClaimTypes.Email`; **Entra frequently does not** — a work account typically carries `preferred_username` (the UPN), and `email` appears only if the app registration adds it as an optional claim or the directory account has a `mail` attribute. Since every authorization decision keys on `ClaimTypes.Email`, an absent claim silently 403s every user. Resolve in this order: `email` → `preferred_username`. **If neither is present, fail the sign-in with an explicit error** rather than admitting a principal with no email, which would surface as an inexplicable 403 in front of a customer. Request the `email` scope and configure the optional claim in the app registration as well — the fallback is a safety net, not the plan.

12. **Normalize email to lowercase on both write and lookup.** Entra returns the UPN with whatever casing the directory holds, so `Tsogt.B@company.com` is entirely possible; an Admin who invited `tsogt.b@company.com` would create a row that never matches. Lowercase in `UserCatalog.CreateAsync` and in `UserAuthorization.FindAsync`. Note the codebase is currently inconsistent about this: `InspectionCatalog.cs:124` and `EquipmentInspections.razor:168` already compare emails with `OrdinalIgnoreCase`, but the identity lookup that gates every request does not. **Before shipping, check the live `Users` table for rows differing only by case** — the unique index would block the migration. If any exist, stop and ask rather than picking a winner.

### Demo environment

13. **Create a free Microsoft Entra tenant** for development and demos. It is free, takes about an hour of portal work, and becomes the permanent OIDC test fixture afterwards.

14. **Register the application as multi-tenant** ("accounts in any organizational directory"), so the prospect can sign in with their *own* real work account — pre-invited as an Operator per decision 4 — and land in the app already provisioned. That is the most persuasive thirty seconds available in this demo. **Known risk:** enterprises often require tenant-admin consent before an unfamiliar third-party app can be used, and a mining company's IT may have that enabled, in which case this fails live. **Mitigation: keep a working account in our own test tenant ready as a fallback**, and if a friendly contact there can test the sign-in a day early, do that.

15. **The demo runs on a second Fly app (`qr-simple-demo`) with its own Postgres.** Decision 6 forces this: production authenticates via Google and therefore cannot also offer Entra. Do not reconfigure production, even temporarily. Set that app's `PublicBaseUrl` to its own hostname so generated QR codes resolve against the demo, not production.

16. **Administer the demo app with an Entra account in our own test tenant.** Since that deployment is OIDC-only, the usual Google login will not work there — seed at least one user from the test tenant as Admin in the demo database.

17. **Seed generic mining equipment, plus inspection records.** The prospect's real fleet names would be better, but they are not known. Seed inspections as well as equipment — the public inspection list from plan 0002 is the part a field technician actually uses, and it demos better than the equipment page alone. The existing dev fixtures have a usable date spread to mirror (7 rows spanning to today, 5 rows all older than six months to exercise the promotion rule, 1 single old row).

18. **The demo app stays up indefinitely**, with Fly machines set to auto-stop when idle so it costs effectively nothing. It doubles as the OIDC integration environment.

### Scope boundaries

19. **Write ADR 0003 superseding ADR 0001**, and correct `CONTEXT.md` and the `BusinessTime.cs` comment to describe what the code actually does. Do not edit ADR 0001's text — supersede it, per the folder's own convention that an ADR is a permanent record.

20. **Sign-out stays local-cookie-only.** `/logout` continues to clear only the app cookie and does not perform RP-initiated federated logout. Federated logout would sign the user out of their entire Microsoft session on that device — Teams, Outlook — which users experience as hostile. **Known consequence, already documented in `docs/local-browser-testing.md`:** the IdP silently re-authenticates via `prompt=none`, so a working sign-out looks broken, and on a shared device the next person may land in the previous user's session. Accepted for now; revisit if Operators turn out to use shared field tablets.

21. **No customer onboarding runbook in this plan.** Demo-grade is the target (decision 1). The "how does a customer's IT register this in their own Entra" document is the first thing to write when a real sale is in progress — not now.

22. **No mock OIDC server in the test suite.** Test the logic we own, not the framework's handshake — see Tests below.

## Current state

Verified against the code on 2026-08-29. **Re-verify before implementing** — this may have drifted.

- **`src/QrSimple.Api/Program.cs:23-32`** — `AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o => o.LoginPath = "/login").AddGoogle(o => { ClientId/ClientSecret from Authentication:Google:* })`. Both credentials fall back to `""` when unset, so the app currently boots fine with no Google config at all and fails only at challenge time.
- **`Program.cs:88-91`** — `/login` is a bare endpoint: `Results.Challenge(new AuthenticationProperties { RedirectUri = returnUrl ?? "/app" }, [GoogleDefaults.AuthenticationScheme])`. There is **no login page component** anywhere.
- **`Program.cs:93-97`** — `/logout` calls `SignOutAsync` on the cookie scheme only, then redirects to `/app`.
- **`src/QrSimple.Api/UserAuthorization.cs:8`** — `db.Users.FirstOrDefaultAsync(u => u.Email == email)`. Case-**sensitive** in Postgres. This is the single lookup behind every authorization decision.
- **`src/QrSimple.Api/RequireRoleFilter.cs:12`** — reads `ClaimTypes.Email` off `HttpContext.User`, then calls the above. Blazor pages re-resolve the same way via `AuthenticationStateTask`.
- **`src/QrSimple.Api/UserCatalog.cs`** — `CreateAsync(email, role, db)` stores `email` verbatim, no trim, no case normalization.
- **`src/QrSimple.Api/User.cs`** — `Id`, `Email`, `Role`, `IsActive`. No provider fields. Unique index on `Email` (`AppDbContext.cs:19-21`).
- **`src/QrSimple.Api/Components/Layout/MainLayout.razor:24`** — hardcoded `<a class="btn btn-primary" href="/login"><Icon Name="log-in" />Sign in with Google</a>`.
- **`src/QrSimple.Api/QrSimple.Api.csproj:26`** — references `Microsoft.AspNetCore.Authentication.Google` 10.0.11. **`Microsoft.AspNetCore.Authentication.OpenIdConnect` is not referenced** and must be added at the same version.
- **`src/QrSimple.Api/appsettings.json:10-15`** — `Authentication:Google:{ClientId,ClientSecret}`, both `"REPLACE_ME"`. Real values come from user-secrets locally and Fly secrets in production.
- **`tests/QrSimple.Api.Tests/ApiFactory.cs:28-29` and `TestAuthHandler.cs`** — the test host replaces the default scheme with `TestAuthHandler`, which reads an email from a request header and builds a principal. **No test exercises a real external handshake**, and none should need to change as a result of this plan.
- **Migrations present:** `InitialCreate`, `AddUserIsActiveAndEmailIndex`, `AddDocumentFileUpload`, `AddInspections`. None reference `Organization`.

## Design

Sketch, not code to paste — verify each file against reality first.

### `QrSimple.Api.csproj`

Add `Microsoft.AspNetCore.Authentication.OpenIdConnect` pinned to `10.0.11`, matching the Google package. The csproj already carries a comment about keeping the net10.0 package versions bumped together; this joins that set.

### `AuthenticationSetup.cs` (new)

Shaped like `Roles.cs` — a small static class holding the fixed provider list and the registration logic, so `Program.cs` stays a wiring file.

- `public const string Google = "Google";` / `public const string Oidc = "Oidc";`, plus an `All` array and an `IsKnown` helper.
- An extension method (e.g. `AddConfiguredAuthentication(this IServiceCollection, IConfiguration)`) that:
  - reads `Authentication:Provider`;
  - **throws a clear exception naming the offending value and the legal set** when it is absent, empty, or unknown (decision 7) — this runs before `builder.Build()`, so it surfaces as a startup crash;
  - validates that the chosen provider's own required settings are present (Google: `ClientId`/`ClientSecret`; Oidc: `Authority`/`ClientId`/`ClientSecret`) and throws the same way if not — note this tightens today's behaviour, where empty Google credentials boot happily;
  - registers the cookie scheme plus **exactly one** external scheme.
- For OIDC: set `Authority`, `ClientId`, `ClientSecret`, `ResponseType = "code"`, `SaveTokens = false` (nothing calls Graph), and add the `email` scope. Leave PKCE and the `/signin-oidc` callback path at framework defaults.
- Claim mapping per decision 11 — in `OnTokenValidated`, resolve `email` then `preferred_username`; if neither yields a value, fail the authentication with an explicit message rather than letting an email-less principal through. Ensure the resulting principal carries the value as `ClaimTypes.Email`, since that is what `RequireRoleFilter` reads. Watch for the framework's inbound claim-type mapping here — verify empirically what `ClaimTypes.Email` actually contains after a real Entra sign-in rather than assuming.

### `Program.cs`

- Replace lines 23-32 with the single `AddConfiguredAuthentication(builder.Configuration)` call.
- `/login` must challenge **the configured scheme**, not `GoogleDefaults.AuthenticationScheme`. Resolve the scheme name from the same configuration source.
- `/logout` unchanged (decision 20).

### `UserAuthorization.cs`

Lowercase the incoming email before the query (decision 12), and add the invariant comment from decision 10. Use an invariant-culture lowercase, not the current culture — a Turkish-locale host would otherwise mangle a dotted `I`.

### `UserCatalog.cs`

Lowercase in `CreateAsync` before both the duplicate check and the insert, so the duplicate check and the later lookup agree.

### Email-normalization migration

A data-only migration issuing `UPDATE "Users" SET "Email" = lower("Email") WHERE "Email" <> lower("Email");`. **Run the collision check from decision 12 first** — the unique index will reject the update if two rows differ only by case. Existing rows should all be Google-issued and already lowercase, so expect this to affect zero rows in practice; it exists so a database that has drifted cannot silently break sign-in.

Leave `Inspection.UploadedByEmail` alone — those comparisons are already `OrdinalIgnoreCase`.

### `MainLayout.razor`

The button label must stop hardcoding "Google". Simplest form that satisfies decision 6: derive the display name from the configured provider (`"Sign in with Google"` / `"Sign in with Microsoft"`), injecting `IConfiguration` or a small options record. Keep the existing `<Icon Name="log-in" />` and button classes — this is a label change, not a redesign, and `wwwroot/brand/tokens.css` still owns the styling.

### `appsettings.json`

Add the provider key and an `Oidc` section alongside the existing `Google` one, with `REPLACE_ME` placeholders matching the current convention. Default `Authentication:Provider` to `"Google"` **in the checked-in file only** so local development and the existing production deployment behave exactly as they do today — this is a checked-in default value, not a runtime fallback, and decision 7's throw still applies when the key is missing entirely.

## Docs to update

- **`docs/adr/0003-per-customer-deployment-supersedes-organization-modeling.md`** (new) — records decision 2, states plainly that ADR 0001's decision was never implemented, and supersedes it. Add a "Superseded by ADR 0003" line at the top of ADR 0001 without altering its body.
- **`CONTEXT.md`** — the **Organization** entry currently describes a table that does not exist. Rewrite it to say tenancy is handled by per-customer deployments and that there is no Organization entity, or remove the entry and its `_Avoid_` line.
- **`src/QrSimple.Api/BusinessTime.cs:9-12`** — the comment reasons from ADR 0001 ("models Organization from day one for eventual multi-tenancy"). Correct it; the conclusion it reaches about a hardcoded timezone is still right, but the premise is not.
- **`AGENTS.md:117`** — amend the v1 non-goals line: "non-Google sign-in" was reversed by this plan. Keep "multi-Organization UI" as a non-goal — decision 2 makes it permanent rather than deferred.
- **`AGENTS.md` Status section** — add the new auth setup to the built list, and note the OpenIdConnect package alongside the existing package notes.
- **`docs/plans/README.md`** — add the index row for 0003 when creating this file, and update it when Status changes.

## Tests

Existing suite should stay green untouched — `TestAuthHandler` replaces the whole authentication stack in the test host, so none of it depends on which external provider is configured. **Verify that assumption early**, since `ApiFactory` builds the real `Program` and decision 7's startup throw could break every test at once if the test configuration lacks `Authentication:Provider`. If it does, set it explicitly in `ApiFactory` rather than weakening the throw.

New coverage, all of it on logic we own (decision 22):

- Provider resolution returns the right scheme name for `"Google"` and `"Oidc"`.
- **Startup throws** on a missing key, an empty key, an unknown value, and on a known provider missing its own required settings. Assert the message names the bad value — a confusing failure here is the exact thing decision 7 exists to prevent.
- `/login` challenges the configured scheme, for both providers.
- Claim mapping: `email` present wins; `email` absent falls back to `preferred_username`; **both absent fails the sign-in** rather than producing an email-less principal.
- Email normalization: a user created with mixed-case email is found by a lowercase lookup, and a mixed-case claim finds a lowercase row. Include the Turkish-`I` case if it is cheap.
- `MainLayout` renders the right provider label.

## Verification checklist

Build and tests run inside the devcontainer (see `AGENTS.md`'s "Running tests" section).

- [ ] Full build clean; full test suite green.
- [ ] **Regression:** local run with `Authentication:Provider=Google` behaves exactly as before — sign in, reach `/app`, role gating intact.
- [ ] Startup crashes, with a legible message, on each bad-configuration case from Tests.
- [ ] Local run with `Authentication:Provider=Oidc` against the Entra test tenant: sign-in completes end-to-end and lands on `/app` as a provisioned user.
- [ ] Confirm what `ClaimTypes.Email` actually holds after a real Entra sign-in — do not take the framework's mapping on trust (decision 11).
- [ ] A user whose Entra UPN is mixed-case signs in successfully against a lowercase invited row (decision 12).
- [ ] An authenticated-but-uninvited account still gets 403, not an auto-created user (decision 4).
- [ ] `MainLayout` shows "Sign in with Microsoft" under OIDC and "Sign in with Google" under Google.
- [ ] `qr-simple-demo` deployed, with its own database, seeded equipment **and** inspection records, and `PublicBaseUrl` pointing at itself.
- [ ] **Multi-tenant check (decision 14):** an account from a directory *other than* our test tenant can sign in to the demo app. If tenant consent blocks it, record that in the Log and confirm the fallback account works.
- [ ] On the demo app: scan a QR code with a phone → public scan page → inspection list → open a PDF. No login prompt anywhere in that path, and no uploader email addresses visible (plan 0002 decision 11).
- [ ] Production is untouched and still authenticating via Google.

## Log

2026-08-29: Plan drafted and grilled with the project owner across four rounds (22 questions). Decisions above are final as of this date.

2026-08-29: Grilling found that `docs/adr/0001-organization-modeled-from-day-one.md` and `CONTEXT.md` describe an `Organization` entity that was never built — no entity file, no column, no migration, only a `BusinessTime.cs` comment reasoning from the absent table. The ADR existed specifically to avoid a later backfill; that cost was never actually avoided. Decision 2 (per-customer deployments) makes it moot rather than urgent, but the docs were actively misleading and this session was itself misled by them before checking.

2026-08-29: Grilling also found that email is the identity primary key while the lookup (`UserAuthorization.cs:8`) is case-sensitive, whereas the ownership comparisons in `InspectionCatalog.cs:124` and `EquipmentInspections.razor:168` are `OrdinalIgnoreCase`. Harmless with Google, which always asserts lowercase; a live bug the moment Entra is involved. Hence decision 12.

2026-08-29: An earlier round settled a per-user "allowed provider" binding to reject issuer mismatches. It was dropped once decision 6 (one registered scheme per deployment) made the mismatch structurally impossible — it would have been dead code guarding an unreachable state. Replaced by the code comment in decision 10, which is the actual mitigation.
