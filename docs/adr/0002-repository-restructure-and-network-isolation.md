# ADR 0002: Repository Restructure & Network Isolation

- **Status:** Accepted
- **Date:** 2026-10-24
- **Authors:** Senior Health-Informatics Architect / Antigravity Engineering Team
- **Applies to:** Full Repository (`medical_portal`)

---

## 1. Context & Business Drivers

As established in [ADR 0001: Target Architecture](0001-target-architecture.md), transitioning Springfield Health's prototype into a modular, offline-first hospital platform for Ethiopia requires establishing a clean separation between the user-facing patient portal, the API backend, and the clinical core.

Previously, the repository had several structural deficiencies:
1. Flat repository root with .NET solution, source directories, test projects, and legacy Obsidian notes (`portal/.obsidian/`) intermingled.
2. The Single Page Application (SPA) assets were embedded inside `src/MedicalPortal.Web/wwwroot/`, blurring the boundary between frontend client code and ASP.NET Core API server logic.
3. PostgreSQL (port 5432) and Redis (port 6379) were directly mapped to the host interface in `docker-compose.yml`, exposing data stores beyond the internal container network.
4. Nginx proxy configuration was only active under the `cluster` profile, while standalone mode exposed the ASP.NET Core Kestrel port (5000:8080) directly to the host.

---

## 2. Decision & Implementation

### 2.1 Repository Directory Split
Using `git mv`, the repository has been partitioned into top-level modules:
- **`backend/`**: Contains `MedicalPortal.sln`, `src/` (`MedicalPortal.Core`, `MedicalPortal.Infrastructure`, `MedicalPortal.Web`), `tests/` (`MedicalPortal.Tests`, `load/k6-portal-flow.js`), and `backend/Dockerfile`.
- **`frontend/`**: Contains the client-side Single Page Application assets (`index.html`, `css/app.css`, `js/app.js`), detached from the ASP.NET Core web host.
- **`docs/`**: Centralized architectural records (`adr/`), audit findings, runbooks, and open questions registers.
- Legacy `portal/` folder (which contained only local Obsidian vault metadata) was removed.

### 2.2 Same-Origin Reverse Proxy Architecture
Nginx (`nginx.conf`) is established as the single ingress point on port 5000:
- **Root Path (`/`)**: Serves static SPA files directly from `/usr/share/nginx/html` (mounted from `./frontend` in Compose) with `try_files $uri $uri/ /index.html` fallback.
- **API Endpoints (`/api/`)**: Reverse-proxied to internal `backend:8080`.
- **SignalR Hubs (`/hubs/`)**: Reverse-proxied to internal `backend:8080` with HTTP `Upgrade` and `Connection` headers for WebSockets.
- **FHIR R4 Endpoints (`/R4/`)**: Reverse-proxied to internal `backend:8080`.
- **Health Checks (`/health`)**: Reverse-proxied to internal `backend:8080`.
- **Interactive Documentation (`/swagger/`)**: Reverse-proxied to internal `backend:8080`.

### 2.3 Network Isolation & Host Port Removal
- Defined an internal bridge network: `portal-internal`.
- **PostgreSQL 17 Alpine**: Host port mapping (`5432:5432`) removed. The database is reachable strictly by application containers within `portal-internal`.
- **Redis 7 Alpine**: Host port mapping (`6379:6379`) removed. Accessible strictly by clustered app instances within `portal-internal`.
- **Application Backend (`medical-portal`, `medical-portal-1`, `medical-portal-2`)**: Host port mapping `5000:8080` removed. Application nodes listen on port 8080 on `portal-internal` under the network alias `backend`.
- **Edge Proxy (`proxy`)**: Binds to host port `5000:80`, terminating client connections and forwarding to `backend:8080`.

### 2.4 CI/CD & Build Pipelines
- Updated `.github/workflows/ci.yml` paths to invoke `dotnet restore backend/MedicalPortal.sln`, `dotnet build backend/MedicalPortal.sln`, and `dotnet test backend/tests/MedicalPortal.Tests/MedicalPortal.Tests.csproj`.
- Provided multi-stage container builds in both root `Dockerfile` and `backend/Dockerfile`.

---

## 3. Consequences & Verification

- **Security Posture**: Attack surface significantly decreased by removing direct host access to PostgreSQL and Redis. The web tier is shielded behind Nginx with standard forwarded header normalization.
- **Developer Experience**: Clear decoupling between frontend static assets and backend C# services. Developers can work on frontend code without recompiling .NET assemblies.
- **Test Baseline**: All 259 unit, integration, and security tests pass cleanly in `backend/` (`Passed: 259, Failed: 0, Skipped: 0`).
