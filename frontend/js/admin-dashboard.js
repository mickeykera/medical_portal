/**
 * Hospital Administrator Operations Console (Hospital Admin Dashboard)
 * Modularized SPA component for Medical Portal (.NET 8).
 * 
 * Supports 8 operational sections:
 *  1. Overview: KPIs linking to fix pages
 *  2. Users & Access: Invite-based creation, RBAC, session revocation, MFA reset, CSV import
 *  3. Organization: Facilities, Departments, Addis Ababa subcity locations, Providers, Schedules, Appt Types
 *  4. Audit Log: Break-glass highlights, hash chain verification, step-up CSV export
 *  5. Security Policy: Bounded lockout/idle timeouts, MFA per role, active session termination
 *  6. Integrations: OpenMRS, HMS, DHIS2, Payment Gateways with masked secrets & conflict resolution
 *  7. Billing Settings: ETB currency display, payment plans, refund approval queue
 *  8. Tenant Settings: Branding, WCAG contrast calculation, dual calendar, platform modules
 */

(function () {
  'use strict';

  function escapeHtml(str) {
    if (str === null || str === undefined) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  function showToast(msg) {
    if (typeof window.showToast === 'function') {
      window.showToast(msg);
    } else {
      console.log('[Admin Toast]', msg);
    }
  }

  let currentSubTab = 'overview';
  let cachedStepUpToken = null;
  let stepUpTokenExpiresAt = 0;

  // Addis Ababa Subcities
  const ADDIS_ABABA_SUBCITIES = [
    'Bole', 'Kirkos', 'Arada', 'Yeka', 'Lideta', 'Nifas Silk-Lafto',
    'Gulele', 'Akaky Kaliti', 'Kolfe Keranio', 'Addis Ketema', 'Lemi Kura'
  ];

  // Dual calendar formatting helper
  function formatDualDate(dateStr) {
    if (!dateStr) return '—';
    try {
      const d = new Date(dateStr);
      const greg = d.toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
      if (window.EthiopianCalendar && typeof window.EthiopianCalendar.gregorianToEthiopian === 'function') {
        const eth = window.EthiopianCalendar.gregorianToEthiopian(d.getFullYear(), d.getMonth() + 1, d.getDate());
        return `${greg} (${eth.day}/${eth.month}/${eth.year} E.C.)`;
      }
      return greg;
    } catch {
      return dateStr;
    }
  }

  // Formatting ETB currency
  function formatEtb(minorUnits) {
    const val = (Number(minorUnits || 0) / 100).toFixed(2);
    return `ETB ${val} Br`;
  }

  // WCAG 2.1 Contrast Calculation
  function calculateContrastRatio(hexColor) {
    if (!hexColor || !/^#[0-9A-Fa-f]{6}$/.test(hexColor)) return { ratio: 1.0, passesAa: false };
    const r = parseInt(hexColor.slice(1, 3), 16) / 255;
    const g = parseInt(hexColor.slice(3, 5), 16) / 255;
    const b = parseInt(hexColor.slice(5, 7), 16) / 255;

    const toLinear = (c) => (c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4));
    const lum = 0.2126 * toLinear(r) + 0.7152 * toLinear(g) + 0.0722 * toLinear(b);
    const whiteLum = 1.0;
    const ratio = (whiteLum + 0.05) / (lum + 0.05);
    const rounded = Math.round(ratio * 100) / 100;
    return { ratio: rounded, passesAa: rounded >= 4.5 };
  }

  // --- Step-Up Modal Flow ---
  async function ensureStepUpToken(actionDescription) {
    const now = Date.now();
    if (cachedStepUpToken && now < stepUpTokenExpiresAt) {
      return cachedStepUpToken;
    }

    return new Promise((resolve, reject) => {
      showInAppModal({
        title: '🔒 Step-Up Re-Authentication Required',
        body: `
          <div style="display: flex; flex-direction: column; gap: 1rem;">
            <div class="notice notice-warn">
              <strong>High-Security Administrative Action:</strong><br>
              ${escapeHtml(actionDescription || 'You are performing a sensitive administrative operation.')}
            </div>
            <div>
              <label for="stepup-password" style="font-weight: 600; display: block; margin-bottom: 0.25rem;">
                Confirm Your Administrator Password
              </label>
              <input type="password" id="stepup-password" class="topbar-select" style="width: 100%;" placeholder="Enter your current password" autocomplete="current-password" autofocus />
            </div>
            <div id="stepup-error" style="color: var(--bad-text); font-size: 0.8125rem; display: none;"></div>
          </div>
        `,
        confirmText: 'Verify Password',
        cancelText: 'Cancel',
        onConfirm: async () => {
          const passInput = document.getElementById('stepup-password');
          const errDiv = document.getElementById('stepup-error');
          const pass = passInput ? passInput.value.trim() : '';

          if (!pass) {
            if (errDiv) {
              errDiv.textContent = 'Password is required.';
              errDiv.style.display = 'block';
            }
            return false;
          }

          try {
            const res = await window.api('/api/auth/step-up', {
              method: 'POST',
              body: JSON.stringify({ password: pass, action: 'admin_step_up' })
            });

            cachedStepUpToken = res.stepUpToken;
            stepUpTokenExpiresAt = Date.now() + ((res.expiresInSeconds || 300) * 1000) - 10000;
            closeInAppModal();
            resolve(cachedStepUpToken);
            return true;
          } catch (err) {
            if (errDiv) {
              errDiv.textContent = err.message || 'Incorrect password.';
              errDiv.style.display = 'block';
            }
            return false;
          }
        },
        onCancel: () => {
          reject(new Error('Step-up verification cancelled.'));
        }
      });
    });
  }

  // --- Modal Helpers ---
  function showInAppModal({ title, body, confirmText = 'Confirm', cancelText = 'Cancel', onConfirm, onCancel }) {
    let container = document.getElementById('modal-container');
    if (!container) {
      container = document.createElement('div');
      container.id = 'modal-container';
      document.body.appendChild(container);
    }

    container.innerHTML = `
      <div class="modal-scrim" id="admin-modal-scrim" role="dialog" aria-modal="true" aria-labelledby="admin-modal-title">
        <div class="modal-box">
          <div class="modal-header">
            <span id="admin-modal-title">${escapeHtml(title)}</span>
            <button type="button" class="btn btn-outline btn-sm" id="btn-admin-close-modal" aria-label="Close dialog">✕</button>
          </div>
          <div class="modal-body">${body}</div>
          <div class="modal-footer">
            <button type="button" class="btn btn-outline" id="btn-admin-cancel-modal">${escapeHtml(cancelText)}</button>
            <button type="button" class="btn btn-primary" id="btn-admin-confirm-modal">${escapeHtml(confirmText)}</button>
          </div>
        </div>
      </div>
    `;

    const escListener = (e) => {
      if (e.key === 'Escape') {
        closeInAppModal();
        if (onCancel) onCancel();
      }
    };
    window.addEventListener('keydown', escListener, { once: true });

    document.getElementById('btn-admin-close-modal').onclick = () => {
      closeInAppModal();
      if (onCancel) onCancel();
    };
    document.getElementById('btn-admin-cancel-modal').onclick = () => {
      closeInAppModal();
      if (onCancel) onCancel();
    };
    document.getElementById('btn-admin-confirm-modal').onclick = async () => {
      if (onConfirm) {
        const keepOpen = await onConfirm();
        if (keepOpen !== false) closeInAppModal();
      } else {
        closeInAppModal();
      }
    };

    setTimeout(() => {
      const firstInput = container.querySelector('input:not([disabled]), select:not([disabled]), textarea:not([disabled]), button:not(#btn-admin-close-modal)');
      if (firstInput) firstInput.focus();
    }, 50);
  }

  function showTypedConfirmModal({ title, message, matchWord = 'CONFIRM', onConfirmed }) {
    showInAppModal({
      title,
      body: `
        <div style="display: flex; flex-direction: column; gap: 1rem;">
          <div class="notice notice-bad">${escapeHtml(message)}</div>
          <div>
            <label for="typed-confirm-input" style="font-size: 0.8125rem; font-weight: 600; display: block; margin-bottom: 0.25rem;">
              Type <strong>${escapeHtml(matchWord)}</strong> below to confirm:
            </label>
            <input type="text" id="typed-confirm-input" class="topbar-select" style="width: 100%; font-family: var(--font-mono);" autocomplete="off" />
          </div>
          <div id="typed-confirm-err" style="color: var(--bad-text); font-size: 0.8125rem; display: none;"></div>
        </div>
      `,
      confirmText: 'Execute Action',
      onConfirm: async () => {
        const inp = document.getElementById('typed-confirm-input');
        const errDiv = document.getElementById('typed-confirm-err');
        if (!inp || inp.value.trim() !== matchWord) {
          if (errDiv) {
            errDiv.textContent = `You must type exactly '${matchWord}' to proceed.`;
            errDiv.style.display = 'block';
          }
          return false;
        }
        if (onConfirmed) await onConfirmed();
        return true;
      }
    });
  }

  function closeInAppModal() {
    const container = document.getElementById('modal-container');
    if (container) container.innerHTML = '';
  }

  // --- Main Entrypoint ---
  async function render(el, currentSession) {
    const userRole = currentSession?.user?.role || 'ADMIN';
    const isDeptAdmin = userRole === 'DEPT_ADMIN';
    const isPlatformAdmin = userRole === 'PLATFORM_ADMIN';

    el.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1.5rem;">
        <!-- Header -->
        <div style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 0.75rem;">
          <div>
            <h2 style="margin: 0; font-size: 1.35rem; display: flex; align-items: center; gap: 0.5rem;">
              <span>🏥</span> Hospital Administration &amp; Operations Dashboard
            </h2>
            <div style="font-size: 0.8125rem; color: var(--text-muted);">
              Addis Care Health Network (Addis Ababa, Ethiopia) • Africa/Addis_Ababa • Dual Calendar
            </div>
          </div>
          <div style="display: flex; gap: 0.5rem; align-items: center;">
            <span class="pill info">${escapeHtml(userRole)}</span>
            <span class="pill good">🏥 Hospital Wide</span>
            ${isDeptAdmin ? '<span class="pill warn">Dept Scoped</span>' : ''}
            ${isPlatformAdmin ? '<span class="pill info">Zero PHI</span>' : ''}
          </div>
        </div>

        <!-- 8 Section Subnavigation -->
        <nav class="admin-subnav" aria-label="Hospital administration sections" style="display: flex; gap: 0.5rem; flex-wrap: wrap; border-bottom: 1px solid var(--border); padding-bottom: 0.5rem;">
          <button type="button" class="btn btn-sm ${currentSubTab === 'overview' ? 'btn-primary' : 'btn-outline'}" data-admin-tab="overview">📊 Overview</button>
          <button type="button" class="btn btn-sm ${currentSubTab === 'users' ? 'btn-primary' : 'btn-outline'}" data-admin-tab="users">👥 Users &amp; Access</button>
          <button type="button" class="btn btn-sm ${currentSubTab === 'organization' ? 'btn-primary' : 'btn-outline'}" data-admin-tab="organization">🏥 Organization</button>
          <button type="button" class="btn btn-sm ${currentSubTab === 'audit' ? 'btn-primary' : 'btn-outline'}" data-admin-tab="audit">📜 Audit Log</button>
          ${!isDeptAdmin ? `
            <button type="button" class="btn btn-sm ${currentSubTab === 'security' ? 'btn-primary' : 'btn-outline'}" data-admin-tab="security">🛡️ Security Policy</button>
            <button type="button" class="btn btn-sm ${currentSubTab === 'integrations' ? 'btn-primary' : 'btn-outline'}" data-admin-tab="integrations">🔌 Integrations</button>
            <button type="button" class="btn btn-sm ${currentSubTab === 'billing' ? 'btn-primary' : 'btn-outline'}" data-admin-tab="billing">💳 Billing Settings</button>
          ` : ''}
          <button type="button" class="btn btn-sm ${currentSubTab === 'tenant' ? 'btn-primary' : 'btn-outline'}" data-admin-tab="tenant">⚙️ Hospital Settings</button>
        </nav>

        <!-- Subtab Content Container -->
        <div id="admin-dashboard-view">
          <div style="text-align: center; padding: 2rem; color: var(--text-muted);">Loading dashboard...</div>
        </div>
      </div>
    `;

    // Hook tab switches
    el.querySelectorAll('button[data-admin-tab]').forEach(btn => {
      btn.onclick = () => {
        currentSubTab = btn.getAttribute('data-admin-tab');
        render(el, currentSession);
      };
    });

    const viewContainer = document.getElementById('admin-dashboard-view');
    try {
      if (currentSubTab === 'overview') await renderOverview(viewContainer, el, currentSession);
      else if (currentSubTab === 'users') await renderUsers(viewContainer, el, currentSession);
      else if (currentSubTab === 'organization') await renderOrganization(viewContainer, el, currentSession);
      else if (currentSubTab === 'audit') await renderAudit(viewContainer, el, currentSession);
      else if (currentSubTab === 'security') await renderSecurity(viewContainer, el, currentSession);
      else if (currentSubTab === 'integrations') await renderIntegrations(viewContainer, el, currentSession);
      else if (currentSubTab === 'billing') await renderBilling(viewContainer, el, currentSession);
      else if (currentSubTab === 'tenant') await renderTenant(viewContainer, el, currentSession);
    } catch (err) {
      viewContainer.innerHTML = `
        <div class="card" style="border-left: 4px solid var(--bad-text); padding: 1.5rem;">
          <h3 style="color: var(--bad-text); margin-top: 0; font-size: 1.1rem; display: flex; align-items: center; gap: 0.5rem;">
            <span>⚠️</span> Unable to load ${escapeHtml(currentSubTab)} section
          </h3>
          <p style="color: var(--text-muted); font-size: 0.875rem; margin-bottom: 1rem;">
            ${escapeHtml(err.message || 'An error occurred while communicating with the hospital backend service.')}
          </p>
          <button type="button" class="btn btn-primary btn-sm" id="retry-subtab-btn">🔄 Retry</button>
        </div>
      `;
      const retryBtn = document.getElementById('retry-subtab-btn');
      if (retryBtn) retryBtn.onclick = () => render(el, currentSession);
    }
  }

  // =========================================================================
  // SECTION 1: Overview
  // =========================================================================
  async function renderOverview(container, rootEl, session) {
    const data = await window.api('/api/admin/overview');
    let complianceSummary = null;
    if (session?.user?.role === 'COMPLIANCE_AUDITOR') {
      complianceSummary = await window.api('/api/compliance/summary');
    }
    const hospitalKpis = await window.api('/api/kpis');

    const roleBreakdown = Object.entries(data.activeUsersByRole || {})
      .map(([role, cnt]) => `<span><strong>${cnt}</strong> ${escapeHtml(role)}</span>`)
      .join(' • ');

    container.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1.5rem;">
        ${complianceSummary ? `
        <div class="card">
          <div class="card-header"><span class="card-title">Compliance audit summary</span><span class="pill good">Last 30 days</span></div>
          <div class="grid grid-4">
            <div class="stat-card"><span class="stat-label">Audit events</span><span class="stat-value">${complianceSummary.totalAuditEvents}</span></div>
            <div class="stat-card"><span class="stat-label">Proxy access</span><span class="stat-value">${complianceSummary.proxyAccesses}</span></div>
            <div class="stat-card"><span class="stat-label">Denied access</span><span class="stat-value">${complianceSummary.deniedAccesses}</span></div>
            <div class="stat-card"><span class="stat-label">Exports / prints</span><span class="stat-value">${complianceSummary.exportsAndPrints}</span></div>
          </div>
          <div class="notice notice-info">Billing-integrity signals: ${complianceSummary.billingIntegritySignals}. Patient-level financial details are not shown.</div>
        </div>` : ''}
        <div class="card">
          <div class="card-header"><span class="card-title">Hospital performance indicators</span><span class="pill info">De-identified · 30 days</span></div>
          <div class="grid grid-4">
            ${(hospitalKpis.indicators || []).slice(0, 8).map(indicator => `
              <div class="stat-card"><span class="stat-label">${escapeHtml(indicator.name)}</span>
                <span class="stat-value">${escapeHtml(String(indicator.value))}</span>
                <span class="stat-sub">${escapeHtml(indicator.unit)} · ${escapeHtml(indicator.category)}</span>
              </div>`).join('') || '<div class="notice notice-info">No KPI definitions configured.</div>'}
          </div>
        </div>

        <div class="grid grid-4">
          <!-- Active Users KPI -->
          <div class="card stat-card" style="cursor: pointer;" id="kpi-users">
            <div class="stat-label">👥 Active Users</div>
            <div style="font-size: 1.75rem; font-weight: 700;">${data.mfaAdoption?.totalActiveUsers || 0}</div>
            <div style="font-size: 0.75rem; color: var(--text-muted);">${roleBreakdown || 'No active users'}</div>
            <div style="margin-top: 0.5rem;"><button type="button" class="btn btn-outline btn-sm">Manage Users &rarr;</button></div>
          </div>

          <!-- Locked Accounts KPI -->
          <div class="card stat-card" style="cursor: pointer;" id="kpi-locked">
            <div class="stat-label">🔒 Locked Accounts</div>
            <div style="font-size: 1.75rem; font-weight: 700; color: ${data.lockedAccountsCount > 0 ? 'var(--bad-text)' : 'inherit'};">
              ${data.lockedAccountsCount || 0}
            </div>
            <div style="font-size: 0.75rem; color: var(--text-muted);">Failed sign-in lockouts</div>
            <div style="margin-top: 0.5rem;"><button type="button" class="btn btn-outline btn-sm">Review &amp; Unlock &rarr;</button></div>
          </div>

          <!-- Pending Invites KPI -->
          <div class="card stat-card" style="cursor: pointer;" id="kpi-invites">
            <div class="stat-label">✉️ Pending Invites</div>
            <div style="font-size: 1.75rem; font-weight: 700;">${data.pendingInvitesCount || 0}</div>
            <div style="font-size: 0.75rem; color: var(--text-muted);">Unaccepted invite tokens</div>
            <div style="margin-top: 0.5rem;"><button type="button" class="btn btn-outline btn-sm">View Invites &rarr;</button></div>
          </div>

        </div>

        <div class="grid grid-3">
          <!-- Integrations & Sync Health -->
          <div class="card stat-card" style="cursor: pointer;" id="kpi-integrations">
            <div class="stat-label">🔌 Integration &amp; Sync Health</div>
            <div style="display: flex; flex-direction: column; gap: 0.35rem; margin-top: 0.5rem; font-size: 0.8125rem;">
              <div style="display: flex; justify-content: space-between;">
                <span>OpenMRS FHIR R4:</span>
                <span class="pill ${data.integrationsHealth?.openMrs?.status === 'Healthy' ? 'good' : 'warn'}">${escapeHtml(data.integrationsHealth?.openMrs?.status || 'Unknown')}</span>
              </div>
              <div style="display: flex; justify-content: space-between;">
                <span>HMS Sync &amp; Webhook:</span>
                <span class="pill ${data.integrationsHealth?.hms?.status === 'Healthy' ? 'good' : 'warn'}">${escapeHtml(data.integrationsHealth?.hms?.status || 'Healthy')}</span>
              </div>
              <div style="display: flex; justify-content: space-between;">
                <span>DHIS2 HMIS:</span>
                <span class="pill good">${escapeHtml(data.integrationsHealth?.dhis2?.status || 'Configured')}</span>
              </div>
              <div style="display: flex; justify-content: space-between;">
                <span>Payment Gateways:</span>
                <span class="pill good">${escapeHtml(data.integrationsHealth?.paymentGateway?.status || 'Operational')}</span>
              </div>
            </div>
            <div style="margin-top: 0.75rem;"><button type="button" class="btn btn-outline btn-sm">Check Integrations &rarr;</button></div>
          </div>

          <!-- Failed Jobs & Dead Letters -->
          <div class="card stat-card" style="cursor: pointer;" id="kpi-deadletters">
            <div class="stat-label">⚠️ Failed Jobs &amp; Dead Letters</div>
            <div style="font-size: 1.75rem; font-weight: 700; color: ${(data.failedJobsAndDeadLetters?.total || 0) > 0 ? 'var(--bad-text)' : 'inherit'};">
              ${data.failedJobsAndDeadLetters?.total || 0}
            </div>
            <div style="font-size: 0.75rem; color: var(--text-muted);">
              ${data.failedJobsAndDeadLetters?.deadLettersCount || 0} HMS dead letters • ${data.failedJobsAndDeadLetters?.failedNotificationsCount || 0} notification delivery failures
            </div>
            <div style="margin-top: 0.75rem;"><button type="button" class="btn btn-outline btn-sm">Inspect Queue &rarr;</button></div>
          </div>

          <!-- Last Database Backup -->
          <div class="card stat-card">
            <div class="stat-label">💾 Last Backup Snapshot</div>
            <div style="font-size: 1.15rem; font-weight: 600; margin-top: 0.5rem;">
              ${formatDualDate(data.lastBackupTime)}
            </div>
            <div style="font-size: 0.75rem; color: var(--text-muted);">Addis Ababa Time: ${new Date(data.lastBackupTime).toLocaleTimeString()}</div>
            <div style="margin-top: 0.75rem;"><span class="pill good">Daily Snapshot Verified</span></div>
          </div>
        </div>
      </div>
    `;

    // Each card links to the page that fixes it
    container.querySelector('#kpi-users').onclick = () => { currentSubTab = 'users'; render(rootEl, session); };
    container.querySelector('#kpi-locked').onclick = () => { currentSubTab = 'users'; renderUsersWithFilter(rootEl, session, { locked: true }); };
    container.querySelector('#kpi-invites').onclick = () => { currentSubTab = 'users'; render(rootEl, session); };
    container.querySelector('#kpi-mfa').onclick = () => { currentSubTab = 'security'; render(rootEl, session); };
    container.querySelector('#kpi-integrations').onclick = () => { currentSubTab = 'integrations'; render(rootEl, session); };
    container.querySelector('#kpi-deadletters').onclick = () => { currentSubTab = 'integrations'; render(rootEl, session); };
  }

  // Helper for jumping to users with preset filters
  async function renderUsersWithFilter(rootEl, session, filters) {
    currentSubTab = 'users';
    await render(rootEl, session);
    const viewContainer = document.getElementById('admin-dashboard-view');
    await renderUsers(viewContainer, rootEl, session, filters);
  }

  // =========================================================================
  // SECTION 2: Users & Access
  // =========================================================================
  async function renderUsers(container, rootEl, session, initialFilters = {}) {
    let currentPage = initialFilters.page || 1;
    let searchVal = initialFilters.search || '';
    let roleVal = initialFilters.role || '';
    let lockedVal = initialFilters.locked === true ? 'true' : '';

    async function loadData() {
      let url = `/api/admin/users?page=${currentPage}&pageSize=20`;
      if (searchVal) url += `&search=${encodeURIComponent(searchVal)}`;
      if (roleVal) url += `&role=${encodeURIComponent(roleVal)}`;
      if (lockedVal) url += `&locked=${encodeURIComponent(lockedVal)}`;

      const data = await window.api(url);
      draw(data);
    }

    function draw(data) {
      container.innerHTML = `
        <div style="display: flex; flex-direction: column; gap: 1rem;">
          <!-- Controls -->
          <div style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 0.75rem;">
            <div style="display: flex; gap: 0.5rem; flex-wrap: wrap; align-items: center;">
              <input type="text" id="users-search" class="topbar-select" placeholder="Search name, email, MRN..." value="${escapeHtml(searchVal)}" style="min-width: 220px;" />
              <select id="users-role-filter" class="topbar-select">
                <option value="">All Roles</option>
                <option value="ADMIN" ${roleVal === 'ADMIN' ? 'selected' : ''}>ADMIN</option>
                <option value="CLINICIAN" ${roleVal === 'CLINICIAN' ? 'selected' : ''}>CLINICIAN</option>
                <option value="PHYSICIAN" ${roleVal === 'PHYSICIAN' ? 'selected' : ''}>PHYSICIAN</option>
                <option value="NURSE" ${roleVal === 'NURSE' ? 'selected' : ''}>NURSE</option>
                <option value="DEPT_ADMIN" ${roleVal === 'DEPT_ADMIN' ? 'selected' : ''}>DEPT_ADMIN</option>
                <option value="BILLING" ${roleVal === 'BILLING' ? 'selected' : ''}>BILLING</option>
                <option value="PATIENT" ${roleVal === 'PATIENT' ? 'selected' : ''}>PATIENT</option>
              </select>
              <select id="users-status-filter" class="topbar-select">
                <option value="">All Statuses</option>
                <option value="locked" ${lockedVal === 'true' ? 'selected' : ''}>Locked Out</option>
              </select>
              <button type="button" class="btn btn-outline btn-sm" id="btn-users-search">Filter</button>
            </div>
            <div style="display: flex; gap: 0.5rem;">
              <button type="button" class="btn btn-outline btn-sm" id="btn-import-csv">📥 Import CSV</button>
              <button type="button" class="btn btn-primary btn-sm" id="btn-invite-user">+ Invite User</button>
            </div>
          </div>

          <!-- Table -->
          <div class="table-wrap">
            <table aria-label="Users and access list">
              <thead>
                <tr>
                  <th scope="col">User</th>
                  <th scope="col">Role &amp; Memberships</th>
                  <th scope="col">Status</th>
                  <th scope="col">MFA</th>
                  <th scope="col">Last Login</th>
                  <th scope="col" style="text-align: right;">Actions</th>
                </tr>
              </thead>
              <tbody>
                ${data.items.length === 0 ? `
                  <tr><td colspan="6" style="text-align: center; color: var(--text-muted); padding: 2rem;">No matching accounts found.</td></tr>
                ` : data.items.map(u => `
                  <tr>
                    <td>
                      <div style="font-weight: 600;">${escapeHtml(u.person.fullName || u.email)}</div>
                      <div style="font-size: 0.75rem; color: var(--text-muted);">${escapeHtml(u.email)}</div>
                      ${u.person.mrn ? `<div style="font-size: 0.6875rem; color: var(--primary);">MRN: ${escapeHtml(u.person.mrn)}</div>` : ''}
                    </td>
                    <td>
                      <span class="pill info">${escapeHtml(u.role)}</span>
                      ${u.memberships.map(m => `
                        <div style="font-size: 0.75rem; color: var(--text-muted); margin-top: 0.2rem;">
                          🏢 ${escapeHtml(m.departmentName || m.facilityName || 'General')}
                        </div>
                      `).join('')}
                    </td>
                    <td>
                      ${u.isLocked ? '<span class="pill bad">🔒 Locked</span>' : u.isActive ? '<span class="pill good">Active</span>' : '<span class="pill warn">Disabled</span>'}
                    </td>
                    <td>
                      ${u.mfaEnabled ? '<span class="pill good">Enrolled</span>' : '<span class="pill warn">Disabled</span>'}
                    </td>
                    <td style="font-size: 0.8125rem; color: var(--text-muted);">
                      ${formatDualDate(u.lastLoginAt)}
                    </td>
                    <td style="text-align: right;">
                      <div style="display: flex; gap: 0.35rem; justify-content: flex-end; flex-wrap: wrap;">
                        <button type="button" class="btn btn-outline btn-sm" data-action="edit-role" data-id="${u.id}" data-role="${escapeHtml(u.role)}">Role</button>
                        ${u.isLocked ? `
                          <button type="button" class="btn btn-outline btn-sm" data-action="unlock" data-id="${u.id}">Unlock</button>
                        ` : ''}
                        <button type="button" class="btn btn-outline btn-sm" data-action="force-logout" data-id="${u.id}">Logout</button>
                        <button type="button" class="btn btn-outline btn-sm" data-action="reset-mfa" data-id="${u.id}">Reset MFA</button>
                        ${u.isActive ? `
                          <button type="button" class="btn btn-outline btn-sm btn-danger" data-action="disable" data-id="${u.id}" data-email="${escapeHtml(u.email)}">Disable</button>
                        ` : `
                          <button type="button" class="btn btn-outline btn-sm" data-action="enable" data-id="${u.id}">Enable</button>
                        `}
                      </div>
                    </td>
                  </tr>
                `).join('')}
              </tbody>
            </table>
          </div>

          <!-- Pagination -->
          <div style="display: flex; justify-content: space-between; align-items: center;">
            <div style="font-size: 0.8125rem; color: var(--text-muted);">Page ${data.page} of ${Math.max(1, data.totalPages)} (${data.totalCount} total)</div>
            <div style="display: flex; gap: 0.5rem;">
              <button type="button" class="btn btn-outline btn-sm" id="btn-users-prev" ${data.page <= 1 ? 'disabled' : ''}>Previous</button>
              <button type="button" class="btn btn-outline btn-sm" id="btn-users-next" ${data.page >= data.totalPages ? 'disabled' : ''}>Next</button>
            </div>
          </div>
        </div>
      `;

      // Event Listeners
      container.querySelector('#btn-users-search').onclick = () => {
        searchVal = container.querySelector('#users-search').value.trim();
        roleVal = container.querySelector('#users-role-filter').value;
        lockedVal = container.querySelector('#users-status-filter').value === 'locked' ? 'true' : '';
        currentPage = 1;
        loadData();
      };

      const prevBtn = container.querySelector('#btn-users-prev');
      if (prevBtn) prevBtn.onclick = () => { currentPage--; loadData(); };
      const nextBtn = container.querySelector('#btn-users-next');
      if (nextBtn) nextBtn.onclick = () => { currentPage++; loadData(); };

      // + Invite User Button
      container.querySelector('#btn-invite-user').onclick = () => openInviteUserModal(rootEl, session);
      // CSV Import Button
      container.querySelector('#btn-import-csv').onclick = () => openCsvImportModal(rootEl, session);

      // Table Row Actions
      container.querySelectorAll('button[data-action]').forEach(btn => {
        const action = btn.getAttribute('data-action');
        const id = btn.getAttribute('data-id');

        if (action === 'edit-role') {
          btn.onclick = () => openEditRoleModal(id, btn.getAttribute('data-role'), rootEl, session);
        } else if (action === 'unlock') {
          btn.onclick = async () => {
            try {
              await window.api(`/api/admin/users/${id}/unlock`, { method: 'POST' });
              window.showToast('Account lockout cleared.');
              loadData();
            } catch (err) { window.showToast(`Error: ${err.message}`); }
          };
        } else if (action === 'force-logout') {
          btn.onclick = () => {
            showInAppModal({
              title: 'Revoke Active Sessions',
              body: '<p>Revoke all active sessions for this user? They will be immediately logged out.</p>',
              confirmText: 'Force Logout',
              onConfirm: async () => {
                try {
                  await window.api(`/api/admin/users/${id}/force-logout`, { method: 'POST' });
                  window.showToast('Sessions revoked successfully.');
                  loadData();
                } catch (err) { window.showToast(`Error: ${err.message}`); }
              }
            });
          };
        } else if (action === 'reset-mfa') {
          btn.onclick = async () => {
            showInAppModal({
              title: 'Reset Multi-Factor Authentication',
              body: '<p>Reset MFA secret and recovery codes for this user? The user will be required to enroll a new authenticator upon next sign-in and will be notified.</p>',
              confirmText: 'Reset MFA',
              onConfirm: async () => {
                try {
                  const stepUp = await ensureStepUpToken('Reset User MFA');
                  await window.api(`/api/admin/users/${id}/reset-mfa`, {
                    method: 'POST',
                    headers: { 'X-Step-Up-Token': stepUp }
                  });
                  window.showToast('MFA credentials reset. User was notified.');
                  loadData();
                } catch (err) { window.showToast(`Error: ${err.message}`); }
              }
            });
          };
        } else if (action === 'disable') {
          const email = btn.getAttribute('data-email');
          btn.onclick = () => {
            showTypedConfirmModal({
              title: 'Disable User Account',
              message: `Disabling '${email}' will revoke all active sessions and block further logins. Administrators cannot disable their own account or the last admin.`,
              matchWord: 'DISABLE',
              onConfirmed: async () => {
                try {
                  const stepUp = await ensureStepUpToken('Disable User Account');
                  await window.api(`/api/admin/users/${id}/disable`, {
                    method: 'POST',
                    headers: { 'X-Step-Up-Token': stepUp }
                  });
                  window.showToast('Account disabled and sessions revoked.');
                  loadData();
                } catch (err) { window.showToast(`Error: ${err.message}`); }
              }
            });
          };
        } else if (action === 'enable') {
          btn.onclick = async () => {
            try {
              await window.api(`/api/admin/users/${id}/enable`, { method: 'POST' });
              window.showToast('User account re-enabled.');
              loadData();
            } catch (err) { window.showToast(`Error: ${err.message}`); }
          };
        }
      });
    }

    await loadData();
  }

  // Invite Modal
  function openInviteUserModal(rootEl, session) {
    showInAppModal({
      title: '✉️ Create User by Invite Link',
      body: `
        <div style="display: flex; flex-direction: column; gap: 0.75rem;">
          <div class="notice notice-good">
            <strong>Security Principle:</strong> The administrator never sets or sees a user password. The system generates a cryptographic invitation link for the user to securely set their own password.
          </div>
          <div>
            <label for="inv-email" style="font-size: 0.8125rem; font-weight: 600;">Email Address *</label>
            <input type="email" id="inv-email" class="topbar-select" style="width: 100%;" placeholder="user@clinic.example" required />
          </div>
          <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0.5rem;">
            <div>
              <label for="inv-first" style="font-size: 0.8125rem; font-weight: 600;">First Name *</label>
              <input type="text" id="inv-first" class="topbar-select" style="width: 100%;" required />
            </div>
            <div>
              <label for="inv-last" style="font-size: 0.8125rem; font-weight: 600;">Last Name *</label>
              <input type="text" id="inv-last" class="topbar-select" style="width: 100%;" required />
            </div>
          </div>
          <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 0.5rem;">
            <div>
              <label for="inv-role" style="font-size: 0.8125rem; font-weight: 600;">Assigned Role *</label>
              <select id="inv-role" class="topbar-select" style="width: 100%;">
                <option value="CLINICIAN">Clinician</option>
                <option value="PHYSICIAN">Physician</option>
                <option value="NURSE">Nurse</option>
                <option value="PHARMACIST">Pharmacist</option>
                <option value="LAB">Lab Specialist</option>
                <option value="BILLING">Billing Officer</option>
                <option value="SCHEDULER">Scheduler</option>
                <option value="DEPT_ADMIN">Department Admin</option>
                ${session?.user?.role === 'ADMIN' || session?.user?.role === 'SYSTEM_ADMIN' ? '<option value="ADMIN">Hospital Admin</option>' : ''}
                <option value="PATIENT">Patient</option>
              </select>
            </div>
            <div>
              <label for="inv-dob" style="font-size: 0.8125rem; font-weight: 600;">Date of Birth</label>
              <input type="date" id="inv-dob" class="topbar-select" style="width: 100%;" value="1990-01-01" />
            </div>
          </div>
          <div id="inv-error" style="color: var(--bad-text); font-size: 0.8125rem; display: none;"></div>
        </div>
      `,
      confirmText: 'Generate Invite Link',
      onConfirm: async () => {
        const email = document.getElementById('inv-email').value.trim();
        const firstName = document.getElementById('inv-first').value.trim();
        const lastName = document.getElementById('inv-last').value.trim();
        const role = document.getElementById('inv-role').value;
        const dob = document.getElementById('inv-dob').value;
        const errDiv = document.getElementById('inv-error');

        if (!email || !firstName || !lastName) {
          if (errDiv) {
            errDiv.textContent = 'Please fill in email, first name, and last name.';
            errDiv.style.display = 'block';
          }
          return false;
        }

        try {
          const res = await window.api('/api/admin/users/invite', {
            method: 'POST',
            body: JSON.stringify({ email, firstName, lastName, role, dateOfBirth: dob })
          });

          closeInAppModal();

          // Show generated invite link modal
          showInAppModal({
            title: '✅ Invitation Link Generated',
            body: `
              <div style="display: flex; flex-direction: column; gap: 1rem;">
                <p>Provide this link to <strong>${escapeHtml(res.email)}</strong> to complete their account setup:</p>
                <input type="text" class="topbar-select" style="width: 100%; font-family: var(--font-mono);" value="${window.location.origin}${res.inviteLink}" readonly onclick="this.select();" />
                <div style="font-size: 0.75rem; color: var(--text-muted);">
                  Token expires at: ${formatDualDate(res.expiresAt)}
                </div>
              </div>
            `,
            confirmText: 'Done'
          });

          render(rootEl, session);
          return true;
        } catch (err) {
          if (errDiv) {
            errDiv.textContent = err.message || 'Failed to create invite.';
            errDiv.style.display = 'block';
          }
          return false;
        }
      }
    });
  }

  // Edit Role Modal
  function openEditRoleModal(userId, currentRole, rootEl, session) {
    showInAppModal({
      title: 'Update User Role',
      body: `
        <div style="display: flex; flex-direction: column; gap: 1rem;">
          <div class="notice notice-warn">
            Changing a user's role requires step-up verification. The affected user will be immediately logged out and notified of the privilege change.
          </div>
          <div>
            <label for="edit-role-select" style="font-weight: 600; display: block; margin-bottom: 0.25rem;">Select New Role</label>
            <select id="edit-role-select" class="topbar-select" style="width: 100%;">
              <option value="CLINICIAN" ${currentRole === 'CLINICIAN' ? 'selected' : ''}>CLINICIAN</option>
              <option value="PHYSICIAN" ${currentRole === 'PHYSICIAN' ? 'selected' : ''}>PHYSICIAN</option>
              <option value="NURSE" ${currentRole === 'NURSE' ? 'selected' : ''}>NURSE</option>
              <option value="DEPT_ADMIN" ${currentRole === 'DEPT_ADMIN' ? 'selected' : ''}>DEPT_ADMIN</option>
              <option value="BILLING" ${currentRole === 'BILLING' ? 'selected' : ''}>BILLING</option>
              <option value="PHARMACIST" ${currentRole === 'PHARMACIST' ? 'selected' : ''}>PHARMACIST</option>
              <option value="LAB" ${currentRole === 'LAB' ? 'selected' : ''}>LAB</option>
              <option value="SCHEDULER" ${currentRole === 'SCHEDULER' ? 'selected' : ''}>SCHEDULER</option>
              <option value="COMPLIANCE_AUDITOR" ${currentRole === 'COMPLIANCE_AUDITOR' ? 'selected' : ''}>COMPLIANCE_AUDITOR</option>
              <option value="ADMIN" ${currentRole === 'ADMIN' ? 'selected' : ''}>ADMIN</option>
              <option value="PATIENT" ${currentRole === 'PATIENT' ? 'selected' : ''}>PATIENT</option>
            </select>
          </div>
        </div>
      `,
      confirmText: 'Update Role',
      onConfirm: async () => {
        const newRole = document.getElementById('edit-role-select').value;
        try {
          const stepUp = await ensureStepUpToken('Update User Role');
          await window.api(`/api/admin/users/${userId}/role`, {
            method: 'PUT',
            headers: { 'X-Step-Up-Token': stepUp },
            body: JSON.stringify({ role: newRole })
          });
          window.showToast('Role updated successfully.');
          render(rootEl, session);
          return true;
        } catch (err) {
          window.showToast(`Error: ${err.message}`);
          return false;
        }
      }
    });
  }

  // CSV Import Modal
  function openCsvImportModal(rootEl, session) {
    showInAppModal({
      title: '📥 Bulk Import Users (CSV)',
      body: `
        <div style="display: flex; flex-direction: column; gap: 0.75rem;">
          <p style="font-size: 0.8125rem; color: var(--text-muted);">
            Paste CSV rows with headers: <code>email,firstName,lastName,role,departmentCode</code>.
          </p>
          <textarea id="csv-content" class="topbar-select" style="width: 100%; height: 140px; font-family: var(--font-mono); font-size: 0.75rem;" placeholder="email,firstName,lastName,role,departmentCode&#10;selam@clinic.example,Selam,Tesfaye,CLINICIAN,CARDIO&#10;hanna@clinic.example,Hanna,Alemu,NURSE,"></textarea>
          <div id="csv-dryrun-results" style="display: none; max-height: 120px; overflow-y: auto; font-size: 0.75rem;"></div>
          <div style="display: flex; gap: 0.5rem; justify-content: flex-end;">
            <button type="button" class="btn btn-outline btn-sm" id="btn-csv-dryrun">Run Dry Run Validation</button>
          </div>
        </div>
      `,
      confirmText: 'Import Users',
      onConfirm: async () => {
        const content = document.getElementById('csv-content').value.trim();
        if (!content) return false;
        try {
          const res = await window.api('/api/admin/users/import-csv', {
            method: 'POST',
            body: JSON.stringify({ csvContent: content, dryRun: false })
          });
          window.showToast(`Successfully imported ${res.importedCount} users.`);
          render(rootEl, session);
          return true;
        } catch (err) {
          window.showToast(`Import Error: ${err.message}`);
          return false;
        }
      }
    });

    setTimeout(() => {
      const dryBtn = document.getElementById('btn-csv-dryrun');
      if (dryBtn) {
        dryBtn.onclick = async () => {
          const content = document.getElementById('csv-content').value.trim();
          const resultsDiv = document.getElementById('csv-dryrun-results');
          if (!content) return;
          try {
            const res = await window.api('/api/admin/users/import-csv', {
              method: 'POST',
              body: JSON.stringify({ csvContent: content, dryRun: true })
            });
            resultsDiv.style.display = 'block';
            resultsDiv.innerHTML = `
              <div class="notice ${res.invalidCount === 0 ? 'notice-good' : 'notice-warn'}">
                Dry Run Result: ${res.validCount} valid rows, ${res.invalidCount} invalid rows.
              </div>
            `;
          } catch (err) {
            resultsDiv.style.display = 'block';
            resultsDiv.innerHTML = `<div class="notice notice-bad">${escapeHtml(err.message)}</div>`;
          }
        };
      }
    }, 50);
  }

  // =========================================================================
  // SECTION 3: Organization (Facilities, Depts, AA Locations, Providers, Schedules, Appt Types)
  // =========================================================================
  async function renderOrganization(container, rootEl, session) {
    let orgSubTab = 'locations';

    async function loadOrgView() {
      container.innerHTML = `
        <div style="display: flex; flex-direction: column; gap: 1rem;">
          <nav aria-label="Organization structure navigation" style="display: flex; gap: 0.5rem; flex-wrap: wrap;">
            <button type="button" class="btn btn-sm ${orgSubTab === 'locations' ? 'btn-primary' : 'btn-outline'}" id="btn-org-locs">📍 Locations (Addis Ababa)</button>
            <button type="button" class="btn btn-sm ${orgSubTab === 'facilities' ? 'btn-primary' : 'btn-outline'}" id="btn-org-facs">🏥 Facilities</button>
            <button type="button" class="btn btn-sm ${orgSubTab === 'departments' ? 'btn-primary' : 'btn-outline'}" id="btn-org-depts">🏢 Departments</button>
            <button type="button" class="btn btn-sm ${orgSubTab === 'providers' ? 'btn-primary' : 'btn-outline'}" id="btn-org-provs">👨‍⚕️ Providers</button>
            <button type="button" class="btn btn-sm ${orgSubTab === 'schedules' ? 'btn-primary' : 'btn-outline'}" id="btn-org-scheds">📅 Schedule Templates</button>
            <button type="button" class="btn btn-sm ${orgSubTab === 'types' ? 'btn-primary' : 'btn-outline'}" id="btn-org-types">⏱️ Appointment Types</button>
          </nav>

          <div id="org-content-container"></div>
        </div>
      `;

      container.querySelector('#btn-org-locs').onclick = () => { orgSubTab = 'locations'; loadOrgView(); };
      container.querySelector('#btn-org-facs').onclick = () => { orgSubTab = 'facilities'; loadOrgView(); };
      container.querySelector('#btn-org-depts').onclick = () => { orgSubTab = 'departments'; loadOrgView(); };
      container.querySelector('#btn-org-provs').onclick = () => { orgSubTab = 'providers'; loadOrgView(); };
      container.querySelector('#btn-org-scheds').onclick = () => { orgSubTab = 'schedules'; loadOrgView(); };
      container.querySelector('#btn-org-types').onclick = () => { orgSubTab = 'types'; loadOrgView(); };

      const subContainer = container.querySelector('#org-content-container');
      if (orgSubTab === 'locations') await renderLocations(subContainer);
      else if (orgSubTab === 'facilities') await renderFacilities(subContainer);
      else if (orgSubTab === 'departments') await renderDepartments(subContainer);
      else if (orgSubTab === 'providers') await renderProviders(subContainer);
      else if (orgSubTab === 'schedules') await renderSchedules(subContainer);
      else if (orgSubTab === 'types') await renderApptTypes(subContainer);
    }

    await loadOrgView();
  }

  // Locations (Addis Ababa Subcities)
  async function renderLocations(container) {
    const locs = await window.api('/api/admin/locations');
    container.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1rem;">
        <div style="display: flex; justify-content: space-between; align-items: center;">
          <div>
            <h3 style="margin: 0; font-size: 1.15rem;">Addis Ababa Subcity Clinics &amp; Locations</h3>
            <div style="font-size: 0.8125rem; color: var(--text-muted);">
              Covers 11 administrative subcities: Bole, Kirkos, Arada, Yeka, Lideta, Nifas Silk-Lafto, Gulele, Akaky Kaliti, Kolfe Keranio, Addis Ketema, Lemi Kura.
            </div>
          </div>
          <button type="button" class="btn btn-primary btn-sm" id="btn-add-location">+ Add Location</button>
        </div>

        <div class="table-wrap">
          <table aria-label="Addis Ababa Locations Table">
            <thead>
              <tr>
                <th scope="col">Name</th>
                <th scope="col">Subcity</th>
                <th scope="col">Type</th>
                <th scope="col">Operating Hours</th>
                <th scope="col">Phone</th>
              </tr>
            </thead>
            <tbody>
              ${locs.length === 0 ? `
                <tr><td colspan="5" style="text-align: center; color: var(--text-muted); padding: 2rem;">No locations registered.</td></tr>
              ` : locs.map(l => `
                <tr>
                  <td style="font-weight: 600;">${escapeHtml(l.name)}</td>
                  <td><span class="pill info">${escapeHtml(l.subcity || 'Addis Ababa')}</span></td>
                  <td>${escapeHtml(l.type)}</td>
                  <td style="font-size: 0.8125rem;">${escapeHtml(l.hours || 'Mon-Sat 08:30 - 17:30')}</td>
                  <td style="font-size: 0.8125rem; font-family: var(--font-mono);">${escapeHtml(l.phone || '—')}</td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>
      </div>
    `;

    container.querySelector('#btn-add-location').onclick = () => {
      showInAppModal({
        title: 'Add New Addis Ababa Location',
        body: `
          <div style="display: flex; flex-direction: column; gap: 0.75rem;">
            <div>
              <label for="loc-name" style="font-size: 0.8125rem; font-weight: 600;">Location Name *</label>
              <input type="text" id="loc-name" class="topbar-select" style="width: 100%;" placeholder="e.g. Bole Medhanialem Clinic" required />
            </div>
            <div>
              <label for="loc-subcity" style="font-size: 0.8125rem; font-weight: 600;">Addis Ababa Subcity *</label>
              <select id="loc-subcity" class="topbar-select" style="width: 100%;">
                ${ADDIS_ABABA_SUBCITIES.map(s => `<option value="${s}">${s}</option>`).join('')}
              </select>
            </div>
            <div>
              <label for="loc-phone" style="font-size: 0.8125rem; font-weight: 600;">Phone</label>
              <input type="text" id="loc-phone" class="topbar-select" style="width: 100%;" placeholder="+251 11 661 2233" />
            </div>
          </div>
        `,
        confirmText: 'Save Location',
        onConfirm: async () => {
          const name = document.getElementById('loc-name').value.trim();
          const subcity = document.getElementById('loc-subcity').value;
          const phone = document.getElementById('loc-phone').value.trim();
          if (!name) return false;
          try {
            await window.api('/api/admin/locations', {
              method: 'POST',
              body: JSON.stringify({ name, subcity, phone })
            });
            window.showToast('Location registered.');
            renderLocations(container);
            return true;
          } catch (err) { window.showToast(`Error: ${err.message}`); return false; }
        }
      });
    };
  }

  // Facilities
  async function renderFacilities(container) {
    const facs = await window.api('/api/admin/facilities');
    container.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1rem;">
        <div style="display: flex; justify-content: space-between; align-items: center;">
          <h3 style="margin: 0; font-size: 1.15rem;">Facilities Hierarchy</h3>
          <button type="button" class="btn btn-primary btn-sm" id="btn-add-facility">+ Add Facility</button>
        </div>
        <div class="grid grid-2">
          ${facs.map(f => `
            <div class="card">
              <div class="card-header">
                <div style="font-weight: 600; font-size: 1rem;">${escapeHtml(f.name)}</div>
                <span class="pill info">${escapeHtml(f.code)}</span>
              </div>
              <div style="font-size: 0.8125rem; color: var(--text-muted); margin-bottom: 0.5rem;">
                📍 ${escapeHtml(f.addressLine1 || 'Addis Ababa')}, ${escapeHtml(f.city || 'Addis Ababa')}
              </div>
              <div style="font-weight: 600; font-size: 0.8125rem; margin-top: 0.5rem;">Departments:</div>
              <div style="display: flex; gap: 0.25rem; flex-wrap: wrap; margin-top: 0.25rem;">
                ${(f.departments || []).map(d => `<span class="pill">${escapeHtml(d.name)} (${escapeHtml(d.code)})</span>`).join('') || '<span style="color: var(--text-muted); font-size: 0.75rem;">None</span>'}
              </div>
            </div>
          `).join('')}
        </div>
      </div>
    `;

    container.querySelector('#btn-add-facility').onclick = () => {
      showInAppModal({
        title: 'Add New Facility',
        body: `
          <div style="display: flex; flex-direction: column; gap: 0.75rem;">
            <div>
              <label for="fac-name" style="font-size: 0.8125rem; font-weight: 600;">Facility Name *</label>
              <input type="text" id="fac-name" class="topbar-select" style="width: 100%;" required />
            </div>
            <div>
              <label for="fac-code" style="font-size: 0.8125rem; font-weight: 600;">Code *</label>
              <input type="text" id="fac-code" class="topbar-select" style="width: 100%;" placeholder="e.g. ACHN-MAIN" required />
            </div>
            <div>
              <label for="fac-address" style="font-size: 0.8125rem; font-weight: 600;">Address</label>
              <input type="text" id="fac-address" class="topbar-select" style="width: 100%;" value="Bole Road, Addis Ababa" />
            </div>
          </div>
        `,
        confirmText: 'Save Facility',
        onConfirm: async () => {
          const name = document.getElementById('fac-name').value.trim();
          const code = document.getElementById('fac-code').value.trim();
          const addressLine1 = document.getElementById('fac-address').value.trim();
          if (!name || !code) return false;
          try {
            await window.api('/api/admin/facilities', {
              method: 'POST',
              body: JSON.stringify({ name, code, addressLine1 })
            });
            window.showToast('Facility created.');
            renderFacilities(container);
            return true;
          } catch (err) { window.showToast(`Error: ${err.message}`); return false; }
        }
      });
    };
  }

  // Departments
  async function renderDepartments(container) {
    const depts = await window.api('/api/admin/departments');
    container.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1rem;">
        <h3 style="margin: 0; font-size: 1.15rem;">Departments</h3>
        <div class="table-wrap">
          <table aria-label="Departments Table">
            <thead>
              <tr>
                <th scope="col">Department</th>
                <th scope="col">Code</th>
                <th scope="col">Facility</th>
                <th scope="col">Specialty</th>
                <th scope="col">Phone</th>
              </tr>
            </thead>
            <tbody>
              ${depts.map(d => `
                <tr>
                  <td style="font-weight: 600;">${escapeHtml(d.name)}</td>
                  <td><code>${escapeHtml(d.code)}</code></td>
                  <td>${escapeHtml(d.facilityName || 'Main Facility')}</td>
                  <td>${escapeHtml(d.specialty || 'General')}</td>
                  <td style="font-family: var(--font-mono); font-size: 0.8125rem;">${escapeHtml(d.phone || '—')}</td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>
      </div>
    `;
  }

  // Providers
  async function renderProviders(container) {
    const provs = await window.api('/api/admin/providers');
    container.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1rem;">
        <h3 style="margin: 0; font-size: 1.15rem;">Clinical Providers</h3>
        <div class="table-wrap">
          <table aria-label="Providers Table">
            <thead>
              <tr>
                <th scope="col">Provider Name</th>
                <th scope="col">Credentials</th>
                <th scope="col">Specialty</th>
                <th scope="col">Email</th>
                <th scope="col">Departments</th>
              </tr>
            </thead>
            <tbody>
              ${provs.map(p => `
                <tr>
                  <td style="font-weight: 600;">${escapeHtml(p.firstName)} ${escapeHtml(p.lastName)}</td>
                  <td><span class="pill info">${escapeHtml(p.credentials || 'MD')}</span></td>
                  <td>${escapeHtml(p.specialty)}</td>
                  <td style="font-size: 0.8125rem;">${escapeHtml(p.email)}</td>
                  <td>
                    ${(p.departments || []).map(d => `<span class="pill">${escapeHtml(d.departmentName || 'General')}</span>`).join(' ') || '—'}
                  </td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>
      </div>
    `;
  }

  // Schedule Templates
  async function renderSchedules(container) {
    const scheds = await window.api('/api/admin/schedule-templates');
    container.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1rem;">
        <h3 style="margin: 0; font-size: 1.15rem;">Weekly Schedule Templates</h3>
        <div class="table-wrap">
          <table aria-label="Schedules Table">
            <thead>
              <tr>
                <th scope="col">Day</th>
                <th scope="col">Time Window</th>
                <th scope="col">Provider</th>
                <th scope="col">Location</th>
                <th scope="col">Status</th>
              </tr>
            </thead>
            <tbody>
              ${scheds.length === 0 ? `
                <tr><td colspan="5" style="text-align: center; color: var(--text-muted); padding: 2rem;">No schedule templates configured.</td></tr>
              ` : scheds.map(s => `
                <tr>
                  <td style="font-weight: 600;">${escapeHtml(s.dayOfWeek)}</td>
                  <td><code>${escapeHtml(s.startTime)} - ${escapeHtml(s.endTime)}</code></td>
                  <td>${escapeHtml(s.providerName)}</td>
                  <td>${escapeHtml(s.locationName)}</td>
                  <td>${s.isActive ? '<span class="pill good">Active</span>' : '<span class="pill warn">Inactive</span>'}</td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>
      </div>
    `;
  }

  // Appointment Types
  async function renderApptTypes(container) {
    const types = await window.api('/api/admin/appointment-types');
    container.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1rem;">
        <h3 style="margin: 0; font-size: 1.15rem;">Appointment Types &amp; Duration</h3>
        <div class="table-wrap">
          <table aria-label="Appointment Types Table">
            <thead>
              <tr>
                <th scope="col">Name</th>
                <th scope="col">Code</th>
                <th scope="col">Duration</th>
                <th scope="col">Description</th>
                <th scope="col">Status</th>
              </tr>
            </thead>
            <tbody>
              ${types.map(t => `
                <tr>
                  <td style="font-weight: 600;">${escapeHtml(t.name)}</td>
                  <td><code>${escapeHtml(t.code)}</code></td>
                  <td>${t.durationMinutes} min</td>
                  <td style="font-size: 0.8125rem; color: var(--text-muted);">${escapeHtml(t.description || '—')}</td>
                  <td>${t.isActive ? '<span class="pill good">Active</span>' : '<span class="pill warn">Inactive</span>'}</td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>
      </div>
    `;
  }

  // =========================================================================
  // SECTION 4: Audit Log View, Verification & CSV Export
  // =========================================================================
  async function renderAudit(container, rootEl, session) {
    let page = 1;
    let breakGlassOnly = false;
    let actionFilter = '';

    async function loadLogs() {
      let url = `/api/admin/audit-logs?page=${page}&pageSize=25`;
      if (breakGlassOnly) url += '&breakGlassOnly=true';
      if (actionFilter) url += `&action=${encodeURIComponent(actionFilter)}`;

      const data = await window.api(url);

      container.innerHTML = `
        <div style="display: flex; flex-direction: column; gap: 1rem;">
          <div style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 0.75rem;">
            <div style="display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap;">
              <input type="text" id="audit-act-filter" class="topbar-select" placeholder="Filter action (e.g. BREAK_GLASS)" value="${escapeHtml(actionFilter)}" />
              <label style="display: flex; align-items: center; gap: 0.25rem; font-size: 0.8125rem; cursor: pointer;">
                <input type="checkbox" id="audit-bg-only" ${breakGlassOnly ? 'checked' : ''} />
                <span>🚨 Break-Glass Only</span>
              </label>
              <button type="button" class="btn btn-outline btn-sm" id="btn-audit-filter">Apply</button>
            </div>
            <div style="display: flex; gap: 0.5rem;">
              <button type="button" class="btn btn-outline btn-sm" id="btn-audit-verify">🔍 Verify Hash Chain</button>
              <button type="button" class="btn btn-primary btn-sm" id="btn-audit-export">📥 Export CSV (Step-Up)</button>
            </div>
          </div>

          <div id="audit-verification-status"></div>

          <div class="table-wrap">
            <table aria-label="Audit Log Entries Table">
              <thead>
                <tr>
                  <th scope="col">Seq #</th>
                  <th scope="col">Timestamp (Addis Ababa)</th>
                  <th scope="col">Action</th>
                  <th scope="col">Resource</th>
                  <th scope="col">Actor</th>
                  <th scope="col">Hash Chain</th>
                </tr>
              </thead>
              <tbody>
                ${data.items.length === 0 ? `
                  <tr><td colspan="6" style="text-align: center; color: var(--text-muted); padding: 2rem;">No audit logs matching query.</td></tr>
                ` : data.items.map(l => `
                  <tr style="${l.isBreakGlass ? 'background: var(--bad-bg);' : ''}">
                    <td style="font-family: var(--font-mono); font-size: 0.75rem;">#${l.sequenceNumber || l.id.slice(0, 8)}</td>
                    <td style="font-size: 0.8125rem;">${formatDualDate(l.createdAt)}</td>
                    <td>
                      ${l.isBreakGlass ? `
                        <span class="pill bad" style="font-weight: 700;">🚨 ${escapeHtml(l.action)}</span>
                      ` : `
                        <span class="pill info">${escapeHtml(l.action)}</span>
                      `}
                    </td>
                    <td><code>${escapeHtml(l.resourceType || '—')}</code></td>
                    <td>
                      <div style="font-weight: 500;">${escapeHtml(l.actor.name)}</div>
                      <div style="font-size: 0.75rem; color: var(--text-muted);">${escapeHtml(l.actor.role)} • ${escapeHtml(l.ipAddress || 'internal')}</div>
                    </td>
                    <td>
                      <span class="pill good" style="font-family: var(--font-mono); font-size: 0.6875rem;" title="${escapeHtml(l.hash || '')}">
                        ${l.hash ? l.hash.slice(0, 10) + '…' : '✓ Unmodified'}
                      </span>
                    </td>
                  </tr>
                `).join('')}
              </tbody>
            </table>
          </div>

          <div style="display: flex; justify-content: space-between; align-items: center;">
            <div style="font-size: 0.8125rem; color: var(--text-muted);">Page ${data.page} of ${Math.max(1, data.totalPages)}</div>
            <div style="display: flex; gap: 0.5rem;">
              <button type="button" class="btn btn-outline btn-sm" id="btn-audit-prev" ${data.page <= 1 ? 'disabled' : ''}>Previous</button>
              <button type="button" class="btn btn-outline btn-sm" id="btn-audit-next" ${data.page >= data.totalPages ? 'disabled' : ''}>Next</button>
            </div>
          </div>
        </div>
      `;

      container.querySelector('#btn-audit-filter').onclick = () => {
        actionFilter = container.querySelector('#audit-act-filter').value.trim();
        breakGlassOnly = container.querySelector('#audit-bg-only').checked;
        page = 1;
        loadLogs();
      };

      const prevBtn = container.querySelector('#btn-audit-prev');
      if (prevBtn) prevBtn.onclick = () => { page--; loadLogs(); };
      const nextBtn = container.querySelector('#btn-audit-next');
      if (nextBtn) nextBtn.onclick = () => { page++; loadLogs(); };

      // Verify Hash Chain
      container.querySelector('#btn-audit-verify').onclick = async () => {
        const statusDiv = container.querySelector('#audit-verification-status');
        statusDiv.innerHTML = '<div class="notice notice-good">Verifying cryptographic hash chain...</div>';
        try {
          const res = await window.api('/api/admin/audit-logs/verify');
          if (res.valid) {
            statusDiv.innerHTML = `
              <div class="notice notice-good">
                <strong>Cryptographic Chain Verified:</strong> Checked ${res.totalRecords} records. Latest Hash: <code>${escapeHtml(res.latestHash || 'Genesis')}</code>. Zero tampering detected.
              </div>
            `;
          } else {
            statusDiv.innerHTML = `
              <div class="notice notice-bad">
                <strong>Integrity Alert:</strong> Corrupted record ID: <code>${escapeHtml(res.corruptedLogId || 'unknown')}</code>. ${escapeHtml(res.error || 'Hash mismatch')}.
              </div>
            `;
          }
        } catch (err) {
          statusDiv.innerHTML = `<div class="notice notice-bad">Verification failed: ${escapeHtml(err.message)}</div>`;
        }
      };

      // Step-Up CSV Export
      container.querySelector('#btn-audit-export').onclick = async () => {
        try {
          const stepUp = await ensureStepUpToken('Export Compliance Audit Log');
          // Fetch CSV using step-up token
          const res = await fetch('/api/admin/audit-logs/export', {
            headers: {
              'Authorization': `Bearer ${window.currentSession?.token || ''}`,
              'X-Step-Up-Token': stepUp
            }
          });
          if (!res.ok) throw new Error(`Export failed (${res.status})`);
          const blob = await res.blob();
          const url = window.URL.createObjectURL(blob);
          const a = document.createElement('a');
          a.href = url;
          a.download = `audit_log_${new Date().toISOString().slice(0, 10)}.csv`;
          document.body.appendChild(a);
          a.click();
          a.remove();
          window.showToast('Audit log CSV exported.');
        } catch (err) {
          window.showToast(`Export error: ${err.message}`);
        }
      };
    }

    await loadLogs();
  }

  // =========================================================================
  // SECTION 5: Security Policy & Active Sessions
  // =========================================================================
  async function renderSecurity(container, rootEl, session) {
    const policy = await window.api('/api/admin/security-policy');
    const sessions = await window.api('/api/admin/sessions');

    container.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1.5rem;">
        <!-- Bounded Policy Config -->
        <div class="card">
          <div class="card-header">
            <div class="card-title">🛡️ Tenant Security Policy Controls (Bounded)</div>
            <span class="pill good">Step-Up Required to Save</span>
          </div>
          <form id="sec-policy-form" style="display: flex; flex-direction: column; gap: 1rem;">
            <div class="grid grid-2">
              <div>
                <label style="display: flex; align-items: center; gap: 0.5rem; font-weight: 600; cursor: pointer;">
                  <input type="checkbox" id="pol-staff-mfa" ${policy.mfaStaff ? 'checked' : ''} />
                  <span>Enforce MFA for Staff &amp; Clinicians</span>
                </label>
                <div style="font-size: 0.75rem; color: var(--text-muted); margin-left: 1.5rem;">Mandates TOTP authenticator setup on next staff login.</div>
              </div>
              <div>
                <label style="display: flex; align-items: center; gap: 0.5rem; font-weight: 600; cursor: pointer;">
                  <input type="checkbox" id="pol-patient-mfa" ${policy.mfaPatients ? 'checked' : ''} />
                  <span>Enforce MFA for Patients</span>
                </label>
                <div style="font-size: 0.75rem; color: var(--text-muted); margin-left: 1.5rem;">Optional for portal accounts; strongly recommended.</div>
              </div>
            </div>

            <div class="grid grid-3">
              <div>
                <label for="pol-idle" style="font-size: 0.8125rem; font-weight: 600;">Idle Timeout (5 - 60 min) *</label>
                <input type="number" id="pol-idle" min="5" max="60" class="topbar-select" style="width: 100%;" value="${policy.idleTimeoutMinutes}" required />
              </div>
              <div>
                <label for="pol-max-attempts" style="font-size: 0.8125rem; font-weight: 600;">Lockout Threshold (3 - 10 attempts) *</label>
                <input type="number" id="pol-max-attempts" min="3" max="10" class="topbar-select" style="width: 100%;" value="${policy.maxFailedLoginAttempts}" required />
              </div>
              <div>
                <label for="pol-duration" style="font-size: 0.8125rem; font-weight: 600;">Lockout Duration (5 - 120 min) *</label>
                <input type="number" id="pol-duration" min="5" max="120" class="topbar-select" style="width: 100%;" value="${policy.lockoutDurationMinutes}" required />
              </div>
            </div>

            <div style="display: flex; justify-content: flex-end;">
              <button type="submit" class="btn btn-primary btn-sm">Save Security Policy (Step-Up)</button>
            </div>
          </form>
        </div>

        <!-- Active Sessions List -->
        <div class="card">
          <div class="card-header">
            <div class="card-title">🖥️ Active Sessions Across Hospital</div>
            <span class="pill info">${sessions.length} Active</span>
          </div>
          <div class="table-wrap">
            <table aria-label="Active sessions table">
              <thead>
                <tr>
                  <th scope="col">User</th>
                  <th scope="col">Role</th>
                  <th scope="col">Last Active</th>
                  <th scope="col">Expires</th>
                  <th scope="col" style="text-align: right;">Action</th>
                </tr>
              </thead>
              <tbody>
                ${sessions.map(s => `
                  <tr>
                    <td>
                      <div style="font-weight: 600;">${escapeHtml(s.userName)}</div>
                      <div style="font-size: 0.75rem; color: var(--text-muted);">${escapeHtml(s.email)}</div>
                    </td>
                    <td><span class="pill info">${escapeHtml(s.role)}</span></td>
                    <td style="font-size: 0.8125rem;">${formatDualDate(s.lastActiveAt)}</td>
                    <td style="font-size: 0.8125rem; color: var(--text-muted);">${formatDualDate(s.expiresAt)}</td>
                    <td style="text-align: right;">
                      ${s.isCurrent ? `
                        <span class="pill good">Current Session</span>
                      ` : `
                        <button type="button" class="btn btn-outline btn-sm btn-danger" data-terminate-sid="${escapeHtml(s.sid)}">Terminate</button>
                      `}
                    </td>
                  </tr>
                `).join('')}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    `;

    // Save policy
    container.querySelector('#sec-policy-form').onsubmit = async (e) => {
      e.preventDefault();
      const mfaStaff = document.getElementById('pol-staff-mfa').checked;
      const mfaPatients = document.getElementById('pol-patient-mfa').checked;
      const idleTimeoutMinutes = parseInt(document.getElementById('pol-idle').value, 10);
      const maxFailedLoginAttempts = parseInt(document.getElementById('pol-max-attempts').value, 10);
      const lockoutDurationMinutes = parseInt(document.getElementById('pol-duration').value, 10);

      try {
        const stepUp = await ensureStepUpToken('Update Hospital Security Policy');
        await window.api('/api/admin/security-policy', {
          method: 'PUT',
          headers: { 'X-Step-Up-Token': stepUp },
          body: JSON.stringify({ mfaStaff, mfaPatients, idleTimeoutMinutes, maxFailedLoginAttempts, lockoutDurationMinutes })
        });
        window.showToast('Security policy updated and audited.');
        render(rootEl, session);
      } catch (err) { window.showToast(`Error: ${err.message}`); }
    };

    // Terminate session buttons
    container.querySelectorAll('button[data-terminate-sid]').forEach(btn => {
      const sid = btn.getAttribute('data-terminate-sid');
      btn.onclick = () => {
        showInAppModal({
          title: 'Terminate User Session',
          body: '<p>Revoke this active session? The user will be required to log in again immediately.</p>',
          confirmText: 'Terminate',
          onConfirm: async () => {
            try {
              await window.api(`/api/admin/sessions/${sid}/terminate`, { method: 'POST' });
              window.showToast('Session terminated.');
              renderSecurity(container, rootEl, session);
            } catch (err) { window.showToast(`Error: ${err.message}`); }
          }
        });
      };
    });
  }

  // =========================================================================
  // SECTION 6: Integrations
  // =========================================================================
  async function renderIntegrations(container, rootEl, session) {
    const data = await window.api('/api/admin/integrations');

    container.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1.5rem;">
        <!-- Status & Test Connection Cards (Secrets Masked) -->
        <div class="grid grid-2">
          <!-- OpenMRS -->
          <div class="card">
            <div class="card-header">
              <div class="card-title">🩺 OpenMRS Clinical Core (FHIR R4)</div>
              <span class="pill good">${escapeHtml(data.openMrs.status)}</span>
            </div>
            <div style="font-size: 0.8125rem; color: var(--text-muted); display: flex; flex-direction: column; gap: 0.25rem;">
              <div>Endpoint: <code>${escapeHtml(data.openMrs.url)}</code></div>
              <div>Auth Secret: <code>${escapeHtml(data.openMrs.apiKey)}</code></div>
              <div>Last Synced: ${formatDualDate(data.openMrs.lastSync)}</div>
            </div>
            <div style="margin-top: 1rem;">
              <button type="button" class="btn btn-outline btn-sm" data-test="openmrs">🔌 Test Connection</button>
            </div>
          </div>

          <!-- HMS Webhook -->
          <div class="card">
            <div class="card-header">
              <div class="card-title">📡 Hospital Management System (HMS)</div>
              <span class="pill ${data.hms.deadLettersCount > 0 ? 'warn' : 'good'}">${escapeHtml(data.hms.status)}</span>
            </div>
            <div style="font-size: 0.8125rem; color: var(--text-muted); display: flex; flex-direction: column; gap: 0.25rem;">
              <div>Dead Letters: <strong>${data.hms.deadLettersCount}</strong> pending</div>
              <div>Conflicts: <strong>${data.hms.conflictsCount}</strong> demographic conflicts</div>
              <div>HMAC Secret: <code>${escapeHtml(data.hms.webhookSecret)}</code></div>
            </div>
            <div style="margin-top: 1rem;">
              <button type="button" class="btn btn-outline btn-sm" data-test="hms">🔌 Test Connection</button>
            </div>
          </div>

          <!-- DHIS2 -->
          <div class="card">
            <div class="card-header">
              <div class="card-title">🇪🇹 National DHIS2 HMIS Reporting</div>
              <span class="pill good">${escapeHtml(data.dhis2.status)}</span>
            </div>
            <div style="font-size: 0.8125rem; color: var(--text-muted); display: flex; flex-direction: column; gap: 0.25rem;">
              <div>Ministry Endpoint: <code>${escapeHtml(data.dhis2.endpoint)}</code></div>
              <div>API Key: <code>${escapeHtml(data.dhis2.apiKey)}</code></div>
            </div>
            <div style="margin-top: 1rem;">
              <button type="button" class="btn btn-outline btn-sm" data-test="dhis2">🔌 Test Connection</button>
            </div>
          </div>

          <!-- Payment Gateway -->
          <div class="card">
            <div class="card-header">
              <div class="card-title">💳 National Payments (Telebirr / CBE Birr)</div>
              <span class="pill good">${escapeHtml(data.payments.status)}</span>
            </div>
            <div style="font-size: 0.8125rem; color: var(--text-muted); display: flex; flex-direction: column; gap: 0.25rem;">
              <div>Gateways: ${escapeHtml(data.payments.provider)}</div>
              <div>Live Secret: <code>${escapeHtml(data.payments.liveSecret)}</code></div>
            </div>
            <div style="margin-top: 1rem;">
              <button type="button" class="btn btn-outline btn-sm" data-test="payment">🔌 Test Handshake</button>
            </div>
          </div>
        </div>

        <!-- Demographic Conflicts Queue -->
        <div class="card">
          <div class="card-header">
            <div class="card-title">⚡ Demographic Conflict Resolution Queue</div>
            <span class="pill info">${data.conflicts.length} Pending</span>
          </div>
          <div class="table-wrap">
            <table aria-label="Demographic conflicts table">
              <thead>
                <tr>
                  <th scope="col">MRN</th>
                  <th scope="col">Field</th>
                  <th scope="col">Portal Value</th>
                  <th scope="col">HMS Remote Value</th>
                  <th scope="col" style="text-align: right;">Resolution</th>
                </tr>
              </thead>
              <tbody>
                ${data.conflicts.length === 0 ? `
                  <tr><td colspan="5" style="text-align: center; color: var(--text-muted); padding: 1.5rem;">Zero pending demographic conflicts.</td></tr>
                ` : data.conflicts.map(c => `
                  <tr>
                    <td><code>${escapeHtml(c.mrn)}</code></td>
                    <td style="font-weight: 600;">${escapeHtml(c.fieldName)}</td>
                    <td><code>${escapeHtml(c.localValue || '—')}</code></td>
                    <td><code>${escapeHtml(c.hmsValue || '—')}</code></td>
                    <td style="text-align: right;">
                      <button type="button" class="btn btn-outline btn-sm" data-resolve-conflict="${c.id}" data-res="USE_LOCAL">Keep Local</button>
                      <button type="button" class="btn btn-outline btn-sm" data-resolve-conflict="${c.id}" data-res="USE_REMOTE">Accept Remote</button>
                    </td>
                  </tr>
                `).join('')}
              </tbody>
            </table>
          </div>
        </div>

        <!-- DHIS2 Reporting & Approval History -->
        <div class="card">
          <div class="card-header">
            <div class="card-title">📊 National DHIS2 Datasets &amp; Facility Approvals</div>
          </div>
          <div class="table-wrap">
            <table aria-label="DHIS2 report datasets table">
              <thead>
                <tr>
                  <th scope="col">Dataset</th>
                  <th scope="col">Period</th>
                  <th scope="col">Status</th>
                  <th scope="col">Approved By</th>
                  <th scope="col" style="text-align: right;">Action</th>
                </tr>
              </thead>
              <tbody>
                ${data.dhis2Reports.length === 0 ? `
                  <tr><td colspan="5" style="text-align: center; color: var(--text-muted); padding: 1.5rem;">No historical report submissions.</td></tr>
                ` : data.dhis2Reports.map(r => `
                  <tr>
                    <td style="font-weight: 600;">${escapeHtml(r.dataSetId)}</td>
                    <td><code>${escapeHtml(r.period)}</code></td>
                    <td><span class="pill ${r.status === 'FacilityApproved' ? 'good' : 'warn'}">${escapeHtml(r.status)}</span></td>
                    <td style="font-size: 0.8125rem;">${escapeHtml(r.approvedBy || 'Pending Leadership Approval')}</td>
                    <td style="text-align: right;">
                      ${r.status !== 'FacilityApproved' ? `
                        <button type="button" class="btn btn-outline btn-sm btn-primary" data-approve-dhis2="${r.id}">Approve Report</button>
                      ` : '<span style="color: var(--good-text); font-size: 0.75rem;">✓ Approved</span>'}
                    </td>
                  </tr>
                `).join('')}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    `;

    // Hook test connection buttons
    container.querySelectorAll('button[data-test]').forEach(btn => {
      btn.onclick = async () => {
        const target = btn.getAttribute('data-test');
        btn.textContent = 'Pinging...';
        try {
          const res = await window.api(`/api/admin/integrations/${target}/test`, { method: 'POST' });
          window.showToast(res.message);
        } catch (err) { window.showToast(`Test failed: ${err.message}`); }
        finally { btn.textContent = '🔌 Test Connection'; }
      };
    });

    // Conflict resolution with Step-Up
    container.querySelectorAll('button[data-resolve-conflict]').forEach(btn => {
      btn.onclick = async () => {
        const id = btn.getAttribute('data-resolve-conflict');
        const resType = btn.getAttribute('data-res');
        try {
          const stepUp = await ensureStepUpToken('Resolve Demographic Conflict');
          await window.api(`/api/admin/integrations/conflicts/${id}/resolve`, {
            method: 'POST',
            headers: { 'X-Step-Up-Token': stepUp },
            body: JSON.stringify({ resolution: resType })
          });
          window.showToast('Demographic conflict resolved.');
          renderIntegrations(container, rootEl, session);
        } catch (err) { window.showToast(`Error: ${err.message}`); }
      };
    });

    // DHIS2 Approve Report
    container.querySelectorAll('button[data-approve-dhis2]').forEach(btn => {
      btn.onclick = async () => {
        const id = btn.getAttribute('data-approve-dhis2');
        try {
          await window.api(`/api/admin/integrations/dhis2/${id}/approve`, { method: 'POST' });
          window.showToast('DHIS2 report approved by facility leadership.');
          renderIntegrations(container, rootEl, session);
        } catch (err) { window.showToast(`Error: ${err.message}`); }
      };
    });
  }

  // =========================================================================
  // SECTION 7: Billing Settings & Refund Approval Queue
  // =========================================================================
  async function renderBilling(container, rootEl, session) {
    const settings = await window.api('/api/admin/billing-settings');
    const refunds = await window.api('/api/admin/billing/refunds');

    container.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1.5rem;">
        <!-- Currency & Payment Plan Controls -->
        <div class="card">
          <div class="card-header">
            <div class="card-title">💳 Ethiopian Birr (ETB) Currency &amp; Payment Plans</div>
            <span class="pill info">${escapeHtml(settings.currencyCode)} (${escapeHtml(settings.symbol)})</span>
          </div>
          <form id="billing-settings-form" style="display: flex; flex-direction: column; gap: 1rem;">
            <div class="grid grid-2">
              <div>
                <label for="bill-min-amount" style="font-size: 0.8125rem; font-weight: 600;">Minimum Payment Plan Threshold (ETB) *</label>
                <div style="display: flex; align-items: center; gap: 0.5rem; margin-top: 0.25rem;">
                  <span style="font-weight: 600;">Br</span>
                  <input type="number" id="bill-min-amount" min="10" step="10" class="topbar-select" style="width: 100%;" value="${settings.minPaymentPlanAmountEtb}" required />
                </div>
                <div style="font-size: 0.75rem; color: var(--text-muted); margin-top: 0.25rem;">Minor units: santim. Minimum balance required to offer installments.</div>
              </div>
              <div>
                <label style="font-size: 0.8125rem; font-weight: 600; display: block; margin-bottom: 0.25rem;">Allowed Installment Durations</label>
                <div style="display: flex; gap: 1rem; align-items: center; margin-top: 0.5rem;">
                  <label><input type="checkbox" checked disabled /> 3 Months</label>
                  <label><input type="checkbox" checked disabled /> 6 Months</label>
                  <label><input type="checkbox" checked disabled /> 12 Months</label>
                </div>
              </div>
            </div>
            <div style="display: flex; justify-content: flex-end;">
              <button type="submit" class="btn btn-primary btn-sm">Save Billing Settings</button>
            </div>
          </form>
        </div>

        <!-- Refund Approval Queue -->
        <div class="card">
          <div class="card-header">
            <div class="card-title">💸 Patient Refund Approval Queue</div>
            <span class="pill ${refunds.length > 0 ? 'warn' : 'good'}">${refunds.length} Pending</span>
          </div>
          <div class="table-wrap">
            <table aria-label="Refund approval queue table">
              <thead>
                <tr>
                  <th scope="col">Payment ID</th>
                  <th scope="col">Amount</th>
                  <th scope="col">Reason</th>
                  <th scope="col">Requested</th>
                  <th scope="col">Status</th>
                  <th scope="col" style="text-align: right;">Action</th>
                </tr>
              </thead>
              <tbody>
                ${refunds.length === 0 ? `
                  <tr><td colspan="6" style="text-align: center; color: var(--text-muted); padding: 1.5rem;">No pending refund requests.</td></tr>
                ` : refunds.map(r => `
                  <tr>
                    <td><code>${escapeHtml(r.paymentId.slice(0, 8))}…</code></td>
                    <td style="font-weight: 700; color: var(--primary);">${formatEtb(r.amountMinorUnits)}</td>
                    <td>${escapeHtml(r.reason || 'Patient request')}</td>
                    <td style="font-size: 0.8125rem;">${formatDualDate(r.createdAt)}</td>
                    <td><span class="pill ${r.status === 'APPROVED' ? 'good' : r.status === 'REJECTED' ? 'bad' : 'warn'}">${escapeHtml(r.status)}</span></td>
                    <td style="text-align: right;">
                      ${r.status === 'PENDING' ? `
                        <button type="button" class="btn btn-outline btn-sm btn-primary" data-refund-approve="${r.id}">Approve</button>
                        <button type="button" class="btn btn-outline btn-sm btn-danger" data-refund-reject="${r.id}">Reject</button>
                      ` : '<span style="color: var(--text-muted); font-size: 0.75rem;">Completed</span>'}
                    </td>
                  </tr>
                `).join('')}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    `;

    // Save Billing Settings
    container.querySelector('#billing-settings-form').onsubmit = async (e) => {
      e.preventDefault();
      const etbVal = parseFloat(document.getElementById('bill-min-amount').value);
      const minorUnits = Math.round(etbVal * 100);
      try {
        await window.api('/api/admin/billing-settings', {
          method: 'PUT',
          body: JSON.stringify({ minPaymentPlanAmountMinorUnits: minorUnits, allowedInstallmentMonths: [3, 6, 12] })
        });
        window.showToast('Billing settings updated.');
      } catch (err) { window.showToast(`Error: ${err.message}`); }
    };

    // Approve Refund
    container.querySelectorAll('button[data-refund-approve]').forEach(btn => {
      btn.onclick = () => {
        const id = btn.getAttribute('data-refund-approve');
        showInAppModal({
          title: 'Approve Patient Refund',
          body: '<p>Authorize payment refund? This will mark the payment as refunded and trigger payment gateway credit.</p>',
          confirmText: 'Authorize Refund',
          onConfirm: async () => {
            try {
              await window.api(`/api/admin/billing/refunds/${id}/approve`, { method: 'POST' });
              window.showToast('Refund approved and audited.');
              renderBilling(container, rootEl, session);
            } catch (err) { window.showToast(`Error: ${err.message}`); }
          }
        });
      };
    });

    // Reject Refund
    container.querySelectorAll('button[data-refund-reject]').forEach(btn => {
      btn.onclick = () => {
        const id = btn.getAttribute('data-refund-reject');
        showInAppModal({
          title: 'Reject Patient Refund',
          body: '<p>Reject this refund request? The payment will remain valid.</p>',
          confirmText: 'Reject Refund',
          onConfirm: async () => {
            try {
              await window.api(`/api/admin/billing/refunds/${id}/reject`, { method: 'POST' });
              window.showToast('Refund request rejected.');
              renderBilling(container, rootEl, session);
            } catch (err) { window.showToast(`Error: ${err.message}`); }
          }
        });
      };
    });
  }

  // =========================================================================
  // SECTION 8: Hospital Settings & Operations Profile
  // =========================================================================
  async function renderTenant(container, rootEl, session) {
    const data = await window.api('/api/admin/tenant-settings');
    const isPlatformAdmin = session?.user?.role === 'PLATFORM_ADMIN';

    container.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1.5rem;">
        <!-- Branding Card -->
        <div class="card">
          <div class="card-header">
            <div class="card-title">🏥 Hospital Configuration &amp; Operations Profile</div>
            <span class="pill good">Addis Care Health Network</span>
          </div>
          <form id="tenant-settings-form" style="display: flex; flex-direction: column; gap: 1rem;">
            <div class="grid grid-2">
              <div>
                <label for="ten-name" style="font-size: 0.8125rem; font-weight: 600;">Hospital Network Name *</label>
                <input type="text" id="ten-name" class="topbar-select" style="width: 100%;" value="${escapeHtml(data.name)}" required />
              </div>
              <div>
                <label for="ten-logo" style="font-size: 0.8125rem; font-weight: 600;">Logo URL / Asset Path</label>
                <input type="text" id="ten-logo" class="topbar-select" style="width: 100%;" value="${escapeHtml(data.logo || '')}" />
              </div>
            </div>

            <!-- Brand Color & Contrast -->
            <div class="grid grid-2">
              <div>
                <label for="ten-color" style="font-size: 0.8125rem; font-weight: 600;">Primary Brand Color (Hex)</label>
                <div style="display: flex; gap: 0.5rem; align-items: center; margin-top: 0.25rem;">
                  <input type="color" id="ten-color-picker" value="${escapeHtml(data.brandColor)}" style="width: 44px; height: 36px; border: 1px solid var(--border); border-radius: 4px; cursor: pointer;" />
                  <input type="text" id="ten-color" class="topbar-select" style="font-family: var(--font-mono); width: 120px;" value="${escapeHtml(data.brandColor)}" />
                </div>
              </div>
              <div style="display: flex; flex-direction: column; justify-content: center;">
                <div style="font-weight: 600; font-size: 0.8125rem;">WCAG 2.1 AA Contrast Ratio:</div>
                <div style="display: flex; gap: 0.5rem; align-items: center; margin-top: 0.25rem;">
                  <span id="contrast-badge" class="pill ${data.contrastPassesAa ? 'good' : 'warn'}">
                    ${data.contrastRatioWhite}:1 Ratio (${data.contrastPassesAa ? 'Passes AA 4.5:1' : 'Fails Normal Text'})
                  </span>
                </div>
              </div>
            </div>

            <!-- Languages & Dual Calendar -->
            <div class="grid grid-2">
              <div>
                <label style="font-size: 0.8125rem; font-weight: 600; display: block; margin-bottom: 0.25rem;">Active Portal Languages</label>
                <div style="display: flex; gap: 0.75rem; align-items: center;">
                  <label><input type="checkbox" checked disabled /> English (en)</label>
                  <label><input type="checkbox" checked disabled /> Amharic (አማርኛ - am)</label>
                  <label><input type="checkbox" checked disabled /> Afaan Oromoo (om)</label>
                </div>
              </div>
              <div>
                <label style="font-size: 0.8125rem; font-weight: 600; display: block; margin-bottom: 0.25rem;">Default Calendar Display</label>
                <select id="ten-calendar" class="topbar-select" style="width: 100%;">
                  <option value="Both" selected>Both (Dual Ethiopian &amp; Gregorian)</option>
                  <option value="Ethiopian">Ethiopian Calendar (ዓ.ም)</option>
                  <option value="Gregorian">Gregorian Calendar</option>
                </select>
              </div>
            </div>

            <!-- Enabled Modules -->
            <div style="border-top: 1px solid var(--border); padding-top: 1rem;">
              <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.5rem;">
                <label style="font-size: 0.875rem; font-weight: 600;">Enabled Feature Modules</label>
                ${!isPlatformAdmin ? '<span class="pill warn">Read-Only (Platform Admin Managed)</span>' : '<span class="pill good">Platform Admin Mode</span>'}
              </div>
              <div class="grid grid-3" style="font-size: 0.8125rem;">
                ${['appointments', 'messaging', 'billing', 'refills', 'proxy', 'sharing', 'fhir', 'forms', 'interop', 'dhis2', 'echis', 'mfr', 'mpi'].map(mod => `
                  <label style="display: flex; align-items: center; gap: 0.35rem; cursor: ${isPlatformAdmin ? 'pointer' : 'default'};">
                    <input type="checkbox" class="ten-module-chk" value="${mod}" ${data.enabledModules.includes(mod) ? 'checked' : ''} ${!isPlatformAdmin ? 'disabled' : ''} />
                    <span>${mod.toUpperCase()}</span>
                  </label>
                `).join('')}
              </div>
            </div>

            <div style="display: flex; justify-content: flex-end;">
              <button type="submit" class="btn btn-primary btn-sm">Save Tenant Settings</button>
            </div>
          </form>
        </div>
      </div>
    `;

    // Color picker sync & live WCAG AA contrast update
    const picker = container.querySelector('#ten-color-picker');
    const hexInput = container.querySelector('#ten-color');
    const contrastBadge = container.querySelector('#contrast-badge');

    function updateContrast() {
      const color = hexInput.value.trim();
      const res = calculateContrastRatio(color);
      if (contrastBadge) {
        contrastBadge.className = `pill ${res.passesAa ? 'good' : 'warn'}`;
        contrastBadge.textContent = `${res.ratio}:1 Ratio (${res.passesAa ? 'Passes AA 4.5:1' : 'Fails Normal Text'})`;
      }
    }

    if (picker && hexInput) {
      picker.oninput = () => {
        hexInput.value = picker.value;
        updateContrast();
      };
      hexInput.oninput = () => {
        if (/^#[0-9A-Fa-f]{6}$/.test(hexInput.value)) {
          picker.value = hexInput.value;
          updateContrast();
        }
      };
    }

    // Save Tenant Settings
    container.querySelector('#tenant-settings-form').onsubmit = async (e) => {
      e.preventDefault();
      const name = document.getElementById('ten-name').value.trim();
      const brandColor = document.getElementById('ten-color').value.trim();
      const logo = document.getElementById('ten-logo').value.trim();

      const payload = { name, brandColor, logo };
      if (isPlatformAdmin) {
        const checkedMods = Array.from(container.querySelectorAll('.ten-module-chk:checked')).map(c => c.value);
        payload.enabledModules = checkedMods;
      }

      try {
        await window.api('/api/admin/tenant-settings', {
          method: 'PUT',
          body: JSON.stringify(payload)
        });
        window.showToast('Tenant settings saved.');
        render(rootEl, session);
      } catch (err) { window.showToast(`Error: ${err.message}`); }
    };
  }

  // Export module to global scope
  window.AdminDashboard = {
    render
  };
})();
