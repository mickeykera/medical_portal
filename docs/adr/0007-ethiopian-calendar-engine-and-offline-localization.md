# ADR 0007: Ethiopian Calendar Engine and Offline-First Localization Architecture

- **Status:** Accepted
- **Date:** 2026-10-24
- **Authors:** Senior Health-Informatics Architect / Antigravity Engineering Team
- **Applies to:** Full Solution (`MedicalPortal.Core`, `MedicalPortal.Infrastructure`, `MedicalPortal.Web`, `frontend/`, `MedicalPortal.Tests`)
- **Implements:** Target Architecture Rule G (Dual Calendar Engine), Rule F (DHIS2 Interoperability Periods), Working Rule 8 (Offline Self-Sufficiency & Browser Dialog Ban)

---

## 1. Context & Business Drivers

To deploy the Medical Portal to hospital systems in Ethiopia, operate seamlessly in offline LAN environments, and interface with Ethiopia's national healthcare systems (DHIS2 for HMIS, eCHIS, and Master Facility Registry), the platform requires:

1. **Dual Calendar Capability (Target Architecture Rule G)**:
   - Clinical systems and databases must store timestamps strictly in Gregorian UTC (`timestamptz` in UTC, `date` for calendar dates, timezone `Africa/Addis_Ababa`).
   - The Ethiopian calendar (*Ge'ez calendar*) must be provided as an input, display, and reporting layer.
   - A single, mathematically verified conversion algorithm must be shared across the backend C# domain, SQL analytics, and client-side JavaScript.
2. **National Reporting & Fiscal Periods (Target Architecture Rule F)**:
   - The Ethiopian Fiscal Year (EFY) begins on 1 Hamle (July 8 Gregorian).
   - Ministry of Health HMIS reporting through DHIS2 requires aggregate monthly and quarterly period identifiers (e.g. `201902`, `2019Q1`).
3. **High-Performance Reporting via Precalculated Dimensions**:
   - Aggregating clinical observations, patient visits, and appointments by Ethiopian month or fiscal quarter directly in PostgreSQL without slow user-defined runtime conversions.
4. **Offline Localization & Font Support (Working Rule 8)**:
   - The hospital server operates on an isolated LAN without internet connectivity.
   - Full localization support for English (`en`), Amharic (`am` - አማርኛ), and Afaan Oromoo (`om`).
   - Zero runtime CDN dependencies (scripts, styles, and fonts must reside locally on disk).
5. **Elimination of Browser Dialogs (Working Rule 8)**:
   - Complete removal of `window.alert`, `window.confirm`, and `window.prompt` in favor of accessible, non-blocking in-app modal cards and toast notifications.

---

## 2. Decision & Architecture

### 2.1 Pure Bidirectional Conversion Engine

We implemented [`EthiopianCalendar.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Common/EthiopianCalendar.cs) in the Core domain and [`ethiopian-calendar.js`](file:///home/michael/projects/app/medical_portal/frontend/js/ethiopian-calendar.js) for the offline browser client.

#### Mathematical Foundation & Epoch Alignment
- **Epoch**: The Ethiopian Era of Incarnation (*Amete Mihret*, A.M.) began on Meskerem 1, 1 A.M., which corresponds to Julian August 29, 8 A.D., or Gregorian August 27, 8 A.D.
- **Ordinal Offset**: In .NET 8, `DateOnly(1, 1, 1).DayNumber == 0`. The exact day offset from the Gregorian epoch (0001-01-01) to the Ethiopian epoch (0008-08-27) is **`2,795`** days:
  $$\text{DaysSinceEthEpoch} = \text{DateOnly.DayNumber} - 2795$$
- **Leap Year Cycle**:
  - The Ethiopian calendar has a pure 4-year leap cycle with no centennial exceptions:
    $$\text{Cycle} = 4 \times 365 + 1 = 1461 \text{ days}$$
  - A year is a leap year if:
    $$\text{Year} \pmod 4 == 3$$
  - Every Ethiopian year consists of 12 equal months of 30 days (360 days) plus a 13th intercalary month called **Pagume** (ጳጉሜ / Qaqumee):
    - Common years: Pagume has 5 days (total 365 days).
    - Leap years: Pagume has 6 days (total 366 days).
- **Verification**: Bidirectional roundtrip conversion was tested and validated across **73,414 consecutive days** from 1900-01-01 to 2100-12-31 with zero drift or discrepancies. Benchmark dates verified include:
  - *Ethiopian Millennium*: `2007-09-12` -> `2000-01-01` (1 Meskerem 2000).
  - *Battle of Adwa*: `1896-03-01` -> `1888-06-23` (23 Yekatit 1888).
  - *Leap Day Pagume 6, 2015*: `2023-09-11` -> `2015-13-06`.
  - *Enkutatash 2016*: `2023-09-12` -> `2016-01-01`.
  - *Common Year Pagume 5, 2016*: `2024-09-10` -> `2016-13-05`.
  - *Enkutatash 2017*: `2024-09-11` -> `2017-01-01`.

### 2.2 Ethiopian Fiscal Year & DHIS2 Reporting Periods

- **Fiscal Year (EFY)**:
  - The Ethiopian Fiscal Year starts on 1 Hamle (July 8 Gregorian).
  - Months 11 (Hamle), 12 (Nehase), and 13 (Pagume) belong to Fiscal Year $Year_{eth} + 1$, Quarter 1 (Q1).
  - Months 1 (Meskerem) belongs to Fiscal Year $Year_{eth} + 1$, Quarter 1 (Q1).
  - Months 2..4 (Tikimt, Hidar, Tahsas) belong to Fiscal Year $Year_{eth}$, Quarter 2 (Q2).
  - Months 5..7 (Tir, Yekatit, Magabit) belong to Fiscal Year $Year_{eth}$, Quarter 3 (Q3).
  - Months 8..10 (Miyazya, Ginbot, Sene) belong to Fiscal Year $Year_{eth}$, Quarter 4 (Q4).
- **DHIS2 Formats**:
  - Monthly: `YYYYMM` (e.g. `201601` for Meskerem 2016, `201613` for Pagume 2016).
  - Quarterly: `YYYYQ#` (e.g. `2017Q1` for Hamle-Meskerem).
  - Yearly: `YYYY` (e.g. `2016`).

### 2.3 Precalculated Calendar Dimension (`calendar_dim`)

To support high-throughput SQL reporting and HMIS data exports without running conversion logic per row:
- Created [`CalendarDim`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Entities/CalendarEntities.cs) mapped to table `"calendar_dim"`.
- Table attributes include:
  - `date` (`DateOnly`, Primary Key)
  - `gregorian_year`, `gregorian_month`, `gregorian_day`, `gregorian_quarter`, `day_of_week`
  - `day_of_week_name_english`, `day_of_week_name_amharic`, `day_of_week_name_oromo`
  - `ethiopian_year`, `ethiopian_month`, `ethiopian_day`, `ethiopian_quarter`
  - `ethiopian_month_name_english`, `ethiopian_month_name_amharic`, `ethiopian_month_name_oromo`
  - `ethiopian_date_formatted`, `ethiopian_date_formatted_amharic`, `ethiopian_date_formatted_oromo`
  - `ethiopian_fiscal_year`, `ethiopian_fiscal_quarter`
  - `is_ethiopian_leap_year`, `is_pagume`, `is_enkutatash`
  - `dhis2_monthly_period`, `dhis2_quarterly_period`, `dhis2_yearly_period`
- Prepopulated with 73,414 rows spanning 1900..2100 via EF Core migration and [`DbSeeder.SeedCalendarDimensionAsync`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Data/DbSeeder.cs).
- Composite indexes on `(ethiopian_year, ethiopian_month)`, `(ethiopian_fiscal_year, ethiopian_fiscal_quarter)`, and `dhis2_monthly_period`.

### 2.4 Localization Service & Client-Side Localization Engine

- **Domain Catalogs**: [`LocalizationCatalog.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Core/Common/LocalizationCatalog.cs) contains 100% complete key parity across:
  - English (`en`)
  - Amharic (`am` - አማርኛ)
  - Afaan Oromoo (`om`)
- **Backend Service**: [`LocalizationService.cs`](file:///home/michael/projects/app/medical_portal/backend/src/MedicalPortal.Infrastructure/Localization/LocalizationService.cs) registered as a singleton with fallback to English for missing keys or unsupported locales.
- **REST APIs**:
  - `GET /api/localization/{language}`
  - `GET /api/localization/languages`
  - `GET /api/calendar/convert?date=YYYY-MM-DD`
  - `GET /api/calendar/convert-ethiopian?year=Y&month=M&day=D`
  - `GET /api/calendar/today` (localized to `Africa/Addis_Ababa` UTC+3)
  - `GET /api/calendar/month-view?year=Y&month=M`
- **Offline Client Engine**: [`i18n.js`](file:///home/michael/projects/app/medical_portal/frontend/js/i18n.js) and static JSON catalogs in `frontend/locales/` allowing dynamic language switching in the SPA with data attribute translation (`data-i18n="key"`).

### 2.5 Offline Self-Sufficiency & Dialog Ban (Rule 8)

- **Zero CDN Dependencies**:
  - Vendored Microsoft SignalR client locally to `frontend/js/vendor/signalr.min.js`.
  - Downloaded `NotoSansEthiopic-Regular.woff2` to `frontend/fonts/` with `@font-face` definition in `frontend/css/app.css`.
  - Removed all external `<script>` and `<link>` tags from `frontend/index.html`.
- **Zero Browser Dialogs**:
  - Removed all 22 `alert(...)` calls and all 6 `confirm(...)` calls across `frontend/js/app.js`.
  - Implemented `showInAppConfirm(title, message, confirmText, cancelText)` and `showInAppAlert(title, message, okText)` returning accessible Promises with keyboard navigation and focus management.
  - Implemented non-intrusive toast notifications via `showToast(message, type, durationMs)`.

---

## 3. Consequences & Trade-offs

### Positive
- **100% Offline Capability**: The entire frontend SPA, SignalR hub client, fonts, localization catalogs, and dual calendar engine run in completely disconnected LAN environments.
- **Zero Database Drift**: The database stores standard Gregorian `timestamptz` and `date`, maintaining compatibility with standard FHIR R4 and international tooling.
- **Sub-Millisecond Ethiopian Reporting**: Grouping and filtering in PostgreSQL via `calendar_dim` allows direct `GROUP BY ethiopian_year, ethiopian_month` without query functions or stored procedures.
- **Zero Browser Dialog Disruption**: Modern in-app modals provide accessibility, avoid locking the browser thread, and allow consistent styling matching tenant design tokens.

### Mitigations
- **Calendar Range Limit**: The `calendar_dim` table is generated for years 1900 to 2100 (73,414 rows, ~12 MB of table storage). Dates outside this range fall back automatically to the pure `EthiopianCalendar` mathematical calculation in memory.
- **Initial Dimension Seeding**: Seeding 73,414 rows in EF Core is batched in chunks of 2,500 with `ChangeTracker.Clear()` to eliminate memory overhead and maintain high throughput during setup.
