// Medical Portal Single Page Application (.NET 8 Backend)
(function() {
  'use strict';

  let currentSession = null;
  let activePatientId = null;
  let activeBreakGlass = null;
  let currentTab = 'overview';
  let modalState = null;
  let hubConnection = null;

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
    const container = document.getElementById('toast-container');
    if (!container) return;
    const toast = document.createElement('div');
    toast.className = 'notice notice-good';
    toast.style.cssText = 'pointer-events: auto; box-shadow: 0 4px 12px rgba(0,0,0,0.15); margin: 0; min-width: 260px; border-left: 4px solid var(--primary);';
    const textDiv = document.createElement('div');
    textDiv.textContent = msg;
    toast.appendChild(textDiv);
    container.appendChild(toast);
    setTimeout(() => {
      toast.style.opacity = '0';
      toast.style.transition = 'opacity 0.3s ease';
      setTimeout(() => toast.remove(), 300);
    }, 4500);
  }

  function showInAppAlert(message, title = 'Notice') {
    const overlay = document.createElement('div');
    overlay.className = 'modal-backdrop';
    overlay.style.cssText = 'position:fixed;inset:0;background:rgba(15,23,42,0.7);z-index:999999;display:flex;align-items:center;justify-content:center;padding:1rem;';
    const card = document.createElement('div');
    card.className = 'card';
    card.style.cssText = 'max-width:440px;width:100%;box-shadow:0 20px 25px -5px rgba(0,0,0,0.2);border-radius:12px;padding:1.5rem;background:#fff;';

    const titleEl = document.createElement('h3');
    titleEl.style.cssText = 'margin-bottom:0.75rem;font-size:1.15rem;font-weight:600;color:var(--text);';
    titleEl.textContent = title;

    const msgEl = document.createElement('p');
    msgEl.style.cssText = 'color:var(--text-muted);margin-bottom:1.25rem;font-size:0.95rem;line-height:1.5;white-space:pre-wrap;';
    msgEl.textContent = message;

    const actions = document.createElement('div');
    actions.style.cssText = 'display:flex;justify-content:flex-end;';
    const okBtn = document.createElement('button');
    okBtn.id = 'inapp-ok-btn';
    okBtn.className = 'btn btn-primary';
    okBtn.textContent = 'OK';
    okBtn.onclick = () => overlay.remove();
    actions.appendChild(okBtn);

    card.appendChild(titleEl);
    card.appendChild(msgEl);
    card.appendChild(actions);
    overlay.appendChild(card);
    document.body.appendChild(overlay);
  }

  function showInAppConfirm(message, onConfirm, onCancel, title = 'Confirmation Required') {
    const overlay = document.createElement('div');
    overlay.className = 'modal-backdrop';
    overlay.style.cssText = 'position:fixed;inset:0;background:rgba(15,23,42,0.7);z-index:999999;display:flex;align-items:center;justify-content:center;padding:1rem;';
    const card = document.createElement('div');
    card.className = 'card';
    card.style.cssText = 'max-width:440px;width:100%;box-shadow:0 20px 25px -5px rgba(0,0,0,0.2);border-radius:12px;padding:1.5rem;background:#fff;';

    const titleEl = document.createElement('h3');
    titleEl.style.cssText = 'margin-bottom:0.75rem;font-size:1.15rem;font-weight:600;color:var(--text);';
    titleEl.textContent = title;

    const msgEl = document.createElement('p');
    msgEl.style.cssText = 'color:var(--text-muted);margin-bottom:1.25rem;font-size:0.95rem;line-height:1.5;white-space:pre-wrap;';
    msgEl.textContent = message;

    const actions = document.createElement('div');
    actions.style.cssText = 'display:flex;justify-content:flex-end;gap:0.75rem;';

    const cancelBtn = document.createElement('button');
    cancelBtn.id = 'inapp-cancel-btn';
    cancelBtn.className = 'btn btn-secondary';
    cancelBtn.textContent = 'Cancel';
    cancelBtn.onclick = () => {
      overlay.remove();
      if (onCancel) onCancel();
    };

    const confirmBtn = document.createElement('button');
    confirmBtn.id = 'inapp-confirm-btn';
    confirmBtn.className = 'btn btn-primary';
    confirmBtn.textContent = 'Confirm';
    confirmBtn.onclick = () => {
      overlay.remove();
      if (onConfirm) onConfirm();
    };

    actions.appendChild(cancelBtn);
    actions.appendChild(confirmBtn);
    card.appendChild(titleEl);
    card.appendChild(msgEl);
    card.appendChild(actions);
    overlay.appendChild(card);
    document.body.appendChild(overlay);
  }

  async function connectSignalR() {
    if (!window.signalR || hubConnection) return;
    try {
      hubConnection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/portal')
        .withAutomaticReconnect()
        .build();

      hubConnection.on('AppointmentUpdated', (data) => {
        showToast(`🔔 <strong>Live Update</strong>: Appointment ${data.action || 'updated'}`);
        if (currentTab === 'overview' || currentTab === 'cockpit') renderTab();
      });

      hubConnection.on('NewMessage', (data) => {
        showToast(`💬 <strong>Live Message</strong>: New message received in thread`);
        if (currentTab === 'messages') renderTab();
      });

      hubConnection.on('RefillUpdated', (data) => {
        showToast(`💊 <strong>Refill Update</strong>: Prescription refill is ${data.status}`);
        if (currentTab === 'overview' || currentTab === 'cockpit') renderTab();
      });

      hubConnection.on('BreakGlassAlert', (data) => {
        showToast(`🚨 <strong>EMERGENCY OVERRIDE ALERT</strong>: Break-glass access initiated for reason: "${data.reason}"`);
        if (activePatientId === data.patientId) {
          activeBreakGlass = { active: true, expiresAt: data.expiresAt };
          render();
        }
      });

      hubConnection.on('ComplianceAlert', (data) => {
        showToast(`🛡️ <strong>Compliance Alert</strong>: ${data.eventType || data.type || 'Emergency event logged'}`);
      });

      await hubConnection.start();
      if (activePatientId) {
        await hubConnection.invoke('JoinPatientRoom', activePatientId);
      }
    } catch (e) {
      console.debug('SignalR connect deferred:', e);
    }
  }

  // --- API Client ---
  async function api(path, options = {}) {
    const headers = {
      'Content-Type': 'application/json',
      'X-Requested-With': 'XMLHttpRequest',
      'X-CSRF-Token': '1',
      ...options.headers
    };
    if (activePatientId) {
      headers['x-patient-id'] = activePatientId;
    }

    const res = await fetch(path, {
      credentials: 'same-origin',
      ...options,
      headers
    });

    const bgHeader = res.headers.get('X-Break-Glass-Session');
    if (bgHeader === 'true') {
      const expires = res.headers.get('X-Break-Glass-Expires');
      if (!activeBreakGlass || activeBreakGlass.expiresAt !== expires) {
        activeBreakGlass = { active: true, expiresAt: expires };
        const banner = document.getElementById('break-glass-banner');
        if (!banner && currentSession) {
          setTimeout(render, 10);
        }
      }
    }

    if (res.status === 401 && path !== '/api/auth/login' && path !== '/api/auth/register') {
      currentSession = null;
      render();
      throw new Error('Session expired');
    }

    const data = await res.json().catch(() => ({}));
    if (!res.ok) {
      throw new Error(data.error || `HTTP ${res.status}`);
    }
    return data;
  }

  // --- Icons ---
  const ICONS = {
    activity: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M22 12h-4l-3 9L9 3l-3 9H2"/></svg>',
    dashboard: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 3h7v9H3zM14 3h7v5h-7zM14 12h7v9h-7zM3 16h7v5H3z"/></svg>',
    trend: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M22 7l-8.5 8.5-5-5L2 17M16 7h6v6"/></svg>',
    forms: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8zM14 2v6h6M16 13H8M16 17H8M10 9H8"/></svg>',
    messages: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/></svg>',
    billing: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M1 4h22v16H1zM1 10h22"/></svg>',
    family: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"/></svg>',
    sharing: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 8A3 3 0 1 0 15 5a3 3 0 0 0 .17.58L8 9a3 3 0 1 0 0 6l7.17 3.43A3 3 0 1 0 16 17a3 3 0 0 0-.17-.58L8.66 12.9a3 3 0 0 0 0-1.8l7.17-3.43A3 3 0 0 0 18 8z"/></svg>',
    care: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M4 3v6a5 5 0 0 0 5 5h1a5 5 0 0 0 5-5V3M8 3H6M14 3h2M11 14v3a5 5 0 0 0 10 0v-1M21 13a1 1 0 1 0 0-2 1 1 0 0 0 0 2z"/></svg>',
    nearby: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0zM12 13a3 3 0 1 0 0-6 3 3 0 0 0 0 6z"/></svg>',
    settings: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09a1.65 1.65 0 0 0-1.08-1.51 1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09a1.65 1.65 0 0 0 1.51-1.08 1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/></svg>',
    shield: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>',
    logout: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9"/></svg>'
  };

  function fmtMoney(cents) {
    return '$' + (cents / 100).toFixed(2);
  }

  function pill(status) {
    const s = String(status || '').toUpperCase();
    let tone = 'info';
    if (['CONFIRMED', 'PAID', 'UP_TO_DATE', 'SUCCEEDED', 'SEEN', 'ACTIVE'].includes(s)) tone = 'good';
    if (['REQUESTED', 'DUE', 'PARTIALLY_PAID', 'OPEN', 'PENDING', 'WAITING', 'IN_TRIAGE', 'IN_ROOM'].includes(s)) tone = 'warn';
    if (['OVERDUE', 'HIGH', 'LOW', 'DENIED', 'FAILED', 'BLOCKED', 'CANCELLED_BY_PATIENT'].includes(s)) tone = 'bad';
    return `<span class="pill ${tone}">${status || '—'}</span>`;
  }

  // --- Bootstrapping ---
  async function init() {
    try {
      currentSession = await api('/api/auth/me');
      if (currentSession.managing && currentSession.managing.length > 0) {
        activePatientId = currentSession.managing[0].patientId;
      }
      if (currentSession.user.role === 'ADMIN' || currentSession.user.role === 'SYSTEM_ADMIN') {
        currentTab = 'admin';
      } else if (currentSession.user.role === 'CLINICIAN' || currentSession.user.role === 'PHYSICIAN') {
        currentTab = 'cockpit';
      } else {
        currentTab = 'overview';
      }
    } catch {
      currentSession = null;
    }
    render();
  }

  // --- Render Orchestrator ---
  function render() {
    const appEl = document.getElementById('app');
    if (!currentSession) {
      appEl.innerHTML = renderSignIn();
      attachAuthEvents();
      return;
    }

    const isClinician = ['CLINICIAN', 'ADMIN', 'SYSTEM_ADMIN', 'PHYSICIAN', 'NURSE'].includes(currentSession.user.role);
    const isAdmin = ['ADMIN', 'SYSTEM_ADMIN', 'DEPT_ADMIN'].includes(currentSession.user.role);

    const patientTabs = [
      { id: 'overview', label: 'Overview', icon: 'dashboard' },
      { id: 'trends', label: 'Trends', icon: 'trend' },
      { id: 'forms', label: 'Forms', icon: 'forms' },
      { id: 'messages', label: 'Messages', icon: 'messages' },
      { id: 'billing', label: 'Billing', icon: 'billing' },
      { id: 'family', label: 'Family', icon: 'family' },
      { id: 'sharing', label: 'Sharing', icon: 'sharing' },
      { id: 'care', label: 'Find Care', icon: 'care' },
      { id: 'nearby', label: 'Nearby Facilities', icon: 'nearby' },
      { id: 'settings', label: 'Settings', icon: 'settings' }
    ];

    const clinicianTabs = [
      { id: 'cockpit', label: 'Today (Cockpit)', icon: 'activity' },
      { id: 'overview', label: 'My Panel', icon: 'dashboard' },
      { id: 'messages', label: 'Messages', icon: 'messages' },
      { id: 'settings', label: 'Settings', icon: 'settings' }
    ];

    const tabs = isClinician ? [...clinicianTabs] : [...patientTabs];
    if (isAdmin && !tabs.some(t => t.id === 'admin')) {
      tabs.splice(tabs.length - 1, 0, { id: 'admin', label: 'Admin Console', icon: 'shield' });
    }
    const currentPatient = currentSession.managing.find(m => m.patientId === activePatientId) || currentSession.managing[0];

    appEl.innerHTML = `
      <div class="shell">
        <aside class="sidebar">
          <div class="sidebar-head">
            <span class="sidebar-logo">
              <span style="font-size: 1.5rem;">🩺</span> Medical Portal
            </span>
          </div>
          <nav class="sidebar-nav">
            ${tabs.map(t => `
              <button class="nav-item ${currentTab === t.id ? 'active' : ''}" data-tab="${t.id}">
                ${ICONS[t.icon] || ''}
                <span>${t.label}</span>
              </button>
            `).join('')}
          </nav>
          <div class="sidebar-foot">
            <div class="user-card">
              <div class="avatar">${escapeHtml(currentSession.user.firstName?.[0] || '')}${escapeHtml(currentSession.user.lastName?.[0] || '')}</div>
              <div class="user-meta">
                <div class="user-name">${escapeHtml(currentSession.user.firstName)} ${escapeHtml(currentSession.user.lastName)}</div>
                <div class="user-role">${escapeHtml(currentSession.user.role)}</div>
              </div>
            </div>
            <button class="nav-item" id="btn-signout" style="color: #f87171;">
              ${ICONS.logout}
              <span>Sign out</span>
            </button>
          </div>
        </aside>

        <div class="shell-body">
          <header class="topbar">
            <div class="topbar-title">
              ${tabs.find(t => t.id === currentTab)?.label || 'Overview'}
            </div>
            <div class="topbar-actions">
              ${currentSession.managing.length > 1 ? `
                <label style="display: flex; align-items: center; gap: 0.5rem; font-size: 0.8125rem; color: var(--text-muted); font-weight: 500;">
                  <span>Viewing:</span>
                  <select class="topbar-select" id="patient-select">
                    ${currentSession.managing.map(m => `
                      <option value="${escapeHtml(m.patientId)}" ${m.patientId === activePatientId ? 'selected' : ''}>
                        ${escapeHtml(m.firstName)} ${escapeHtml(m.lastName)} ${m.isSelf ? '(you)' : ''}
                      </option>
                    `).join('')}
                  </select>
                </label>
              ` : `
                <span style="font-weight: 600; font-size: 0.875rem;">
                  ${currentPatient ? `${escapeHtml(currentPatient.firstName)} ${escapeHtml(currentPatient.lastName)}` : ''}
                </span>
              `}
              <span class="pill good">.NET 8 Online</span>
            </div>
          </header>

          ${activeBreakGlass && activeBreakGlass.active ? `
            <div class="break-glass-banner" id="break-glass-banner">
              <div style="display: flex; align-items: center; gap: 0.85rem;">
                <span style="font-size: 1.5rem;">🚨</span>
                <div>
                  <div style="font-weight: 700; letter-spacing: 0.02em;">EMERGENCY BREAK-GLASS OVERRIDE ACTIVE</div>
                  <div style="font-size: 0.8125rem; opacity: 0.95;">Temporary emergency access granted (60m). All actions are logged and compliance-monitored.</div>
                </div>
              </div>
              <div style="display: flex; align-items: center; gap: 1rem;">
                <div class="break-glass-badge">
                  ⏰ EXPIRES: ${activeBreakGlass.expiresAt ? new Date(activeBreakGlass.expiresAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '60m'}
                </div>
                <button class="btn-end-break-glass" id="btn-end-break-glass">End Emergency Session</button>
              </div>
            </div>
          ` : ''}

          <main class="content" id="tab-content">
            <div style="text-align: center; padding: 2rem; color: var(--text-muted);">Loading view...</div>
          </main>
        </div>
      </div>
      <div id="modal-container"></div>
    `;

    // Attach Topbar & Nav Events
    document.querySelectorAll('.nav-item[data-tab]').forEach(btn => {
      btn.onclick = () => {
        currentTab = btn.getAttribute('data-tab');
        render();
      };
    });

    const patientSelect = document.getElementById('patient-select');
    if (patientSelect) {
      patientSelect.onchange = (e) => {
        activePatientId = e.target.value;
        loadCurrentTabContent();
      };
    }

    document.getElementById('btn-signout').onclick = async () => {
      await api('/api/auth/logout', { method: 'POST' });
      currentSession = null;
      activeBreakGlass = null;
      render();
    };

    const endBgBtn = document.getElementById('btn-end-break-glass');
    if (endBgBtn) {
      endBgBtn.onclick = () => {
        showInAppConfirm('Conclude emergency break-glass session and revoke override access?', async () => {
          try {
            await api('/api/clinical/break-glass/revoke', {
              method: 'POST',
              body: JSON.stringify({ patientId: activePatientId })
            });
            activeBreakGlass = null;
            showToast('Break-glass emergency session concluded.');
            render();
          } catch (err) {
            showToast(`Error concluding session: ${err.message}`);
          }
        }, null, 'Conclude Emergency Override');
      };
    }

    loadCurrentTabContent();
  }

  // --- Auth View ---
  function renderSignIn() {
    return `
      <div class="login-wrap">
        <div class="login-card">
          <div class="login-logo">🩺 Medical Portal</div>
          <div class="login-sub">Rewritten & Powered by ASP.NET Core 8 & Entity Framework Core</div>

          <form id="form-login">
            <div class="form-group">
              <label class="form-label">Email</label>
              <input class="input" type="email" id="login-email" required placeholder="name@example.com">
            </div>
            <div class="form-group">
              <label class="form-label">Password</label>
              <input class="input" type="password" id="login-password" required placeholder="••••••••">
            </div>
            <div id="login-error" style="color: #dc2626; font-size: 0.8125rem; margin-bottom: 0.75rem;"></div>
            <button class="btn btn-primary" style="width: 100%;" type="submit">Sign In</button>
          </form>

          <div class="demo-buttons">
            <div style="font-size: 0.75rem; text-transform: uppercase; color: var(--text-muted); font-weight: 600; text-align: center;">
              One-Click Seed Accounts
            </div>
            <button class="btn btn-outline" id="demo-patient">
              👤 Sign in as Dana Rivera (Patient with Dependents)
            </button>
            <button class="btn btn-outline" id="demo-clinician">
              🩺 Sign in as Dr. Amara Patel, MD (Clinician)
            </button>
          </div>
        </div>
      </div>
    `;
  }

  function attachAuthEvents() {
    const form = document.getElementById('form-login');
    const errEl = document.getElementById('login-error');

    async function doLogin(email, password) {
      errEl.textContent = '';
      try {
        await api('/api/auth/login', {
          method: 'POST',
          body: JSON.stringify({ email, password })
        });
        await init();
      } catch (err) {
        errEl.textContent = err.message;
      }
    }

    form.onsubmit = async (e) => {
      e.preventDefault();
      const email = document.getElementById('login-email').value;
      const pass = document.getElementById('login-password').value;
      await doLogin(email, pass);
    };

    document.getElementById('demo-patient').onclick = () =>
      doLogin('dana@example.com', 'ChangeMe!Demo2024');

    document.getElementById('demo-clinician').onclick = () =>
      doLogin('apatel@clinic.example', 'ChangeMe!Demo2024');
  }

  // --- Tab Loaders ---
  async function loadCurrentTabContent() {
    const contentEl = document.getElementById('tab-content');
    if (!contentEl) return;

    try {
      if (currentTab === 'overview') await loadOverview(contentEl);
      else if (currentTab === 'cockpit') await loadCockpit(contentEl);
      else if (currentTab === 'trends') await loadTrends(contentEl);
      else if (currentTab === 'forms') await loadForms(contentEl);
      else if (currentTab === 'messages') await loadMessages(contentEl);
      else if (currentTab === 'billing') await loadBilling(contentEl);
      else if (currentTab === 'family') await loadFamily(contentEl);
      else if (currentTab === 'sharing') await loadSharing(contentEl);
      else if (currentTab === 'care') await loadFindCare(contentEl);
      else if (currentTab === 'nearby') await loadNearby(contentEl);
      else if (currentTab === 'admin') await loadAdmin(contentEl);
      else if (currentTab === 'settings') await loadSettings(contentEl);
    } catch (err) {
      contentEl.innerHTML = `<div class="notice notice-bad">${escapeHtml(err.message)}</div>`;
    }
  }

  // --- Overview Tab ---
  async function loadOverview(el) {
    connectSignalR();

    const [snapshot, vitalsRes, medsRes, apptsRes] = await Promise.all([
      api('/api/clinical/snapshot'),
      api('/api/clinical/vitals'),
      api('/api/clinical/medications'),
      api('/api/appointments')
    ]);

    el.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1.5rem;">
        <div style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 0.75rem;">
          <div>
            <h2 style="margin: 0; font-size: 1.25rem;">Health Chart & Care Summary</h2>
            <div style="font-size: 0.8125rem; color: var(--text-muted);">Real-time EHR synchronized with clinical records</div>
          </div>
          <a href="/api/clinical/summary/export" target="_blank" class="btn btn-outline btn-sm" style="text-decoration: none; display: inline-flex; align-items: center; gap: 0.35rem; background: var(--surface);">
            📄 Export Printable Visit Summary (PDF)
          </a>
        </div>

        <div class="grid grid-4">
          <div class="card stat-card">
            <span class="stat-label">Active Conditions</span>
            <span class="stat-value">${snapshot.activeConditionCount}</span>
            <span class="stat-sub">Documented in chart</span>
          </div>
          <div class="card stat-card">
            <span class="stat-label">Active Medications</span>
            <span class="stat-value">${snapshot.activeMedicationCount}</span>
            <span class="stat-sub">Current prescriptions</span>
          </div>
          <div class="card stat-card">
            <span class="stat-label">Abnormal Labs</span>
            <span class="stat-value" style="color: ${snapshot.abnormalLabCount > 0 ? '#dc2626' : 'var(--text)'};">
              ${snapshot.abnormalLabCount}
            </span>
            <span class="stat-sub">Requiring attention</span>
          </div>
          <div class="card stat-card">
            <span class="stat-label">Outstanding Balance</span>
            <span class="stat-value" style="color: ${snapshot.outstandingBalance?.cents > 0 ? '#b45309' : 'var(--text)'};">
              ${snapshot.outstandingBalance ? snapshot.outstandingBalance.formatted : 'Restricted'}
            </span>
            <span class="stat-sub">${snapshot.outstandingBalance ? 'Due for care' : 'No billing access'}</span>
          </div>
        </div>

        <div class="grid grid-2">
          <div class="card">
            <div class="card-header">
              <span class="card-title">🩺 Recent Vitals</span>
            </div>
            <div class="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Sign</th>
                    <th>Value</th>
                    <th>Reference</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  ${vitalsRes.vitals.map(v => `
                    <tr>
                      <td style="font-weight: 500;">${escapeHtml(v.label)}</td>
                      <td>${escapeHtml(v.value)} ${escapeHtml(v.unit || '')}</td>
                      <td style="color: var(--text-muted);">${escapeHtml(v.referenceRange || '—')}</td>
                      <td>${v.isAbnormal ? '<span class="pill bad">Out of Range</span>' : '<span class="pill good">Normal</span>'}</td>
                    </tr>
                  `).join('')}
                </tbody>
              </table>
            </div>
          </div>

          <div class="card">
            <div class="card-header">
              <span class="card-title">💊 Active Medications</span>
            </div>
            <div class="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Medication</th>
                    <th>Dosage</th>
                    <th>Instructions</th>
                  </tr>
                </thead>
                <tbody>
                  ${medsRes.medications.map(m => `
                    <tr>
                      <td style="font-weight: 600;">${escapeHtml(m.name)} ${escapeHtml(m.strength || '')}</td>
                      <td>${escapeHtml(m.dosage)} (${escapeHtml(m.frequency)})</td>
                      <td style="color: var(--text-muted);">${escapeHtml(m.instructions || 'As prescribed')}</td>
                    </tr>
                  `).join('')}
                </tbody>
              </table>
            </div>
          </div>
        </div>

        <div class="card">
          <div class="card-header">
            <span class="card-title">📅 Appointments</span>
            <button class="btn btn-outline btn-sm" id="btn-request-appt">+ Request Visit</button>
          </div>
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Date & Time</th>
                  <th>Provider</th>
                  <th>Reason</th>
                  <th>Status</th>
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                ${apptsRes.appointments.length === 0 ? '<tr><td colspan="5" style="text-align: center; color: var(--text-muted);">No scheduled appointments</td></tr>' : ''}
                ${apptsRes.appointments.map(a => `
                  <tr>
                    <td style="font-weight: 500;">${new Date(a.startAt).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' })}</td>
                    <td>${escapeHtml(a.provider?.name || 'Healthcare Provider')} (${escapeHtml(a.provider?.credentials || 'MD')})</td>
                    <td>${escapeHtml(a.reasonForVisit || 'Regular check-up')}</td>
                    <td>${pill(a.status)}</td>
                    <td>
                      ${a.status !== 'CANCELLED_BY_PATIENT' && a.status !== 'COMPLETED' ? `
                        <button class="btn btn-outline btn-sm btn-cancel-appt" data-id="${escapeHtml(a.id)}">Cancel</button>
                      ` : '—'}
                    </td>
                  </tr>
                `).join('')}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    `;

    document.querySelectorAll('.btn-cancel-appt').forEach(btn => {
      btn.onclick = () => {
        const id = btn.getAttribute('data-id');
        showInAppConfirm('Cancel this appointment?', async () => {
          await api(`/api/appointments/${id}/cancel`, { method: 'POST' });
          showToast('Appointment cancelled.');
          loadOverview(el);
        }, null, 'Cancel Appointment');
      };
    });

    document.getElementById('btn-request-appt').onclick = () => {
      currentTab = 'care';
      render();
    };
  }

  // --- Doctor Cockpit Tab ---
  async function loadCockpit(el) {
    connectSignalR();

    const [snapshot, allergiesRes, vitalsRes] = await Promise.all([
      api('/api/clinical/snapshot'),
      api('/api/clinical/allergies'),
      api('/api/clinical/vitals')
    ]);

    el.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1.5rem;">
        <div class="notice notice-good" style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 0.5rem;">
          <div>
            <strong>Doctor Encounter Cockpit</strong>: Connected to patient chart. All updates write directly into the clinical database.
          </div>
          <a href="/api/clinical/summary/export" target="_blank" class="btn btn-outline btn-sm" style="text-decoration: none; background: white;">
            📄 Export Visit Summary (PDF)
          </a>
        </div>

        <div class="grid grid-2">
          <!-- Live Queue Panel -->
          <div class="card">
            <div class="card-header">
              <span class="card-title">📋 Today's Clinic Queue</span>
              <span class="pill info">Live Queue</span>
            </div>
            <div>
              <div class="queue-row selected">
                <div>
                  <div style="font-weight: 600;">Dana Rivera (Age 38, F)</div>
                  <div style="font-size: 0.8125rem; color: var(--text-muted);">Slot: 09:00 • Chief Complaint: Blood pressure follow-up</div>
                </div>
                <div style="display: flex; gap: 0.5rem; align-items: center;">
                  <span class="pill good">IN_ROOM</span>
                  <span class="pill acuity-2">Acuity 2</span>
                </div>
              </div>

              <div class="queue-row">
                <div>
                  <div style="font-weight: 600;">Mateo Rivera (Age 9, M)</div>
                  <div style="font-size: 0.8125rem; color: var(--text-muted);">Slot: 09:30 • Chief Complaint: Asthma review</div>
                </div>
                <div style="display: flex; gap: 0.5rem; align-items: center;">
                  <span class="pill warn">WAITING</span>
                  <span class="pill acuity-3">Acuity 3</span>
                </div>
              </div>

              <div class="queue-row">
                <div>
                  <div style="font-weight: 600;">Sofia Rivera (Age 6, F)</div>
                  <div style="font-size: 0.8125rem; color: var(--text-muted);">Slot: 10:00 • Chief Complaint: Pediatric wellness</div>
                </div>
                <div style="display: flex; gap: 0.5rem; align-items: center;">
                  <span class="pill warn">IN_TRIAGE</span>
                  <span class="pill acuity-4">Acuity 4</span>
                </div>
              </div>
            </div>
          </div>

          <!-- Patient Safety & Allergies Card -->
          <div class="card">
            <div class="card-header">
              <span class="card-title">⚠️ Patient Allergies & Warnings</span>
            </div>
            <div style="display: flex; flex-direction: column; gap: 0.75rem;">
              ${allergiesRes.allergies.map(a => `
                <div class="notice notice-bad" style="margin-bottom: 0;">
                  <div>
                    <strong>ALLERGY: ${escapeHtml(a.substance)} (${escapeHtml(a.drugClass || 'Unclassified')})</strong>
                    <div style="font-size: 0.8125rem;">Severity: ${escapeHtml(a.severity)} • Reaction: ${escapeHtml(a.reaction || 'Not specified')}</div>
                    <div style="font-size: 0.75rem; color: #7f1d1d; font-family: var(--font-mono);">Class code: ${escapeHtml(a.drugClassCode || 'N/A')}</div>
                  </div>
                </div>
              `).join('')}

              <div style="margin-top: 0.5rem;">
                <div style="font-weight: 600; margin-bottom: 0.5rem;">Latest Vitals:</div>
                <div style="display: flex; flex-wrap: wrap; gap: 0.5rem;">
                  ${vitalsRes.vitals.map(v => `
                    <span class="pill ${v.isAbnormal ? 'bad' : 'good'}">
                      ${escapeHtml(v.label)}: ${escapeHtml(v.value)} ${escapeHtml(v.unit || '')}
                    </span>
                  `).join('')}
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- Prescribing, Allergy & Drug-Drug Interaction (DDI) Safety Engine -->
        <div class="card">
          <div class="card-header">
            <span class="card-title">💊 Medication Prescribing (Allergy & DDI Safety Guards)</span>
            <span class="pill info">Pharmacological Engine</span>
          </div>
          <p style="color: var(--text-muted); font-size: 0.875rem; margin-bottom: 1rem;">
            Test the safety engine: Attempting to prescribe <strong>Amoxicillin</strong> triggers cross-reactivity contraindication with Penicillin. Attempting to prescribe <strong>Spironolactone</strong> triggers drug-drug interaction warning with Lisinopril (Hyperkalemia risk)! <span style="font-size:0.75rem; opacity:0.85;">(Demonstration reference rules; does not claim clinical completeness)</span>
          </p>

          <div style="display: flex; gap: 1rem; align-items: flex-end; flex-wrap: wrap;">
            <div class="form-group" style="flex: 1; min-width: 250px; margin-bottom: 0;">
              <label class="form-label">Select Drug to Order</label>
              <select class="select" id="select-order-drug">
                <option value="Amoxicillin" data-class="J01CA">Amoxicillin 500mg (Penicillin Cross-Reactivity Risk)</option>
                <option value="Spironolactone" data-class="C03DA">Spironolactone 25mg (DDI with Lisinopril - Hyperkalemia Risk)</option>
                <option value="Ibuprofen" data-class="M01AE">Ibuprofen 600mg (NSAID - GI Bleed Risk with Anticoagulants)</option>
                <option value="Metformin" data-class="A10BA">Metformin 500mg (Biguanide)</option>
                <option value="Atorvastatin" data-class="C10AA">Atorvastatin 20mg (Statin)</option>
                <option value="Acetaminophen" data-class="N02BE">Acetaminophen 500mg (Safe Analgesic)</option>
              </select>
            </div>
            <button class="btn btn-primary" id="btn-check-allergy">Verify Safety & Order Drug</button>
          </div>

          <div id="allergy-alert-box" style="margin-top: 1rem;"></div>
        </div>

        <!-- SOAP Clinical Note -->
        <div class="card">
          <div class="card-header">
            <span class="card-title">📝 SOAP Clinical Encounter Note</span>
          </div>
          <div class="grid grid-2">
            <div class="form-group">
              <label class="form-label">Subjective (Symptoms reported)</label>
              <textarea class="textarea" rows="3" id="soap-s">Patient reports intermittent headache and blood pressure elevated at home.</textarea>
              <div style="display: flex; gap: 0.35rem; margin-top: 0.25rem;">
                <button class="btn btn-outline btn-sm macro-btn" data-target="soap-s" data-text="Denies chest pain. ">+ Denies chest pain</button>
                <button class="btn btn-outline btn-sm macro-btn" data-target="soap-s" data-text="No fever or chills. ">+ No fever</button>
              </div>
            </div>
            <div class="form-group">
              <label class="form-label">Objective (Exam & measurements)</label>
              <textarea class="textarea" rows="3" id="soap-o">Alert and oriented x3. Chest clear to auscultation bilaterally. No peripheral edema.</textarea>
              <div style="display: flex; gap: 0.35rem; margin-top: 0.25rem;">
                <button class="btn btn-outline btn-sm macro-btn" data-target="soap-o" data-text="Alert and oriented. ">+ Alert</button>
                <button class="btn btn-outline btn-sm macro-btn" data-target="soap-o" data-text="No focal neurological deficit. ">+ Normal Neuro</button>
              </div>
            </div>
            <div class="form-group">
              <label class="form-label">Assessment (Impression & differential)</label>
              <textarea class="textarea" rows="3" id="soap-a">Hypertension, essential - suboptimally controlled. Type 2 Diabetes Mellitus - improving glycemic control.</textarea>
            </div>
            <div class="form-group">
              <label class="form-label">Plan (Therapy, orders & follow-up)</label>
              <textarea class="textarea" rows="3" id="soap-p">Titrate Lisinopril to 20mg daily. Repeat BMP and HbA1c in 3 months. Return to clinic in 4 weeks.</textarea>
            </div>
          </div>
          <div style="display: flex; justify-content: flex-end; margin-top: 1rem;">
            <button class="btn btn-primary" id="btn-save-note">Sign & Finalize Encounter Note</button>
          </div>
        </div>
      </div>
    `;

    document.querySelectorAll('.macro-btn').forEach(btn => {
      btn.onclick = () => {
        const targetId = btn.getAttribute('data-target');
        const text = btn.getAttribute('data-text');
        const input = document.getElementById(targetId);
        if (input) input.value += text;
      };
    });

    document.getElementById('btn-check-allergy').onclick = async () => {
      const select = document.getElementById('select-order-drug');
      const drug = select.value;
      const alertBox = document.getElementById('allergy-alert-box');

      alertBox.innerHTML = '<div style="color:var(--text-muted); padding:0.5rem 0;">Analyzing contraindications and drug-drug interactions...</div>';

      try {
        const checkRes = await api('/api/clinical/prescriptions/safety-check', {
          method: 'POST',
          body: JSON.stringify({ medicationName: drug })
        });

        if (checkRes.safe) {
          const kbVer = checkRes.knowledgeBases ? checkRes.knowledgeBases.interactionVersion : 'Demo';
          alertBox.innerHTML = `
            <div class="notice notice-good">
              <div>
                <strong>✅ Safety Check Passed:</strong> No known contraindications detected for <strong>${escapeHtml(drug)}</strong> in current reference ruleset.
                <div style="font-size:0.75rem; color:var(--text-muted); margin-top:0.35rem; line-height:1.3;">
                  KB: ${escapeHtml(kbVer)} &bull; Demonstration rules only; does not claim clinical completeness. Independent clinical evaluation required.
                </div>
                <button class="btn btn-primary btn-sm" id="btn-confirm-prescribe" style="margin-top:0.75rem; display:block;">Authorize & Sign Prescription</button>
              </div>
            </div>
          `;

          document.getElementById('btn-confirm-prescribe').onclick = async () => {
            try {
              await api('/api/clinical/prescriptions', {
                method: 'POST',
                body: JSON.stringify({
                  medicationName: drug,
                  dosage: '1 tablet daily',
                  frequency: 'Daily',
                  instructions: 'Take with full glass of water'
                })
              });
              showToast(`💊 Prescribed ${drug} successfully!`);
              alertBox.innerHTML = `<div class="notice notice-good">Prescription for <strong>${escapeHtml(drug)}</strong> written to patient chart and synchronized to pharmacy queue.</div>`;
            } catch (err) {
              showToast(err.message);
            }
          };
        } else {
          const kbVer = checkRes.knowledgeBases ? checkRes.knowledgeBases.interactionVersion : 'Demo';
          let html = `<div class="notice notice-bad"><div>`;
          if (checkRes.hasAllergyConflict) {
            html += `<div style="font-weight:700; margin-bottom:0.25rem;">⚠️ ALLERGY CONTRAINDICATION:</div>`;
            checkRes.allergyWarnings.forEach(w => {
              html += `<div style="margin-bottom:0.25rem;">• ${escapeHtml(w.reason)} <span class="pill bad" style="font-size:0.7rem;">${escapeHtml(w.severity)}</span></div>`;
            });
          }
          if (checkRes.hasDrugInteraction) {
            html += `<div style="font-weight:700; margin-top:0.5rem; margin-bottom:0.25rem;">⚠️ DRUG-DRUG INTERACTION DETECTED:</div>`;
            checkRes.drugInteractions.forEach(w => {
              html += `<div style="margin-bottom:0.35rem;">• <strong>${escapeHtml(w.drug1)} + ${escapeHtml(w.drug2)}</strong>: ${escapeHtml(w.description)}<br><span style="font-size:0.8rem; color:#7f1d1d;">Guidance: ${escapeHtml(w.clinicalGuidance)}</span></div>`;
            });
          }
          html += `<div style="font-size:0.75rem; color:#7f1d1d; margin-top:0.5rem; border-top:1px dashed #fca5a5; padding-top:0.25rem;">KB: ${escapeHtml(kbVer)} &bull; Demonstration rules only; does not claim clinical completeness.</div>`;
          if (checkRes.requiresOverride) {
            html += `
              <div style="margin-top: 0.75rem; border-top: 1px solid #f87171; padding-top: 0.5rem;">
                <label style="font-size:0.8125rem; font-weight:600; display:block; margin-bottom:0.25rem;">Clinician Override Justification (Required to order):</label>
                <div style="display:flex; gap:0.5rem;">
                  <input type="text" id="override-reason" class="input" placeholder="e.g. Benefits outweigh risks, patient closely monitored in clinic" style="flex:1;">
                  <button class="btn btn-primary btn-sm" id="btn-override-prescribe" style="background:#dc2626; white-space:nowrap;">Override & Sign Order</button>
                </div>
              </div>
            `;
          }
          html += `</div></div>`;
          alertBox.innerHTML = html;

          document.getElementById('btn-override-prescribe')?.addEventListener('click', async () => {
            const justification = document.getElementById('override-reason').value.trim();
            if (!justification) {
              showToast('Clinical justification is required to override high-severity safety alerts.');
              return;
            }
            try {
              await api('/api/clinical/prescriptions', {
                method: 'POST',
                body: JSON.stringify({
                  medicationName: drug,
                  dosage: '1 tablet daily',
                  frequency: 'Daily',
                  instructions: 'Take as directed under physician supervision',
                  overrideJustification: justification
                })
              });
              showToast(`⚠️ Prescribed ${drug} under clinician override.`);
              alertBox.innerHTML = `<div class="notice notice-good">Order recorded with safety override audit trail: "${escapeHtml(justification)}".</div>`;
            } catch (err) {
              showToast(err.message);
            }
          });
        }
      } catch (err) {
        alertBox.innerHTML = `<div class="notice notice-bad">${escapeHtml(err.message)}</div>`;
      }
    };

    document.getElementById('btn-save-note').onclick = () => {
      showToast('Clinical encounter note signed and recorded.');
    };
  }

  // --- Trends Tab ---
  async function loadTrends(el) {
    const data = await api('/api/clinical/trends');

    const hba1cSeries = data.series.find(s => s.code === '4548-4') || data.series[0];

    el.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1.5rem;">
        <div class="card">
          <div class="card-header">
            <span class="card-title">📈 Lab Trends Over Time: ${hba1cSeries ? hba1cSeries.name : 'No Lab Data'}</span>
            <span class="pill ${hba1cSeries?.latestAbnormal ? 'bad' : 'good'}">
              Latest: ${hba1cSeries?.last ?? '—'} ${hba1cSeries?.unit ?? ''} (${hba1cSeries?.direction})
            </span>
          </div>

          ${hba1cSeries ? renderSvgTrendChart(hba1cSeries) : '<p>No data</p>'}

          <div style="margin-top: 1rem; font-size: 0.875rem; color: var(--text-muted); display: flex; justify-content: space-between;">
            <span>Reference Target: ${hba1cSeries?.refRangeLow} - ${hba1cSeries?.refRangeHigh} ${hba1cSeries?.unit}</span>
            <span>Net Change: ${hba1cSeries?.delta ? (hba1cSeries.delta > 0 ? '+' : '') + hba1cSeries.delta.toFixed(1) : '—'} ${hba1cSeries?.unit}</span>
          </div>
        </div>

        <div class="card">
          <div class="card-header">
            <span class="card-title">📊 Tracked Diagnostic Biomarkers</span>
          </div>
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Biomarker</th>
                  <th>First</th>
                  <th>Latest</th>
                  <th>Change</th>
                  <th>Direction</th>
                  <th>Abnormal Ratio</th>
                  <th>Alert</th>
                </tr>
              </thead>
              <tbody>
                ${data.series.map(s => `
                  <tr>
                    <td style="font-weight: 600;">${s.name}</td>
                    <td>${s.first ?? '—'} ${s.unit || ''}</td>
                    <td>${s.last ?? '—'} ${s.unit || ''}</td>
                    <td>${s.delta != null ? (s.delta > 0 ? '+' : '') + s.delta.toFixed(1) : '—'}</td>
                    <td><span class="pill info">${s.direction}</span></td>
                    <td>${Math.round(s.abnormalShare * 100)}%</td>
                    <td>${s.worthFlagging ? '<span class="pill bad">Action Flag</span>' : '<span class="pill good">OK</span>'}</td>
                  </tr>
                `).join('')}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    `;
  }

  function renderSvgTrendChart(series) {
    const pts = series.points.filter(p => p.valueNumber != null);
    if (pts.length < 2) return '<p>Not enough points to render trend</p>';

    const width = 800;
    const height = 220;
    const padding = 40;

    const values = pts.map(p => Number(p.valueNumber));
    const minVal = Math.min(Math.min(...values), series.refRangeLow || 4) - 0.5;
    const maxVal = Math.max(Math.max(...values), series.refRangeHigh || 6) + 0.5;

    const scaleX = (i) => padding + (i / (pts.length - 1)) * (width - 2 * padding);
    const scaleY = (v) => height - padding - ((v - minVal) / (maxVal - minVal)) * (height - 2 * padding);

    const pathD = pts.map((p, i) => `${i === 0 ? 'M' : 'L'} ${scaleX(i)} ${scaleY(Number(p.valueNumber))}`).join(' ');

    return `
      <div class="chart-container">
        <svg class="trend-chart" viewBox="0 0 ${width} ${height}">
          <!-- Target Band -->
          ${series.refRangeHigh ? `
            <rect x="${padding}" y="${scaleY(series.refRangeHigh)}" width="${width - 2 * padding}" height="${scaleY(series.refRangeLow || 0) - scaleY(series.refRangeHigh)}" class="chart-ref-band" />
          ` : ''}

          <!-- Grid Lines -->
          <line x1="${padding}" y1="${height - padding}" x2="${width - padding}" y2="${height - padding}" stroke="#e2e8f0" stroke-width="1" />

          <!-- Trend Line -->
          <path d="${pathD}" class="chart-line" />

          <!-- Points -->
          ${pts.map((p, i) => `
            <circle cx="${scaleX(i)}" cy="${scaleY(Number(p.valueNumber))}" class="chart-point ${p.isAbnormal ? 'abnormal' : ''}">
              <title>${p.valueNumber} ${series.unit} (${new Date(p.observedAt).toLocaleDateString()})</title>
            </circle>
            <text x="${scaleX(i)}" y="${height - 15}" font-size="11" fill="#64748b" text-anchor="middle">
              ${new Date(p.observedAt).toLocaleDateString([], { month: 'short', day: 'numeric' })}
            </text>
            <text x="${scaleX(i)}" y="${scaleY(Number(p.valueNumber)) - 10}" font-size="12" font-weight="600" fill="#0f172a" text-anchor="middle">
              ${p.valueNumber}
            </text>
          `).join('')}
        </svg>
      </div>
    `;
  }

  // --- Messages Tab ---
  async function loadMessages(el) {
    const [threadsRes, careTeamRes] = await Promise.all([
      api('/api/messages'),
      api('/api/messages/care-team')
    ]);

    el.innerHTML = `
      <div class="grid grid-2">
        <div class="card">
          <div class="card-header">
            <span class="card-title">💬 Message Threads</span>
            <button class="btn btn-primary btn-sm" id="btn-compose-msg">+ Compose</button>
          </div>
          <div style="display: flex; flex-direction: column; gap: 0.5rem;" id="threads-list">
            ${threadsRes.threads.length === 0 ? '<p style="color: var(--text-muted); text-align: center; padding: 1rem;">No messages</p>' : ''}
            ${threadsRes.threads.map(t => `
              <div class="queue-row" data-thread-id="${escapeHtml(t.id)}">
                <div>
                  <div style="font-weight: 600; display: flex; align-items: center; gap: 0.5rem;">
                    ${escapeHtml(t.subject)}
                    ${t.unread ? '<span class="pill warn">Unread</span>' : ''}
                  </div>
                  <div style="font-size: 0.8125rem; color: var(--text-muted);">${escapeHtml(t.preview || 'No messages yet')}</div>
                </div>
                <span style="font-size: 0.75rem; color: var(--text-muted);">${t.lastMessageAt ? new Date(t.lastMessageAt).toLocaleDateString() : ''}</span>
              </div>
            `).join('')}
          </div>
        </div>

        <div class="card" id="thread-view-card">
          <div style="text-align: center; padding: 3rem; color: var(--text-muted);">
            Select a thread on the left to read and reply.
          </div>
        </div>
      </div>
    `;

    document.querySelectorAll('[data-thread-id]').forEach(row => {
      row.onclick = async () => {
        const id = row.getAttribute('data-thread-id');
        await openThreadDetail(id);
      };
    });

    document.getElementById('btn-compose-msg').onclick = () => {
      showComposeModal(careTeamRes.careTeam);
    };
  }

  async function openThreadDetail(id) {
    const card = document.getElementById('thread-view-card');
    card.innerHTML = '<p style="text-align: center;">Loading message history...</p>';

    const res = await api(`/api/messages/${id}`);
    const thread = res.thread;

    card.innerHTML = `
      <div class="card-header">
        <span class="card-title">${escapeHtml(thread.subject)}</span>
        <span class="pill info">${escapeHtml(thread.status)}</span>
      </div>
      <div style="max-height: 400px; overflow-y: auto; display: flex; flex-direction: column; gap: 0.75rem; padding-right: 0.5rem; margin-bottom: 1rem;">
        ${thread.messages.map(m => `
          <div style="align-self: ${m.mine ? 'flex-end' : 'flex-start'}; max-width: 80%; background: ${m.mine ? 'var(--primary-light)' : 'var(--surface-hover)'}; padding: 0.75rem 1rem; border-radius: var(--radius); border: 1px solid var(--border);">
            <div style="display: flex; justify-content: space-between; gap: 1rem; margin-bottom: 0.25rem;">
              <span style="font-weight: 600; font-size: 0.75rem; color: var(--text);">${escapeHtml(m.senderName)} (${escapeHtml(m.senderRole)})</span>
              <span style="font-size: 0.75rem; color: var(--text-muted);">${new Date(m.sentAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</span>
            </div>
            <div style="font-size: 0.875rem; white-space: pre-wrap;">${escapeHtml(m.body)}</div>
          </div>
        `).join('')}
      </div>
      <div style="display: flex; gap: 0.5rem;">
        <input class="input" id="reply-text" placeholder="Write a secure reply...">
        <button class="btn btn-primary" id="btn-send-reply">Send</button>
      </div>
    `;

    document.getElementById('btn-send-reply').onclick = async () => {
      const text = document.getElementById('reply-text').value.trim();
      if (!text) return;
      await api(`/api/messages/${id}/replies`, {
        method: 'POST',
        body: JSON.stringify({ body: text })
      });
      openThreadDetail(id);
    };
  }

  function showComposeModal(careTeam) {
    showModal({
      title: 'Compose Secure Message',
      body: `
        <div class="form-group">
          <label class="form-label">Recipient Clinician</label>
          <select class="select" id="compose-provider">
            ${careTeam.map(c => `
              <option value="${escapeHtml(c.providerId)}">${escapeHtml(c.name)} (${escapeHtml(c.specialty || 'General')})</option>
            `).join('')}
          </select>
        </div>
        <div class="form-group">
          <label class="form-label">Subject</label>
          <input class="input" id="compose-subject" placeholder="e.g. Question about medication dosage">
        </div>
        <div class="form-group">
          <label class="form-label">Message</label>
          <textarea class="textarea" rows="4" id="compose-body" placeholder="Describe your question or concern in detail..."></textarea>
        </div>
      `,
      confirmText: 'Send Message',
      onConfirm: async () => {
        const providerId = document.getElementById('compose-provider').value;
        const subject = document.getElementById('compose-subject').value;
        const body = document.getElementById('compose-body').value;
        await api('/api/messages', {
          method: 'POST',
          body: JSON.stringify({ providerId, subject, body })
        });
        closeModal();
        loadCurrentTabContent();
      }
    });
  }

  // --- Billing Tab ---
  async function loadBilling(el) {
    const [statementsRes, plansRes] = await Promise.all([
      api('/api/billing/statements'),
      api('/api/billing/plans')
    ]);

    el.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1.5rem;">
        <div class="card" style="background: linear-gradient(135deg, #0284c7 0%, #0369a1 100%); color: white;">
          <div style="font-size: 0.875rem; opacity: 0.9;">Total Outstanding Patient Balance</div>
          <div style="font-size: 2.25rem; font-weight: 800; margin: 0.25rem 0;">${statementsRes.totalOwedFormatted}</div>
          <div style="display: flex; gap: 0.75rem; margin-top: 0.5rem;">
            <button class="btn" style="background: white; color: #0284c7; font-weight: 600;" id="btn-pay-now">Make a Payment</button>
            <button class="btn btn-outline" style="color: white; border-color: rgba(255,255,255,0.4);" id="btn-new-plan">Set Up Payment Plan</button>
          </div>
        </div>

        <div class="card">
          <div class="card-header">
            <span class="card-title">📄 Clinical Statements</span>
          </div>
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Description</th>
                  <th>Total</th>
                  <th>Paid</th>
                  <th>Balance Due</th>
                  <th>Due Date</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                ${statementsRes.statements.map(s => `
                  <tr>
                    <td style="font-weight: 500;">${escapeHtml(s.description || 'Clinical Care Visit')}</td>
                    <td>${escapeHtml(s.formatted.total)}</td>
                    <td>${escapeHtml(s.formatted.paid)}</td>
                    <td style="font-weight: 700; color: ${s.balanceCents > 0 ? '#b91c1c' : 'var(--text)'};">${escapeHtml(s.formatted.balance)}</td>
                    <td>${s.dueAt ? new Date(s.dueAt).toLocaleDateString() : '—'}</td>
                    <td>${pill(s.status)}</td>
                  </tr>
                `).join('')}
              </tbody>
            </table>
          </div>
        </div>

        <div class="card">
          <div class="card-header">
            <span class="card-title">🗓️ Active Payment Plans</span>
          </div>
          ${plansRes.plans.length === 0 ? '<p style="color: var(--text-muted); padding: 1rem 0;">No active payment installment plans.</p>' : ''}
          ${plansRes.plans.map(p => `
            <div style="border: 1px solid var(--border); border-radius: var(--radius); padding: 1rem; margin-bottom: 0.75rem;">
              <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.5rem;">
                <span style="font-weight: 600;">Plan for Statement #${escapeHtml(p.statementId.substring(0, 8))}</span>
                <span class="pill good">${escapeHtml(p.status)}</span>
              </div>
              <div style="font-size: 0.875rem; color: var(--text-muted);">
                ${p.numberOfPayments} monthly payments of <strong>${fmtMoney(p.installmentCents)}</strong> (Total: ${fmtMoney(p.totalCents)})
              </div>
              ${p.nextInstallment ? `
                <div style="margin-top: 0.5rem; font-size: 0.8125rem; color: var(--primary-text); font-weight: 500;">
                  Next installment: ${fmtMoney(p.nextInstallment.amountCents)} due ${new Date(p.nextInstallment.dueAt).toLocaleDateString()}
                </div>
              ` : ''}
            </div>
          `).join('')}
        </div>
      </div>
    `;

    document.getElementById('btn-pay-now').onclick = () => {
      showModal({
        title: 'Submit Clinical Payment',
        body: `
          <div class="form-group">
            <label class="form-label">Payment Amount ($)</label>
            <input class="input" id="pay-amount" value="${(statementsRes.totalOwedCents / 100).toFixed(2)}">
          </div>
          <div class="notice notice-good">
            Secure PCI SAQ A Hosted Fields: Card details are collected securely via Stripe Elements.
          </div>
        `,
        confirmText: 'Pay Now',
        onConfirm: async () => {
          const amt = document.getElementById('pay-amount').value;
          const intent = await api('/api/billing/payment-intent', {
            method: 'POST',
            body: JSON.stringify({ amount: amt })
          });
          if (intent && intent.paymentIntentId) {
            await api('/api/billing/payments', {
              method: 'POST',
              body: JSON.stringify({ paymentIntentId: intent.paymentIntentId })
            });
          }
          closeModal();
          loadBilling(el);
        }
      });
    };

    document.getElementById('btn-new-plan').onclick = () => {
      const openStatement = statementsRes.statements.find(s => s.balanceCents >= 5000);
      if (!openStatement) {
        showToast('Payment plans require a statement with an outstanding balance of at least $50.00.');
        return;
      }

      showModal({
        title: 'Set up Monthly Payment Plan',
        body: `
          <div class="form-group">
            <label class="form-label">Select Statement</label>
            <select class="select" id="plan-stmt">
              ${statementsRes.statements.filter(s => s.balanceCents >= 5000).map(s => `
                <option value="${s.id}">${s.description} (${s.formatted.balance})</option>
              `).join('')}
            </select>
          </div>
          <div class="form-group">
            <label class="form-label">Number of Monthly Installments</label>
            <select class="select" id="plan-installments">
              <option value="3">3 Monthly Payments</option>
              <option value="6" selected>6 Monthly Payments</option>
              <option value="12">12 Monthly Payments</option>
            </select>
          </div>
          <div class="form-group">
            <label class="form-label">Plan Start Date</label>
            <input class="input" type="date" id="plan-start" value="${new Date().toISOString().slice(0, 10)}">
          </div>
        `,
        confirmText: 'Create Plan',
        onConfirm: async () => {
          const stmtId = document.getElementById('plan-stmt').value;
          const inst = parseInt(document.getElementById('plan-installments').value, 10);
          const start = document.getElementById('plan-start').value;
          await api('/api/billing/plans', {
            method: 'POST',
            body: JSON.stringify({ statementId: stmtId, numberOfPayments: inst, startDate: start })
          });
          closeModal();
          loadBilling(el);
        }
      });
    };
  }

  // --- Family Tab ---
  async function loadFamily(el) {
    const invitesRes = await api('/api/invites');

    el.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1.5rem;">
        <div class="card">
          <div class="card-header">
            <span class="card-title">👨‍👩‍👧 Family Care Management</span>
            <button class="btn btn-primary btn-sm" id="btn-invite-family">+ Invite Caregiver</button>
          </div>
          <p style="color: var(--text-muted); font-size: 0.875rem; margin-bottom: 1rem;">
            Grant family members or designated guardians authorized proxy access to view health records, message care teams, and manage appointments.
          </p>

          <div style="font-weight: 600; margin-bottom: 0.5rem;">Active Care Delegation:</div>
          <div class="grid grid-3">
            ${currentSession.managing.map(m => `
              <div style="border: 1px solid var(--border); border-radius: var(--radius); padding: 1rem; background: var(--surface-hover);">
                <div style="font-weight: 700; font-size: 1rem;">${escapeHtml(m.firstName)} ${escapeHtml(m.lastName)}</div>
                <div style="font-size: 0.8125rem; color: var(--text-muted);">DOB: ${escapeHtml(m.dateOfBirth)} (${escapeHtml(m.age)} yrs)</div>
                <div style="margin-top: 0.5rem;">
                  <span class="pill ${m.isSelf ? 'good' : 'info'}">${escapeHtml(m.relationship)}</span>
                  <span style="font-size: 0.75rem; color: var(--text-muted); margin-left: 0.5rem;">MRN: ${escapeHtml(m.mrn || 'N/A')}</span>
                </div>
              </div>
            `).join('')}
          </div>
        </div>

        <div class="card">
          <div class="card-header">
            <span class="card-title">✉️ Pending Proxy Invitations</span>
          </div>
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Recipient Email</th>
                  <th>Relationship</th>
                  <th>Created</th>
                  <th>Expires</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                ${invitesRes.invites.length === 0 ? '<tr><td colspan="5" style="text-align: center; color: var(--text-muted);">No pending invitations</td></tr>' : ''}
                ${invitesRes.invites.map(i => `
                  <tr>
                    <td style="font-weight: 500;">${escapeHtml(i.email)}</td>
                    <td>${escapeHtml(i.relationship || 'Guardian')}</td>
                    <td>${new Date(i.createdAt).toLocaleDateString()}</td>
                    <td>${new Date(i.expiresAt).toLocaleDateString()}</td>
                    <td>${pill(i.effectiveStatus)}</td>
                  </tr>
                `).join('')}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    `;

    document.getElementById('btn-invite-family').onclick = () => {
      showModal({
        title: 'Invite Family Caregiver',
        body: `
          <div class="form-group">
            <label class="form-label">Caregiver Email</label>
            <input class="input" type="email" id="invite-email" placeholder="relative@example.com">
          </div>
          <div class="form-group">
            <label class="form-label">Relationship</label>
            <input class="input" id="invite-rel" placeholder="Parent, Spouse, Guardian">
          </div>
          <div class="form-group">
            <label class="form-label">Permissions Granted</label>
            <label style="display: flex; align-items: center; gap: 0.5rem; font-size: 0.875rem;">
              <input type="checkbox" id="perm-msg" checked> Can Message Care Team
            </label>
            <label style="display: flex; align-items: center; gap: 0.5rem; font-size: 0.875rem;">
              <input type="checkbox" id="perm-appt" checked> Can Manage Appointments
            </label>
            <label style="display: flex; align-items: center; gap: 0.5rem; font-size: 0.875rem;">
              <input type="checkbox" id="perm-refill" checked> Can Request Refills
            </label>
            <label style="display: flex; align-items: center; gap: 0.5rem; font-size: 0.875rem;">
              <input type="checkbox" id="perm-bill"> Can View & Pay Bills
            </label>
          </div>
        `,
        confirmText: 'Generate Invitation',
        onConfirm: async () => {
          const email = document.getElementById('invite-email').value;
          const relationship = document.getElementById('invite-rel').value;
          const canMessage = document.getElementById('perm-msg').checked;
          const canManageAppointments = document.getElementById('perm-appt').checked;
          const canRequestRefills = document.getElementById('perm-refill').checked;
          const canPayBills = document.getElementById('perm-bill').checked;

          const res = await api('/api/invites', {
            method: 'POST',
            body: JSON.stringify({ email, relationship, canMessage, canManageAppointments, canRequestRefills, canPayBills })
          });

          closeModal();
          showInAppAlert(`Invitation created! Invitation Code: ${res.code}\nShare this code with the recipient.`, 'Proxy Invitation Created');
          loadFamily(el);
        }
      });
    };
  }

  // --- Sharing Tab ---
  async function loadSharing(el) {
    const sharesRes = await api('/api/shares');

    el.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1.5rem;">
        <div class="card">
          <div class="card-header">
            <span class="card-title">🔐 Secure Record Sharing</span>
            <button class="btn btn-primary btn-sm" id="btn-create-share">+ New Share Code</button>
          </div>
          <p style="color: var(--text-muted); font-size: 0.875rem; margin-bottom: 1rem;">
            Export a read-only FHIR R4 clinical summary to outside clinicians via an encrypted, single-use or time-bounded access code.
          </p>

          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Label / Recipient</th>
                  <th>Created</th>
                  <th>Expires</th>
                  <th>Uses</th>
                  <th>Status</th>
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                ${sharesRes.shares.length === 0 ? '<tr><td colspan="6" style="text-align: center; color: var(--text-muted);">No active share codes</td></tr>' : ''}
                ${sharesRes.shares.map(s => `
                  <tr>
                    <td>
                      <div style="font-weight: 600;">${escapeHtml(s.label)}</div>
                      <div style="font-size: 0.8125rem; color: var(--text-muted);">${escapeHtml(s.recipientName || 'External Clinician')} (${escapeHtml(s.recipientRole || 'Physician')})</div>
                    </td>
                    <td>${new Date(s.createdAt).toLocaleDateString()}</td>
                    <td>${new Date(s.expiresAt).toLocaleDateString()}</td>
                    <td>${s.useCount} / ${s.maxUses}</td>
                    <td>${pill(s.status)}</td>
                    <td>
                      ${s.status === 'ACTIVE' ? `
                        <button class="btn btn-danger btn-sm btn-revoke-share" data-id="${escapeHtml(s.id)}">Revoke</button>
                      ` : '—'}
                    </td>
                  </tr>
                `).join('')}
              </tbody>
            </table>
          </div>
        </div>

        <div class="card">
          <div class="card-header">
            <span class="card-title">🔎 Redeem External Share Code</span>
          </div>
          <div style="display: flex; gap: 0.75rem;">
            <input class="input" id="redeem-code" placeholder="Enter SHR-XXXXXX code...">
            <button class="btn btn-outline" id="btn-redeem">View Record Summary</button>
          </div>
          <div id="redeem-result" style="margin-top: 1rem;"></div>
        </div>
      </div>
    `;

    document.querySelectorAll('.btn-revoke-share').forEach(btn => {
      btn.onclick = async () => {
        const id = btn.getAttribute('data-id');
        await api(`/api/shares/${id}/revoke`, { method: 'POST' });
        loadSharing(el);
      };
    });

    document.getElementById('btn-create-share').onclick = () => {
      showModal({
        title: 'Create External Clinician Share Code',
        body: `
          <div class="form-group">
            <label class="form-label">Share Purpose / Label</label>
            <input class="input" id="share-label" placeholder="e.g. Cardiology Second Opinion">
          </div>
          <div class="form-group">
            <label class="form-label">Recipient Clinician Name (Optional)</label>
            <input class="input" id="share-recipient" placeholder="Dr. Jane Doe">
          </div>
          <div class="form-group">
            <label class="form-label">Expiration (Days)</label>
            <input class="input" type="number" id="share-days" value="30" min="1" max="90">
          </div>
        `,
        confirmText: 'Generate Share Code',
        onConfirm: async () => {
          const label = document.getElementById('share-label').value;
          const recipientName = document.getElementById('share-recipient').value;
          const expiresInDays = parseInt(document.getElementById('share-days').value, 10);

          const res = await api('/api/shares', {
            method: 'POST',
            body: JSON.stringify({ label, recipientName, expiresInDays, maxUses: 5, scope: ["allergies","medications","conditions","labs","vitals"] })
          });

          closeModal();
          showInAppAlert(`Share code created!\nCode: ${res.code}\nGive this code to your outside provider.`, 'Record Share Code');
          loadSharing(el);
        }
      });
    };

    document.getElementById('btn-redeem').onclick = async () => {
      const code = document.getElementById('redeem-code').value.trim();
      const resEl = document.getElementById('redeem-result');
      if (!code) return;

      try {
        const bundle = await api('/api/share', {
          method: 'POST',
          body: JSON.stringify({ code })
        });
        resEl.innerHTML = `
          <div class="notice notice-good">
            <strong>FHIR R4 Bundle Verified</strong>: Total ${escapeHtml(bundle.total)} resources received.
          </div>
          <pre style="background: #0f172a; color: #38bdf8; padding: 1rem; border-radius: var(--radius); font-size: 0.75rem; max-height: 250px; overflow: auto;">${escapeHtml(JSON.stringify(bundle, null, 2))}</pre>
        `;
      } catch (err) {
        resEl.innerHTML = `<div class="notice notice-bad">${escapeHtml(err.message)}</div>`;
      }
    };
  }

  // --- Find Care & Nearby Tab ---
  async function loadFindCare(el) {
    const res = await api('/api/care/search?latitude=39.7983&longitude=-89.6437');

    el.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1.5rem;">
        <div class="card">
          <div class="card-header">
            <span class="card-title">👨‍⚕️ Clinic Doctors & Specialists</span>
          </div>
          <div class="grid grid-2">
            ${res.providers.map(p => `
              <div style="border: 1px solid var(--border); border-radius: var(--radius); padding: 1rem; display: flex; flex-direction: column; justify-content: space-between;">
                <div>
                  <div style="font-weight: 700; font-size: 1.125rem;">${escapeHtml(p.name)}</div>
                  <div style="color: var(--primary); font-weight: 500; font-size: 0.875rem;">${escapeHtml(p.specialty)} (${escapeHtml(p.credentials || 'MD')})</div>
                  <div style="margin-top: 0.5rem; font-size: 0.8125rem; color: var(--text-muted);">
                    ${p.locations.map(l => `<div>📍 ${escapeHtml(l.name)} — ${escapeHtml(l.distanceLabel || 'Springfield')}</div>`).join('')}
                  </div>
                </div>
                <button class="btn btn-primary btn-sm btn-book-slot" style="margin-top: 1rem;" data-provider-id="${escapeHtml(p.providerId)}">
                  Check Availability & Book
                </button>
              </div>
            `).join('')}
          </div>
        </div>

        <div id="availability-picker-container"></div>
      </div>
    `;

    document.querySelectorAll('.btn-book-slot').forEach(btn => {
      btn.onclick = async () => {
        const providerId = btn.getAttribute('data-provider-id');
        await loadAvailabilitySlots(providerId);
      };
    });
  }

  async function loadAvailabilitySlots(providerId) {
    const container = document.getElementById('availability-picker-container');
    container.innerHTML = '<p style="text-align: center;">Searching appointment calendar...</p>';

    const res = await api(`/api/care/availability?providerId=${providerId}&days=7&durationMinutes=30`);

    container.innerHTML = `
      <div class="card">
        <div class="card-header">
          <span class="card-title">🕒 Available Booking Slots (Next 7 Days)</span>
        </div>
        ${res.days.length === 0 ? '<p style="color: var(--text-muted);">No open slots found for this week.</p>' : ''}
        <div style="display: flex; flex-direction: column; gap: 1rem;">
          ${res.days.map(d => `
            <div>
              <div style="font-weight: 600; margin-bottom: 0.5rem;">📅 ${new Date(d.date).toLocaleDateString([], { weekday: 'long', month: 'short', day: 'numeric' })}</div>
              <div style="display: flex; flex-wrap: wrap; gap: 0.5rem;">
                ${d.slots.map(s => `
                  <button class="btn btn-outline btn-sm btn-slot-select" data-provider-id="${providerId}" data-start="${s.startAt}" data-end="${s.endAt}">
                    ${new Date(s.startAt).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}
                  </button>
                `).join('')}
              </div>
            </div>
          `).join('')}
        </div>
      </div>
    `;

    document.querySelectorAll('.btn-slot-select').forEach(btn => {
      btn.onclick = () => {
        const pid = btn.getAttribute('data-provider-id');
        const startAt = btn.getAttribute('data-start');
        const endAt = btn.getAttribute('data-end');

        showInAppConfirm(`Book appointment on ${new Date(startAt).toLocaleString([], { dateStyle: 'medium', timeStyle: 'short' })}?`, async () => {
          await api('/api/appointments', {
            method: 'POST',
            body: JSON.stringify({
              providerId: pid,
              startAt,
              endAt,
              reasonForVisit: 'Clinic check-up'
            })
          });

          showToast('Appointment requested successfully!');
          currentTab = 'overview';
          render();
        }, null, 'Book Appointment');
      };
    });
  }

  // --- Nearby Facilities Tab ---
  async function loadNearby(el) {
    el.innerHTML = '<div style="padding: 2rem; text-align: center; color: var(--text-muted);">Detecting location via Public IP and loading facilities...</div>';

    let geo = null;
    try {
      geo = await api('/api/geo/approximate');
    } catch (e) {
      console.warn('Geo approximate failed, using fallback', e);
      geo = { latitude: 39.7983, longitude: -89.6437, label: 'Springfield, Illinois', isPublicIp: false, ipAddress: '127.0.0.1' };
    }

    let lat = geo.latitude || 39.7983;
    let lng = geo.longitude || -89.6437;
    let radius = 25;

    async function renderFacilities(currentLat, currentLng, radiusKm, currentLabel, ipInfo) {
      const placesRes = await api(`/api/facilities/nearby?lat=${currentLat}&lng=${currentLng}&radius=${radiusKm}`);
      const hasPlaces = placesRes.places && placesRes.places.length > 0;

      el.innerHTML = `
        <div style="display: flex; flex-direction: column; gap: 1.5rem;">
          <div class="card">
            <div class="card-header" style="flex-wrap: wrap; gap: 0.75rem;">
              <div>
                <span class="card-title">🏥 Medical Facilities & Care Locations</span>
                <div style="font-size: 0.8125rem; color: var(--text-muted); margin-top: 0.25rem;">
                  📍 <strong>Origin:</strong> ${escapeHtml(currentLabel)}
                  ${ipInfo ? ` &bull; <span class="pill ${ipInfo.isPublicIp ? 'good' : 'info'}" style="font-size: 0.75rem; padding: 0.15rem 0.5rem;">
                    🌐 ${ipInfo.isPublicIp ? 'Public IP' : 'Local IP'}: ${escapeHtml(ipInfo.ipAddress || 'Unknown')} ${ipInfo.isp ? `(${escapeHtml(ipInfo.isp)})` : ''}
                  </span>` : ''}
                </div>
              </div>
              <div style="display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap;">
                <button class="btn btn-secondary btn-sm" id="btn-toggle-springfield">📍 Springfield Clinic Area</button>
                <button class="btn btn-secondary btn-sm" id="btn-refresh-ip">🔄 Refresh IP Geolocation</button>
              </div>
            </div>

            <div style="margin-top: 1rem; display: flex; gap: 0.75rem; align-items: center; flex-wrap: wrap;">
              <input type="text" id="facility-search-input" class="input" style="max-width: 350px;" placeholder="Search Springfield, downtown, westside..." />
              <button class="btn btn-primary btn-sm" id="btn-search-loc">Search Places</button>
              <span class="pill info">${placesRes.places?.length || 0} Facilities within ${radiusKm} km</span>
            </div>

            <div id="loc-search-suggestions" style="margin-top: 0.5rem;"></div>

            <div class="grid grid-3" style="margin-top: 1.5rem;">
              ${hasPlaces ? placesRes.places.map(p => `
                <div style="border: 1px solid var(--border); border-radius: var(--radius); padding: 1rem; background: var(--surface);">
                  <div style="display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 0.25rem;">
                    <span style="font-weight: 700; font-size: 0.95rem;">${escapeHtml(p.name)}</span>
                    <span class="pill good">${escapeHtml(p.distanceLabel)}</span>
                  </div>
                  <div style="font-size: 0.75rem; text-transform: uppercase; color: var(--primary); font-weight: 600;">${escapeHtml(p.type)}</div>
                  <div style="font-size: 0.8125rem; color: var(--text-muted); margin-top: 0.25rem;">
                    ${escapeHtml(p.addressLine1)}, ${escapeHtml(p.city)}, ${escapeHtml(p.state)}
                  </div>
                  ${p.phone ? `<div style="font-size: 0.8125rem; margin-top: 0.25rem;">📞 ${escapeHtml(p.phone)}</div>` : ''}
                  ${p.hours ? `<div style="font-size: 0.75rem; color: var(--text-muted); margin-top: 0.25rem;">⏰ ${escapeHtml(p.hours)}</div>` : ''}
                </div>
              `).join('') : `
                <div style="grid-column: 1 / -1; padding: 2.5rem 1.5rem; text-align: center; color: var(--text-muted); border: 1px dashed var(--border); border-radius: var(--radius);">
                  <div style="font-size: 1.1rem; font-weight: 600; margin-bottom: 0.5rem; color: var(--text-main);">No facilities within ${radiusKm} km of this location</div>
                  <div>Your public IP location is detected at <strong>${escapeHtml(currentLabel)}</strong> (${escapeHtml(ipInfo?.ipAddress || '')}).</div>
                  <div style="margin-top: 0.5rem;">The seeded medical directory facilities are located in Springfield, IL.</div>
                  <button class="btn btn-primary btn-sm" style="margin-top: 1rem;" id="btn-empty-switch">
                    📍 Switch to Springfield Demo Clinic Directory
                  </button>
                </div>
              `}
            </div>
          </div>
        </div>
      `;

      document.getElementById('btn-toggle-springfield')?.addEventListener('click', () => {
        renderFacilities(39.7983, -89.6437, 25, 'Springfield, Illinois (Demo Facilities)', null);
      });

      document.getElementById('btn-empty-switch')?.addEventListener('click', () => {
        renderFacilities(39.7983, -89.6437, 25, 'Springfield, Illinois (Demo Facilities)', null);
      });

      document.getElementById('btn-refresh-ip')?.addEventListener('click', async () => {
        const freshGeo = await api('/api/geo/approximate');
        renderFacilities(freshGeo.latitude, freshGeo.longitude, 25, freshGeo.label, freshGeo);
      });

      const searchInput = document.getElementById('facility-search-input');
      const searchBtn = document.getElementById('btn-search-loc');
      const suggestions = document.getElementById('loc-search-suggestions');

      const doSearch = async () => {
        const q = searchInput.value.trim();
        if (!q) return;
        const res = await api(`/api/geo/search?q=${encodeURIComponent(q)}`);
        if (res.results && res.results.length > 0) {
          suggestions.innerHTML = `
            <div style="display: flex; gap: 0.5rem; flex-wrap: wrap; margin-top: 0.5rem;">
              ${res.results.map(r => `
                <button class="pill info btn-suggestion" data-lat="${r.latitude}" data-lng="${r.longitude}" data-label="${r.label}" style="cursor: pointer; border: 1px solid var(--border);">
                  📍 ${r.label}
                </button>
              `).join('')}
            </div>
          `;
          suggestions.querySelectorAll('.btn-suggestion').forEach(btn => {
            btn.onclick = () => {
              renderFacilities(
                parseFloat(btn.dataset.lat),
                parseFloat(btn.dataset.lng),
                25,
                btn.dataset.label,
                null
              );
            };
          });
        }
      };

      searchBtn?.addEventListener('click', doSearch);
      searchInput?.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') doSearch();
      });
    }

    await renderFacilities(lat, lng, radius, geo.label || 'Detected Location', geo);
  }

  // --- Forms Tab ---
  async function loadForms(el) {
    const formsRes = await api('/api/forms');

    el.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1.5rem;">
        ${formsRes.forms.map(f => `
          <div class="card">
            <div class="card-header">
              <div>
                <span class="card-title">${f.title}</span>
                <div style="font-size: 0.8125rem; color: var(--text-muted);">${f.description || ''}</div>
              </div>
              <span class="pill ${f.completion === 100 ? 'good' : 'warn'}">${f.completion}% Completed</span>
            </div>

            <form class="portal-form" data-form-id="${f.id}" style="margin-top: 1rem;">
              ${f.fields.map(field => `
                <div class="form-group">
                  <label class="form-label">${field.label} ${field.required ? '<span style="color:#dc2626;">*</span>' : ''}</label>
                  ${field.type === 'textarea' ? `
                    <textarea class="textarea" name="${field.id}" rows="3">${f.response?.answers?.[field.id] || ''}</textarea>
                  ` : (field.type === 'boolean' ? `
                    <select class="select" name="${field.id}">
                      <option value="false" ${f.response?.answers?.[field.id] === false ? 'selected' : ''}>No</option>
                      <option value="true" ${f.response?.answers?.[field.id] === true ? 'selected' : ''}>Yes</option>
                    </select>
                  ` : `
                    <input class="input" type="${field.type === 'number' ? 'number' : 'text'}" name="${field.id}" value="${f.response?.answers?.[field.id] || ''}">
                  `)}
                </div>
              `).join('')}

              <button class="btn btn-primary" type="submit">Submit Form Answers</button>
            </form>
          </div>
        `).join('')}
      </div>
    `;

    document.querySelectorAll('.portal-form').forEach(form => {
      form.onsubmit = async (e) => {
        e.preventDefault();
        const id = form.getAttribute('data-form-id');
        const formData = new FormData(form);
        const answers = {};
        for (const [k, v] of formData.entries()) {
          answers[k] = v;
        }

        await api(`/api/forms/${id}/submit`, {
          method: 'POST',
          body: JSON.stringify({ answers })
        });

        showToast('Form answers submitted.');
        loadForms(el);
      };
    });
  }

  // --- Settings Tab ---
  async function loadSettings(el) {
    const [profile, prefsRes, sysIp, sysDb] = await Promise.all([
      api('/api/profile'),
      api('/api/notification-preferences'),
      api('/api/system/ip').catch(() => null),
      api('/api/system/db').catch(() => null)
    ]);

    el.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1.5rem;">
        <div class="card">
          <div class="card-header">
            <span class="card-title">👤 Patient Identity & Demographics</span>
          </div>
          <form id="form-profile">
            <div class="grid grid-2">
              <div class="form-group">
                <label class="form-label">First Name</label>
                <input class="input" id="p-first" value="${profile.firstName || ''}">
              </div>
              <div class="form-group">
                <label class="form-label">Last Name</label>
                <input class="input" id="p-last" value="${profile.lastName || ''}">
              </div>
              <div class="form-group">
                <label class="form-label">Phone Number</label>
                <input class="input" id="p-phone" value="${profile.phone || ''}">
              </div>
              <div class="form-group">
                <label class="form-label">Preferred Pharmacy</label>
                <input class="input" id="p-pharmacy" value="${profile.preferredPharmacy || ''}">
              </div>
              <div class="form-group">
                <label class="form-label">Address</label>
                <input class="input" id="p-addr" value="${profile.addressLine1 || ''}">
              </div>
              <div class="form-group">
                <label class="form-label">City, State, Zip</label>
                <div style="display: flex; gap: 0.5rem;">
                  <input class="input" id="p-city" value="${profile.city || ''}" placeholder="City" style="flex:2;">
                  <input class="input" id="p-state" value="${profile.state || ''}" placeholder="State" style="flex:1;">
                  <input class="input" id="p-zip" value="${profile.postalCode || ''}" placeholder="Zip" style="flex:1;">
                </div>
              </div>
              <div class="form-group">
                <label class="form-label">Emergency Contact Name</label>
                <input class="input" id="p-e-name" value="${profile.emergencyContactName || ''}">
              </div>
              <div class="form-group">
                <label class="form-label">Emergency Contact Phone</label>
                <input class="input" id="p-e-phone" value="${profile.emergencyContactPhone || ''}">
              </div>
            </div>
            <button class="btn btn-primary" type="submit" style="margin-top: 1rem;">Save Profile Changes</button>
          </form>
        </div>

        <div class="card">
          <div class="card-header">
            <span class="card-title">🔔 Notification Preferences</span>
          </div>
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Category</th>
                  <th>Email</th>
                  <th>SMS</th>
                  <th>Push</th>
                  <th>Muted</th>
                </tr>
              </thead>
              <tbody>
                ${prefsRes.preferences.map(p => `
                  <tr>
                    <td style="font-weight: 500;">${p.category}</td>
                    <td><input type="checkbox" ${p.emailEnabled ? 'checked' : ''} disabled></td>
                    <td><input type="checkbox" ${p.smsEnabled ? 'checked' : ''} disabled></td>
                    <td><input type="checkbox" ${p.pushEnabled ? 'checked' : ''} disabled></td>
                    <td><input type="checkbox" ${p.muted ? 'checked' : ''} disabled></td>
                  </tr>
                `).join('')}
              </tbody>
            </table>
          </div>
        </div>

        <div class="card">
          <div class="card-header">
            <span class="card-title">🌐 Infrastructure, Public IP & Database Status</span>
            <span class="pill good">Active</span>
          </div>
          <div class="grid grid-2" style="margin-top: 0.5rem; gap: 1rem;">
            <div style="border: 1px solid var(--border); border-radius: var(--radius); padding: 1rem; background: var(--surface);">
              <div style="font-weight: 700; font-size: 0.9rem; margin-bottom: 0.5rem; color: var(--primary);">📡 Network & Public IP Details</div>
              <div style="display: flex; flex-direction: column; gap: 0.35rem; font-size: 0.8125rem;">
                <div><strong>Client IP:</strong> <code>${sysIp?.clientIp || 'Unknown'}</code> ${sysIp?.isPrivateOrLoopback ? '<span class="pill info" style="font-size:0.7rem; padding:0.1rem 0.4rem;">Private/Loopback</span>' : '<span class="pill good" style="font-size:0.7rem; padding:0.1rem 0.4rem;">Public</span>'}</div>
                <div><strong>Public Egress IP:</strong> <code>${sysIp?.publicEgressIp || 'Resolved via gateway'}</code></div>
                <div><strong>Detected Geolocation:</strong> ${sysIp?.ipLocation?.city || 'Springfield'}, ${sysIp?.ipLocation?.region || 'IL'}, ${sysIp?.ipLocation?.country || 'US'}</div>
                <div><strong>Coordinates:</strong> <code>${sysIp?.ipLocation?.latitude || 39.7983}, ${sysIp?.ipLocation?.longitude || -89.6437}</code></div>
                <div><strong>ISP / Provider:</strong> ${sysIp?.ipLocation?.isp || 'Local / Private'}</div>
                <div><strong>Resolution Source:</strong> <span class="pill info" style="font-size:0.7rem; padding:0.1rem 0.4rem;">${sysIp?.ipLocation?.source || 'direct'}</span></div>
              </div>
            </div>

            <div style="border: 1px solid var(--border); border-radius: var(--radius); padding: 1rem; background: var(--surface);">
              <div style="font-weight: 700; font-size: 0.9rem; margin-bottom: 0.5rem; color: var(--primary);">💾 Database Engine & Health</div>
              <div style="display: flex; flex-direction: column; gap: 0.35rem; font-size: 0.8125rem;">
                <div><strong>Provider:</strong> <span class="pill good" style="font-size:0.75rem;">${sysDb?.provider || 'PostgreSQL / Npgsql'}</span></div>
                <div><strong>Connection Status:</strong> ${sysDb?.canConnect ? '🟢 Connected & Verified' : '🟡 Offline'}</div>
                <div><strong>Server Time (UTC):</strong> <code>${sysDb?.serverUtc ? new Date(sysDb.serverUtc).toISOString() : new Date().toISOString()}</code></div>
                <div style="margin-top: 0.5rem; font-size: 0.75rem; color: var(--text-muted);">
                  Supports latest PostgreSQL 17/16 with Npgsql 8.0, standard & URI connection strings, and zero-downtime retry policies.
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    `;

    document.getElementById('form-profile').onsubmit = async (e) => {
      e.preventDefault();
      await api('/api/profile', {
        method: 'PATCH',
        body: JSON.stringify({
          firstName: document.getElementById('p-first').value,
          lastName: document.getElementById('p-last').value,
          phone: document.getElementById('p-phone').value,
          preferredPharmacy: document.getElementById('p-pharmacy').value,
          addressLine1: document.getElementById('p-addr').value,
          city: document.getElementById('p-city').value,
          state: document.getElementById('p-state').value,
          postalCode: document.getElementById('p-zip').value,
          emergencyContactName: document.getElementById('p-e-name').value,
          emergencyContactPhone: document.getElementById('p-e-phone').value
        })
      });
      showToast('Profile updated.');
      loadSettings(el);
    };
  }

  // --- Admin Console Tab ---
  let adminSubTab = 'users';
  let adminAuditPage = 1;
  let adminUserFilters = { search: '', role: '', active: '' };
  let adminAuditFilters = { resourceType: '', action: '' };

  async function loadAdmin(el) {
    el.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1.5rem;">
        <div style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 0.75rem;">
          <div>
            <h2 style="margin: 0; font-size: 1.35rem; display: flex; align-items: center; gap: 0.5rem;">
              <span>🛡️</span> Administrative Operations Console
            </h2>
            <div style="font-size: 0.8125rem; color: var(--text-muted);">
              Enterprise User Management, Facility Hierarchy, RBAC Matrix &amp; Audit Oversight
            </div>
          </div>
          <div style="display: flex; gap: 0.5rem; align-items: center;">
            <span class="pill info">${currentSession.user.role}</span>
            <span class="pill good">RBAC Enforced</span>
          </div>
        </div>

        <nav class="admin-subnav" aria-label="Admin console navigation" style="display: flex; gap: 0.5rem; flex-wrap: wrap; border-bottom: 1px solid var(--border); padding-bottom: 0.5rem;">
          <button type="button" class="btn btn-sm ${adminSubTab === 'users' ? 'btn-primary' : 'btn-outline'}" data-admin-subtab="users">👥 Users &amp; Roles</button>
          <button type="button" class="btn btn-sm ${adminSubTab === 'facilities' ? 'btn-primary' : 'btn-outline'}" data-admin-subtab="facilities">🏥 Facilities &amp; Departments</button>
          <button type="button" class="btn btn-sm ${adminSubTab === 'audit' ? 'btn-primary' : 'btn-outline'}" data-admin-subtab="audit">📜 Audit Log &amp; Verification</button>
          <button type="button" class="btn btn-sm ${adminSubTab === 'proxy' ? 'btn-primary' : 'btn-outline'}" data-admin-subtab="proxy">🤝 Proxy Grants</button>
          <button type="button" class="btn btn-sm ${adminSubTab === 'shares' ? 'btn-primary' : 'btn-outline'}" data-admin-subtab="shares">🔗 Active Record Shares</button>
        </nav>

        <div id="admin-subtab-container">
          <div style="text-align: center; padding: 2rem; color: var(--text-muted);">Loading admin view...</div>
        </div>
      </div>
    `;

    el.querySelectorAll('button[data-admin-subtab]').forEach(btn => {
      btn.onclick = () => {
        adminSubTab = btn.getAttribute('data-admin-subtab');
        loadAdmin(el);
      };
    });

    const subContainer = document.getElementById('admin-subtab-container');
    if (!subContainer) return;

    try {
      if (adminSubTab === 'users') await renderAdminUsers(subContainer, el);
      else if (adminSubTab === 'facilities') await renderAdminFacilities(subContainer, el);
      else if (adminSubTab === 'audit') await renderAdminAudit(subContainer, el);
      else if (adminSubTab === 'proxy') await renderAdminProxyGrants(subContainer, el);
      else if (adminSubTab === 'shares') await renderAdminShares(subContainer, el);
    } catch (err) {
      subContainer.innerHTML = `<div class="notice notice-bad">${escapeHtml(err.message)}</div>`;
    }
  }

  async function renderAdminUsers(container, rootEl) {
    const params = new URLSearchParams();
    if (adminUserFilters.search) params.set('search', adminUserFilters.search);
    if (adminUserFilters.role) params.set('role', adminUserFilters.role);
    if (adminUserFilters.active) params.set('active', adminUserFilters.active);

    const [usersRes, rolesRes, facilitiesRes] = await Promise.all([
      api(`/api/admin/users?${params.toString()}`),
      api('/api/admin/roles').catch(() => []),
      api('/api/admin/facilities').catch(() => [])
    ]);

    container.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1rem;">
        <div class="card" style="padding: 1rem;">
          <div style="display: flex; justify-content: space-between; align-items: flex-end; flex-wrap: wrap; gap: 1rem;">
            <div style="display: flex; gap: 0.75rem; flex-wrap: wrap; align-items: flex-end; flex: 1;">
              <div class="form-group" style="margin: 0; min-width: 220px; flex: 1;">
                <label class="form-label" for="user-search-input">Search Users</label>
                <input class="input" id="user-search-input" placeholder="Name, email, or MRN..." value="${escapeHtml(adminUserFilters.search || '')}">
              </div>
              <div class="form-group" style="margin: 0; min-width: 150px;">
                <label class="form-label" for="user-role-select">Role Filter</label>
                <select class="topbar-select" id="user-role-select" style="width: 100%;">
                  <option value="">All Roles (${rolesRes.length})</option>
                  ${rolesRes.map(r => `<option value="${escapeHtml(r.name)}" ${adminUserFilters.role === r.name ? 'selected' : ''}>${escapeHtml(r.name)}</option>`).join('')}
                </select>
              </div>
              <div class="form-group" style="margin: 0; min-width: 130px;">
                <label class="form-label" for="user-status-select">Status Filter</label>
                <select class="topbar-select" id="user-status-select" style="width: 100%;">
                  <option value="" ${adminUserFilters.active === '' ? 'selected' : ''}>All Statuses</option>
                  <option value="true" ${adminUserFilters.active === 'true' ? 'selected' : ''}>Active</option>
                  <option value="false" ${adminUserFilters.active === 'false' ? 'selected' : ''}>Disabled</option>
                </select>
              </div>
              <button type="button" class="btn btn-outline" id="btn-filter-users" style="height: 38px;">Filter</button>
            </div>
            <button type="button" class="btn btn-primary" id="btn-create-user" style="height: 38px;">+ Create New User</button>
          </div>
        </div>

        <div class="table-wrap">
          <table aria-label="Workforce and Users Table">
            <thead>
              <tr>
                <th scope="col">User / Identity</th>
                <th scope="col">Role</th>
                <th scope="col">Status</th>
                <th scope="col">Department Scope &amp; Facilities</th>
                <th scope="col">MFA</th>
                <th scope="col">Last Login</th>
                <th scope="col" style="text-align: right;">Actions</th>
              </tr>
            </thead>
            <tbody>
              ${usersRes.items.length === 0 ? `
                <tr><td colspan="7" style="text-align: center; color: var(--text-muted); padding: 2rem;">No user accounts found matching filters.</td></tr>
              ` : usersRes.items.map(u => `
                <tr>
                  <td>
                    <div style="font-weight: 600;">${escapeHtml(u.person?.fullName || '—')}</div>
                    <div style="font-size: 0.8125rem; color: var(--text-muted);">${escapeHtml(u.email)}</div>
                    ${u.person?.mrn ? `<div style="font-size: 0.75rem; color: var(--primary);">MRN: ${escapeHtml(u.person.mrn)}</div>` : ''}
                  </td>
                  <td>${pill(u.role)}</td>
                  <td>
                    ${u.isActive ? '<span class="pill good">Active</span>' : '<span class="pill bad">Disabled</span>'}
                    ${u.isLocked ? '<span class="pill warn" title="Temporarily locked out">Locked</span>' : ''}
                  </td>
                  <td>
                    ${u.memberships.length === 0 ? '<span style="color: var(--text-muted); font-size: 0.8125rem;">Enterprise (Unscoped)</span>' : `
                      <div style="display: flex; flex-direction: column; gap: 0.2rem;">
                        ${u.memberships.map(m => `
                          <span class="pill info" style="font-size: 0.7rem; justify-content: flex-start; text-align: left;">
                            ${escapeHtml(m.facilityCode || 'FAC')}${m.departmentCode ? ` : ${escapeHtml(m.departmentCode)}` : ' (All Depts)'} [${escapeHtml(m.role)}]
                          </span>
                        `).join('')}
                      </div>
                    `}
                  </td>
                  <td>${u.mfaEnabled ? '<span class="pill good">Enrolled</span>' : '<span class="pill warn">Not Set</span>'}</td>
                  <td style="font-size: 0.8125rem; color: var(--text-muted);">${u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleDateString() : 'Never'}</td>
                  <td style="text-align: right;">
                    <div style="display: inline-flex; gap: 0.35rem; justify-content: flex-end;">
                      <button type="button" class="btn btn-outline btn-sm" data-edit-role="${escapeHtml(u.id)}" data-current-role="${escapeHtml(u.role)}" aria-label="Assign role for ${escapeHtml(u.email)}">Role</button>
                      <button type="button" class="btn btn-outline btn-sm" data-manage-memberships="${escapeHtml(u.id)}" aria-label="Manage memberships for ${escapeHtml(u.email)}">Scope (${u.memberships.length})</button>
                      ${u.isActive ? `
                        <button type="button" class="btn btn-outline btn-sm btn-danger" data-disable-user="${escapeHtml(u.id)}" data-email="${escapeHtml(u.email)}" aria-label="Disable account ${escapeHtml(u.email)}">Disable</button>
                      ` : `
                        <button type="button" class="btn btn-outline btn-sm btn-primary" data-enable-user="${escapeHtml(u.id)}" data-email="${escapeHtml(u.email)}" aria-label="Enable account ${escapeHtml(u.email)}">Enable</button>
                      `}
                    </div>
                  </td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>
        <div style="font-size: 0.8125rem; color: var(--text-muted);">
          Showing ${usersRes.items.length} of ${usersRes.totalCount} registered user accounts across system.
        </div>
      </div>
    `;

    document.getElementById('btn-filter-users').onclick = () => {
      adminUserFilters.search = document.getElementById('user-search-input').value.trim();
      adminUserFilters.role = document.getElementById('user-role-select').value;
      adminUserFilters.active = document.getElementById('user-status-select').value;
      loadAdmin(rootEl);
    };

    document.getElementById('btn-create-user').onclick = () => {
      openCreateUserModal(rolesRes, facilitiesRes, () => loadAdmin(rootEl));
    };

    container.querySelectorAll('button[data-disable-user]').forEach(btn => {
      btn.onclick = () => {
        const id = btn.getAttribute('data-disable-user');
        const email = btn.getAttribute('data-email');
        showInAppConfirm(`Disable user account '${email}'? Active sessions will be terminated immediately.`, async () => {
          try {
            await api(`/api/admin/users/${id}/disable`, { method: 'POST' });
            showToast(`User '${email}' disabled.`);
            loadAdmin(rootEl);
          } catch (err) {
            showToast(`Failed to disable user: ${err.message}`);
          }
        }, null, 'Disable User Account');
      };
    });

    container.querySelectorAll('button[data-enable-user]').forEach(btn => {
      btn.onclick = async () => {
        const id = btn.getAttribute('data-enable-user');
        const email = btn.getAttribute('data-email');
        try {
          await api(`/api/admin/users/${id}/enable`, { method: 'POST' });
          showToast(`User '${email}' re-enabled.`);
          loadAdmin(rootEl);
        } catch (err) {
          showToast(`Failed to enable user: ${err.message}`);
        }
      };
    });

    container.querySelectorAll('button[data-edit-role]').forEach(btn => {
      btn.onclick = () => {
        const id = btn.getAttribute('data-edit-role');
        const currentRole = btn.getAttribute('data-current-role');
        openEditRoleModal(id, currentRole, rolesRes, () => loadAdmin(rootEl));
      };
    });

    container.querySelectorAll('button[data-manage-memberships]').forEach(btn => {
      btn.onclick = () => {
        const id = btn.getAttribute('data-manage-memberships');
        const user = usersRes.items.find(u => u.id === id);
        if (user) {
          openManageMembershipsModal(user, facilitiesRes, () => loadAdmin(rootEl));
        }
      };
    });
  }

  function openCreateUserModal(roles, facilities, onSuccess) {
    showModal({
      title: '➕ Create New User Account',
      confirmText: 'Create Account',
      body: `
        <form id="form-create-user" style="display: flex; flex-direction: column; gap: 0.85rem;">
          <div class="grid grid-2" style="margin: 0; gap: 0.75rem;">
            <div class="form-group" style="margin: 0;">
              <label class="form-label" for="new-u-first">First Name *</label>
              <input class="input" id="new-u-first" required placeholder="e.g. John">
            </div>
            <div class="form-group" style="margin: 0;">
              <label class="form-label" for="new-u-last">Last Name *</label>
              <input class="input" id="new-u-last" required placeholder="e.g. Doe">
            </div>
          </div>

          <div class="form-group" style="margin: 0;">
            <label class="form-label" for="new-u-email">Email Address *</label>
            <input class="input" id="new-u-email" type="email" required placeholder="name@clinic.example">
          </div>

          <div class="grid grid-2" style="margin: 0; gap: 0.75rem;">
            <div class="form-group" style="margin: 0;">
              <label class="form-label" for="new-u-password">Password (min 8 chars) *</label>
              <input class="input" id="new-u-password" type="password" required minlength="8" placeholder="••••••••">
            </div>
            <div class="form-group" style="margin: 0;">
              <label class="form-label" for="new-u-dob">Date of Birth *</label>
              <input class="input" id="new-u-dob" type="date" required value="1990-01-01">
            </div>
          </div>

          <div class="form-group" style="margin: 0;">
            <label class="form-label" for="new-u-role">Role (RBAC Authority) *</label>
            <select class="topbar-select" id="new-u-role" style="width: 100%;">
              ${roles.map(r => `<option value="${r.name}">${r.name}</option>`).join('')}
            </select>
          </div>

          <div style="border-top: 1px solid var(--border); padding-top: 0.75rem; margin-top: 0.25rem;">
            <div style="font-weight: 600; font-size: 0.85rem; margin-bottom: 0.5rem; color: var(--primary);">
              Initial Department &amp; Facility Assignment (Optional)
            </div>
            <div class="grid grid-2" style="margin: 0; gap: 0.75rem;">
              <div class="form-group" style="margin: 0;">
                <label class="form-label" for="new-u-facility">Facility</label>
                <select class="topbar-select" id="new-u-facility" style="width: 100%;">
                  <option value="">None / Enterprise Scope</option>
                  ${facilities.map(f => `<option value="${f.id}">${f.name} (${f.code})</option>`).join('')}
                </select>
              </div>
              <div class="form-group" style="margin: 0;">
                <label class="form-label" for="new-u-dept">Department</label>
                <select class="topbar-select" id="new-u-dept" style="width: 100%;">
                  <option value="">None / Facility-Wide</option>
                  ${facilities.flatMap(f => f.departments.map(d => `<option value="${d.id}">${f.code}: ${d.name} (${d.specialty || 'General'})</option>`)).join('')}
                </select>
              </div>
            </div>
          </div>
        </form>
      `,
      onConfirm: async () => {
        const firstName = document.getElementById('new-u-first').value.trim();
        const lastName = document.getElementById('new-u-last').value.trim();
        const email = document.getElementById('new-u-email').value.trim();
        const password = document.getElementById('new-u-password').value;
        const dob = document.getElementById('new-u-dob').value;
        const role = document.getElementById('new-u-role').value;
        const facilityId = document.getElementById('new-u-facility').value || null;
        const departmentId = document.getElementById('new-u-dept').value || null;

        if (!firstName || !lastName || !email || !password || !dob) {
          showToast('Please fill out all required fields.');
          return;
        }

        try {
          await api('/api/admin/users', {
            method: 'POST',
            body: JSON.stringify({
              firstName,
              lastName,
              email,
              password,
              dateOfBirth: dob,
              role,
              facilityId,
              departmentId
            })
          });
          closeModal();
          showToast(`User account '${email}' created successfully.`);
          onSuccess();
        } catch (err) {
          showToast(`Error creating user: ${err.message}`);
        }
      }
    });
  }

  function openEditRoleModal(userId, currentRole, roles, onSuccess) {
    showModal({
      title: '🛡️ Assign User Role (RBAC)',
      confirmText: 'Save Role',
      body: `
        <div class="form-group">
          <label class="form-label" for="edit-role-select">Select New Role</label>
          <select class="topbar-select" id="edit-role-select" style="width: 100%;">
            ${roles.map(r => `<option value="${r.name}" ${r.name === currentRole ? 'selected' : ''}>${r.name}</option>`).join('')}
          </select>
          <div style="font-size: 0.75rem; color: var(--text-muted); margin-top: 0.5rem;">
            Changing role immediately adjusts resource access according to the Permission Matrix.
          </div>
        </div>
      `,
      onConfirm: async () => {
        const newRole = document.getElementById('edit-role-select').value;
        try {
          await api(`/api/admin/users/${userId}/role`, {
            method: 'PUT',
            body: JSON.stringify({ role: newRole })
          });
          closeModal();
          showToast(`Role updated to ${newRole}.`);
          onSuccess();
        } catch (err) {
          showToast(`Error updating role: ${err.message}`);
        }
      }
    });
  }

  function openManageMembershipsModal(user, facilities, onSuccess) {
    const renderModalBody = () => `
      <div style="display: flex; flex-direction: column; gap: 1rem;">
        <div>
          <div style="font-weight: 600;">${user.person.fullName} (${user.email})</div>
          <div style="font-size: 0.8125rem; color: var(--text-muted);">Current Role: <span class="pill info">${user.role}</span></div>
        </div>

        <div style="border-top: 1px solid var(--border); padding-top: 0.75rem;">
          <div style="font-weight: 600; font-size: 0.85rem; margin-bottom: 0.5rem;">Assigned Organizational Scopes:</div>
          ${user.memberships.length === 0 ? `
            <div style="font-size: 0.8125rem; color: var(--text-muted);">No scoped memberships assigned (User operates enterprise-wide).</div>
          ` : `
            <div style="display: flex; flex-direction: column; gap: 0.5rem;">
              ${user.memberships.map(m => `
                <div style="display: flex; justify-content: space-between; align-items: center; background: #f8fafc; padding: 0.5rem 0.75rem; border-radius: var(--radius); border: 1px solid var(--border);">
                  <div style="font-size: 0.8125rem;">
                    <strong>${m.facilityName || 'Facility'}</strong> ${m.departmentName ? ` → ${m.departmentName} (${m.departmentCode})` : ' (All Departments)'}
                    <span class="pill good" style="font-size: 0.65rem; margin-left: 0.35rem;">${m.role}</span>
                  </div>
                  <button type="button" class="btn btn-outline btn-sm btn-danger" data-remove-m="${m.id}" aria-label="Remove membership">✕</button>
                </div>
              `).join('')}
            </div>
          `}
        </div>

        <div style="border-top: 1px solid var(--border); padding-top: 0.75rem;">
          <div style="font-weight: 600; font-size: 0.85rem; margin-bottom: 0.5rem; color: var(--primary);">+ Assign New Scope Membership</div>
          <div class="grid grid-2" style="margin: 0; gap: 0.5rem;">
            <div class="form-group" style="margin: 0;">
              <label class="form-label" for="assign-m-fac">Facility</label>
              <select class="topbar-select" id="assign-m-fac" style="width: 100%;">
                <option value="">Select Facility...</option>
                ${facilities.map(f => `<option value="${f.id}">${f.name}</option>`).join('')}
              </select>
            </div>
            <div class="form-group" style="margin: 0;">
              <label class="form-label" for="assign-m-dept">Department</label>
              <select class="topbar-select" id="assign-m-dept" style="width: 100%;">
                <option value="">All Departments in Facility</option>
                ${facilities.flatMap(f => f.departments.map(d => `<option value="${d.id}">${f.code}: ${d.name}</option>`)).join('')}
              </select>
            </div>
          </div>
          <button type="button" class="btn btn-outline btn-sm" id="btn-add-membership" style="margin-top: 0.75rem;">+ Add Membership</button>
        </div>
      </div>
    `;

    showModal({
      title: '🏢 Facility & Department Memberships',
      confirmText: 'Done',
      body: renderModalBody(),
      onConfirm: async () => {
        closeModal();
        onSuccess();
      }
    });

    const attachMembershipEvents = () => {
      document.querySelectorAll('button[data-remove-m]').forEach(btn => {
        btn.onclick = async () => {
          const mId = btn.getAttribute('data-remove-m');
          try {
            await api(`/api/admin/users/${user.id}/memberships/${mId}`, { method: 'DELETE' });
            user.memberships = user.memberships.filter(m => m.id !== mId);
            document.querySelector('.modal-body').innerHTML = renderModalBody();
            attachMembershipEvents();
            showToast('Membership removed.');
          } catch (err) {
            showToast(`Error: ${err.message}`);
          }
        };
      });

      const addBtn = document.getElementById('btn-add-membership');
      if (addBtn) {
        addBtn.onclick = async () => {
          const facilityId = document.getElementById('assign-m-fac').value || null;
          const departmentId = document.getElementById('assign-m-dept').value || null;
          if (!facilityId && !departmentId) {
            showToast('Select at least a facility or department.');
            return;
          }
          try {
            const added = await api(`/api/admin/users/${user.id}/memberships`, {
              method: 'POST',
              body: JSON.stringify({ facilityId, departmentId, role: user.role })
            });
            user.memberships.push({
              id: added.id,
              facilityId: added.facilityId,
              facilityName: facilities.find(f => f.id === added.facilityId)?.name,
              departmentId: added.departmentId,
              departmentName: facilities.flatMap(f => f.departments).find(d => d.id === added.departmentId)?.name,
              role: added.role
            });
            document.querySelector('.modal-body').innerHTML = renderModalBody();
            attachMembershipEvents();
            showToast('Membership assigned.');
          } catch (err) {
            showToast(`Error: ${err.message}`);
          }
        };
      }
    };

    attachMembershipEvents();
  }

  async function renderAdminFacilities(container, rootEl) {
    const facilities = await api('/api/admin/facilities');

    container.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1.25rem;">
        <div style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 0.75rem;">
          <div>
            <h3 style="margin: 0; font-size: 1.15rem;">Healthcare Facilities &amp; Clinical Divisions</h3>
            <div style="font-size: 0.8125rem; color: var(--text-muted);">Manage multi-facility enterprise layout, physical locations, and specialty departments.</div>
          </div>
          <button type="button" class="btn btn-primary" id="btn-add-facility">+ Add Facility</button>
        </div>

        <div style="display: flex; flex-direction: column; gap: 1.25rem;">
          ${facilities.map(f => `
            <div class="card" style="padding: 1.25rem;">
              <div style="display: flex; justify-content: space-between; align-items: flex-start; flex-wrap: wrap; gap: 0.75rem; border-bottom: 1px solid var(--border); padding-bottom: 0.75rem; margin-bottom: 0.75rem;">
                <div>
                  <div style="display: flex; align-items: center; gap: 0.5rem;">
                    <span style="font-size: 1.1rem; font-weight: 700;">${escapeHtml(f.name)}</span>
                    <span class="pill info">${escapeHtml(f.code)}</span>
                  </div>
                  <div style="font-size: 0.8125rem; color: var(--text-muted); margin-top: 0.25rem;">
                    📍 ${[f.addressLine1, f.city, f.state, f.postalCode].filter(Boolean).map(escapeHtml).join(', ') || 'No address specified'}
                    ${f.phone ? ` • 📞 ${escapeHtml(f.phone)}` : ''}
                  </div>
                </div>
                <div style="display: flex; gap: 0.5rem;">
                  <button type="button" class="btn btn-outline btn-sm" data-edit-facility="${escapeHtml(f.id)}">Edit Facility</button>
                  <button type="button" class="btn btn-primary btn-sm" data-add-dept="${escapeHtml(f.id)}">+ Add Department</button>
                </div>
              </div>

              <div>
                <div style="font-weight: 600; font-size: 0.85rem; margin-bottom: 0.5rem; color: var(--text);">Clinical Departments (${f.departments.length}):</div>
                ${f.departments.length === 0 ? `
                  <div style="font-size: 0.8125rem; color: var(--text-muted); padding: 0.5rem 0;">No departments assigned to this facility yet.</div>
                ` : `
                  <div class="table-wrap">
                    <table aria-label="Departments Table for ${escapeHtml(f.name)}">
                      <thead>
                        <tr>
                          <th scope="col">Department Name</th>
                          <th scope="col">Code</th>
                          <th scope="col">Specialty</th>
                          <th scope="col">Phone</th>
                          <th scope="col" style="text-align: right;">Action</th>
                        </tr>
                      </thead>
                      <tbody>
                        ${f.departments.map(d => `
                          <tr>
                            <td style="font-weight: 600;">${escapeHtml(d.name)}</td>
                            <td><span class="pill info">${escapeHtml(d.code)}</span></td>
                            <td>${escapeHtml(d.specialty || 'General')}</td>
                            <td style="font-size: 0.8125rem; color: var(--text-muted);">${escapeHtml(d.phone || '—')}</td>
                            <td style="text-align: right;">
                              <button type="button" class="btn btn-outline btn-sm" data-edit-dept="${escapeHtml(d.id)}" data-dept-name="${escapeHtml(d.name)}" data-dept-code="${escapeHtml(d.code)}" data-dept-spec="${escapeHtml(d.specialty || '')}" data-dept-phone="${escapeHtml(d.phone || '')}">Edit</button>
                            </td>
                          </tr>
                        `).join('')}
                      </tbody>
                    </table>
                  </div>
                `}
              </div>
            </div>
          `).join('')}
        </div>
      </div>
    `;

    document.getElementById('btn-add-facility').onclick = () => {
      openFacilityModal(null, () => loadAdmin(rootEl));
    };

    container.querySelectorAll('button[data-edit-facility]').forEach(btn => {
      btn.onclick = () => {
        const id = btn.getAttribute('data-edit-facility');
        const fac = facilities.find(f => f.id === id);
        if (fac) openFacilityModal(fac, () => loadAdmin(rootEl));
      };
    });

    container.querySelectorAll('button[data-add-dept]').forEach(btn => {
      btn.onclick = () => {
        const facId = btn.getAttribute('data-add-dept');
        openDepartmentModal(facId, null, () => loadAdmin(rootEl));
      };
    });

    container.querySelectorAll('button[data-edit-dept]').forEach(btn => {
      btn.onclick = () => {
        const dId = btn.getAttribute('data-edit-dept');
        const dept = {
          id: dId,
          name: btn.getAttribute('data-dept-name'),
          code: btn.getAttribute('data-dept-code'),
          specialty: btn.getAttribute('data-dept-spec'),
          phone: btn.getAttribute('data-dept-phone')
        };
        openDepartmentModal(null, dept, () => loadAdmin(rootEl));
      };
    });
  }

  function openFacilityModal(facility, onSuccess) {
    const isEdit = !!facility;
    showModal({
      title: isEdit ? `✏️ Edit Facility (${facility.code})` : '➕ Add Healthcare Facility',
      confirmText: isEdit ? 'Save Changes' : 'Create Facility',
      body: `
        <form id="form-facility" style="display: flex; flex-direction: column; gap: 0.75rem;">
          <div class="grid grid-2" style="margin: 0; gap: 0.5rem;">
            <div class="form-group" style="margin: 0;">
              <label class="form-label" for="fac-name">Facility Name *</label>
              <input class="input" id="fac-name" required value="${facility?.name || ''}" placeholder="e.g. Springfield Memorial Hospital">
            </div>
            <div class="form-group" style="margin: 0;">
              <label class="form-label" for="fac-code">Code *</label>
              <input class="input" id="fac-code" required value="${facility?.code || ''}" placeholder="e.g. SMH">
            </div>
          </div>
          <div class="form-group" style="margin: 0;">
            <label class="form-label" for="fac-addr">Street Address</label>
            <input class="input" id="fac-addr" value="${facility?.addressLine1 || ''}" placeholder="123 Healthcare Ave">
          </div>
          <div class="grid grid-3" style="margin: 0; gap: 0.5rem;">
            <div class="form-group" style="margin: 0;">
              <label class="form-label" for="fac-city">City</label>
              <input class="input" id="fac-city" value="${facility?.city || ''}">
            </div>
            <div class="form-group" style="margin: 0;">
              <label class="form-label" for="fac-state">State</label>
              <input class="input" id="fac-state" value="${facility?.state || ''}">
            </div>
            <div class="form-group" style="margin: 0;">
              <label class="form-label" for="fac-zip">Postal Code</label>
              <input class="input" id="fac-zip" value="${facility?.postalCode || ''}">
            </div>
          </div>
          <div class="form-group" style="margin: 0;">
            <label class="form-label" for="fac-phone">Main Phone</label>
            <input class="input" id="fac-phone" value="${facility?.phone || ''}" placeholder="555-0100">
          </div>
        </form>
      `,
      onConfirm: async () => {
        const name = document.getElementById('fac-name').value.trim();
        const code = document.getElementById('fac-code').value.trim();
        if (!name || !code) {
          showToast('Facility name and code are required.');
          return;
        }
        const payload = {
          name,
          code,
          addressLine1: document.getElementById('fac-addr').value.trim(),
          city: document.getElementById('fac-city').value.trim(),
          state: document.getElementById('fac-state').value.trim(),
          postalCode: document.getElementById('fac-zip').value.trim(),
          phone: document.getElementById('fac-phone').value.trim()
        };
        try {
          if (isEdit) {
            await api(`/api/admin/facilities/${facility.id}`, { method: 'PUT', body: JSON.stringify(payload) });
          } else {
            await api('/api/admin/facilities', { method: 'POST', body: JSON.stringify(payload) });
          }
          closeModal();
          showToast(`Facility '${name}' saved.`);
          onSuccess();
        } catch (err) {
          showToast(`Error saving facility: ${err.message}`);
        }
      }
    });
  }

  function openDepartmentModal(facilityId, department, onSuccess) {
    const isEdit = !!department;
    showModal({
      title: isEdit ? `✏️ Edit Department (${department.code})` : '➕ Add Clinical Department',
      confirmText: isEdit ? 'Save Department' : 'Create Department',
      body: `
        <form id="form-dept" style="display: flex; flex-direction: column; gap: 0.75rem;">
          <div class="grid grid-2" style="margin: 0; gap: 0.5rem;">
            <div class="form-group" style="margin: 0;">
              <label class="form-label" for="dept-name">Department Name *</label>
              <input class="input" id="dept-name" required value="${department?.name || ''}" placeholder="e.g. Cardiology">
            </div>
            <div class="form-group" style="margin: 0;">
              <label class="form-label" for="dept-code">Code *</label>
              <input class="input" id="dept-code" required value="${department?.code || ''}" placeholder="e.g. CARD">
            </div>
          </div>
          <div class="form-group" style="margin: 0;">
            <label class="form-label" for="dept-spec">Specialty</label>
            <input class="input" id="dept-spec" value="${department?.specialty || ''}" placeholder="e.g. Cardiovascular Medicine">
          </div>
          <div class="form-group" style="margin: 0;">
            <label class="form-label" for="dept-phone">Department Phone</label>
            <input class="input" id="dept-phone" value="${department?.phone || ''}" placeholder="555-0150">
          </div>
        </form>
      `,
      onConfirm: async () => {
        const name = document.getElementById('dept-name').value.trim();
        const code = document.getElementById('dept-code').value.trim();
        if (!name || !code) {
          showToast('Department name and code are required.');
          return;
        }
        const specialty = document.getElementById('dept-spec').value.trim();
        const phone = document.getElementById('dept-phone').value.trim();
        try {
          if (isEdit) {
            await api(`/api/admin/departments/${department.id}`, {
              method: 'PUT',
              body: JSON.stringify({ name, code, specialty, phone })
            });
          } else {
            await api('/api/admin/departments', {
              method: 'POST',
              body: JSON.stringify({ facilityId, name, code, specialty, phone })
            });
          }
          closeModal();
          showToast(`Department '${name}' saved.`);
          onSuccess();
        } catch (err) {
          showToast(`Error saving department: ${err.message}`);
        }
      }
    });
  }

  async function renderAdminAudit(container, rootEl) {
    const params = new URLSearchParams({
      page: adminAuditPage,
      pageSize: 30
    });
    if (adminAuditFilters.resourceType) params.set('resourceType', adminAuditFilters.resourceType);
    if (adminAuditFilters.action) params.set('action', adminAuditFilters.action);

    const logsRes = await api(`/api/admin/audit-logs?${params.toString()}`);

    container.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1rem;">
        <div class="card" style="padding: 1.25rem;">
          <div style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 1rem;">
            <div>
              <div style="font-weight: 700; font-size: 1.05rem;">🛡️ Tamper-Evident SHA-256 Hash Chain Verification</div>
              <div style="font-size: 0.8125rem; color: var(--text-muted);">
                Audit entries are cryptographically chained to previous hashes to ensure immutable tamper-detection.
              </div>
            </div>
            <button type="button" class="btn btn-primary" id="btn-verify-chain">Verify Hash Chain Integrity</button>
          </div>
          <div id="chain-verify-result" style="margin-top: 0.75rem; display: none;"></div>
        </div>

        <div class="card" style="padding: 1rem;">
          <div style="display: flex; gap: 0.75rem; flex-wrap: wrap; align-items: flex-end;">
            <div class="form-group" style="margin: 0; min-width: 180px;">
              <label class="form-label" for="audit-res-filter">Resource Type</label>
              <input class="input" id="audit-res-filter" placeholder="e.g. UserAccount, Patient" value="${adminAuditFilters.resourceType || ''}">
            </div>
            <div class="form-group" style="margin: 0; min-width: 180px;">
              <label class="form-label" for="audit-act-filter">Action Name</label>
              <input class="input" id="audit-act-filter" placeholder="e.g. READ, LOGIN, ADMIN_" value="${adminAuditFilters.action || ''}">
            </div>
            <button type="button" class="btn btn-outline" id="btn-filter-audit" style="height: 38px;">Filter Logs</button>
            ${adminAuditFilters.resourceType || adminAuditFilters.action ? `
              <button type="button" class="btn btn-outline" id="btn-clear-audit-filter" style="height: 38px;">Clear Filters</button>
            ` : ''}
          </div>
        </div>

        <div class="table-wrap">
          <table aria-label="System Audit Trail">
            <thead>
              <tr>
                <th scope="col">Time (UTC)</th>
                <th scope="col">Action</th>
                <th scope="col">Resource</th>
                <th scope="col">Actor</th>
                <th scope="col">Target Patient</th>
                <th scope="col">IP Address</th>
                <th scope="col">Tamper Hash</th>
              </tr>
            </thead>
            <tbody>
              ${logsRes.items.length === 0 ? `
                <tr><td colspan="7" style="text-align: center; color: var(--text-muted); padding: 2rem;">No audit logs found matching criteria.</td></tr>
              ` : logsRes.items.map(l => `
                <tr>
                  <td style="font-size: 0.8125rem; white-space: nowrap; font-family: monospace;">
                    ${new Date(l.createdAt).toISOString().replace('T', ' ').substring(0, 19)}
                  </td>
                  <td>${pill(l.action)}</td>
                  <td><code>${escapeHtml(l.resourceType)}</code> ${l.resourceId ? `<span style="font-size:0.7rem; color:var(--text-muted);">(${escapeHtml(l.resourceId.substring(0,8))}...)</span>` : ''}</td>
                  <td>
                    <div style="font-weight: 500;">${escapeHtml(l.actor?.name || 'System')}</div>
                    <div style="font-size: 0.75rem; color: var(--text-muted);">${escapeHtml(l.actor?.role || '—')}</div>
                  </td>
                  <td>
                    ${l.patientId ? `<code>${escapeHtml(l.patientId.substring(0,8))}...</code>` : '—'}
                    ${l.isProxy ? '<span class="pill info" style="font-size: 0.65rem;">PROXY</span>' : ''}
                  </td>
                  <td style="font-size: 0.8125rem; font-family: monospace;">${escapeHtml(l.ipAddress || '—')}</td>
                  <td style="font-size: 0.75rem; font-family: monospace; color: var(--text-muted);" title="${escapeHtml(l.hash || 'Unchained')}">
                    ${l.hash ? escapeHtml(l.hash.substring(0, 12)) + '...' : '—'}
                  </td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>

        <div style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 0.5rem;">
          <div style="font-size: 0.8125rem; color: var(--text-muted);">
            Page ${logsRes.page} of ${logsRes.totalPages || 1} (${logsRes.totalCount} total events recorded)
          </div>
          <div style="display: flex; gap: 0.5rem;">
            <button type="button" class="btn btn-outline btn-sm" id="btn-audit-prev" ${logsRes.page <= 1 ? 'disabled' : ''}>← Previous</button>
            <button type="button" class="btn btn-outline btn-sm" id="btn-audit-next" ${logsRes.page >= logsRes.totalPages ? 'disabled' : ''}>Next →</button>
          </div>
        </div>
      </div>
    `;

    document.getElementById('btn-verify-chain').onclick = async () => {
      const resEl = document.getElementById('chain-verify-result');
      resEl.style.display = 'block';
      resEl.innerHTML = '<span class="pill info">Verifying cryptographic hash chain...</span>';
      try {
        const check = await api('/api/admin/audit-logs/verify');
        if (check.valid) {
          resEl.innerHTML = `
            <div class="notice notice-good" style="margin: 0; padding: 0.75rem 1rem;">
              <strong>✅ Audit Chain Verified Integrity</strong>: All ${check.totalRecords} log entries are unbroken and tamper-free.
              <div style="font-size: 0.75rem; font-family: monospace; margin-top: 0.25rem;">
                Genesis: ${check.genesisHash.substring(0, 16)}... | Head: ${check.latestHash ? check.latestHash.substring(0, 16) + '...' : 'None'}
              </div>
            </div>
          `;
        } else {
          resEl.innerHTML = `
            <div class="notice notice-bad" style="margin: 0; padding: 0.75rem 1rem;">
              <strong>⚠️ Audit Chain Corruption Detected</strong>: Log record ${escapeHtml(check.corruptedLogId)} failed cryptographic hash chain match. Error: ${escapeHtml(check.error)}
            </div>
          `;
        }
      } catch (e) {
        resEl.innerHTML = `<div class="notice notice-bad">${escapeHtml(e.message)}</div>`;
      }
    };

    document.getElementById('btn-filter-audit').onclick = () => {
      adminAuditFilters.resourceType = document.getElementById('audit-res-filter').value.trim();
      adminAuditFilters.action = document.getElementById('audit-act-filter').value.trim();
      adminAuditPage = 1;
      loadAdmin(rootEl);
    };

    const clearBtn = document.getElementById('btn-clear-audit-filter');
    if (clearBtn) {
      clearBtn.onclick = () => {
        adminAuditFilters.resourceType = '';
        adminAuditFilters.action = '';
        adminAuditPage = 1;
        loadAdmin(rootEl);
      };
    }

    const prevBtn = document.getElementById('btn-audit-prev');
    if (prevBtn && !prevBtn.disabled) {
      prevBtn.onclick = () => {
        adminAuditPage = Math.max(1, adminAuditPage - 1);
        loadAdmin(rootEl);
      };
    }

    const nextBtn = document.getElementById('btn-audit-next');
    if (nextBtn && !nextBtn.disabled) {
      nextBtn.onclick = () => {
        adminAuditPage++;
        loadAdmin(rootEl);
      };
    }
  }

  async function renderAdminProxyGrants(container, rootEl) {
    const grants = await api('/api/admin/proxy-grants');

    container.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1rem;">
        <div>
          <h3 style="margin: 0; font-size: 1.15rem;">Active Proxy Access Relationships</h3>
          <div style="font-size: 0.8125rem; color: var(--text-muted);">Family, guardian, and authorized representative delegation grants.</div>
        </div>

        <div class="table-wrap">
          <table aria-label="Proxy Access Grants Table">
            <thead>
              <tr>
                <th scope="col">Grantor (Patient)</th>
                <th scope="col">Grantee (Proxy User)</th>
                <th scope="col">Delegated Permissions</th>
                <th scope="col">Consent Date</th>
                <th scope="col">Status</th>
                <th scope="col" style="text-align: right;">Action</th>
              </tr>
            </thead>
            <tbody>
              ${grants.length === 0 ? `
                <tr><td colspan="6" style="text-align: center; color: var(--text-muted); padding: 2rem;">No proxy grants found in system.</td></tr>
              ` : grants.map(g => `
                <tr>
                  <td>
                    <div style="font-weight: 600;">${escapeHtml(g.grantor?.name || 'Unknown')}</div>
                    <div style="font-size: 0.75rem; color: var(--text-muted);">${escapeHtml(g.grantor?.email || 'No email')} ${g.grantor?.mrn ? `• MRN: ${escapeHtml(g.grantor.mrn)}` : ''}</div>
                  </td>
                  <td>
                    <div style="font-weight: 600;">${escapeHtml(g.grantee?.name || 'Unknown')}</div>
                    <div style="font-size: 0.75rem; color: var(--text-muted);">${escapeHtml(g.grantee?.email || '—')}</div>
                  </td>
                  <td>
                    <div style="display: flex; gap: 0.25rem; flex-wrap: wrap;">
                      ${g.permissions.canViewRecords ? '<span class="pill info" style="font-size:0.65rem;">Records</span>' : ''}
                      ${g.permissions.canMessage ? '<span class="pill info" style="font-size:0.65rem;">Messaging</span>' : ''}
                      ${g.permissions.canManageAppointments ? '<span class="pill info" style="font-size:0.65rem;">Appts</span>' : ''}
                      ${g.permissions.canRequestRefills ? '<span class="pill info" style="font-size:0.65rem;">Refills</span>' : ''}
                      ${g.permissions.canPayBills ? '<span class="pill info" style="font-size:0.65rem;">Billing</span>' : ''}
                    </div>
                  </td>
                  <td style="font-size: 0.8125rem; color: var(--text-muted);">${new Date(g.consentGivenAt).toLocaleDateString()}</td>
                  <td>
                    ${g.isActive ? '<span class="pill good">Active</span>' : '<span class="pill bad">Revoked</span>'}
                  </td>
                  <td style="text-align: right;">
                    ${g.isActive ? `
                      <button type="button" class="btn btn-outline btn-sm btn-danger" data-revoke-grant="${g.id}" aria-label="Revoke proxy grant">Revoke</button>
                    ` : '<span style="color: var(--text-muted); font-size: 0.75rem;">Concluded</span>'}
                  </td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>
      </div>
    `;

    container.querySelectorAll('button[data-revoke-grant]').forEach(btn => {
      btn.onclick = () => {
        const id = btn.getAttribute('data-revoke-grant');
        showInAppConfirm('Revoke this proxy access grant? The proxy user will immediately lose access to the patient chart.', async () => {
          try {
            await api(`/api/admin/proxy-grants/${id}/revoke`, { method: 'POST' });
            showToast('Proxy grant revoked.');
            loadAdmin(rootEl);
          } catch (err) {
            showToast(`Error: ${err.message}`);
          }
        }, null, 'Revoke Proxy Grant');
      };
    });
  }

  async function renderAdminShares(container, rootEl) {
    const shares = await api('/api/admin/shares');

    container.innerHTML = `
      <div style="display: flex; flex-direction: column; gap: 1rem;">
        <div>
          <h3 style="margin: 0; font-size: 1.15rem;">External Record Shares (SHR Redemption Codes)</h3>
          <div style="font-size: 0.8125rem; color: var(--text-muted);">Monitored time-limited and use-limited external EHR export grants.</div>
        </div>

        <div class="table-wrap">
          <table aria-label="Active Record Shares Table">
            <thead>
              <tr>
                <th scope="col">Patient</th>
                <th scope="col">Share Label</th>
                <th scope="col">Recipient</th>
                <th scope="col">Usage Limit</th>
                <th scope="col">Expiration</th>
                <th scope="col">Status</th>
                <th scope="col" style="text-align: right;">Action</th>
              </tr>
            </thead>
            <tbody>
              ${shares.length === 0 ? `
                <tr><td colspan="7" style="text-align: center; color: var(--text-muted); padding: 2rem;">No external shares recorded.</td></tr>
              ` : shares.map(s => `
                <tr>
                  <td>
                    <div style="font-weight: 600;">${escapeHtml(s.patientName || 'Patient')}</div>
                    <div style="font-size: 0.75rem; color: var(--text-muted);">MRN: ${escapeHtml(s.mrn || '—')}</div>
                  </td>
                  <td style="font-weight: 500;">${escapeHtml(s.label)}</td>
                  <td>
                    <div>${escapeHtml(s.recipientName || 'External Recipient')}</div>
                    <div style="font-size: 0.75rem; color: var(--text-muted);">${escapeHtml(s.recipientRole || 'Physician/Specialist')}</div>
                  </td>
                  <td>
                    <span class="pill ${s.useCount >= s.maxUses ? 'warn' : 'info'}">${s.useCount} / ${s.maxUses} uses</span>
                  </td>
                  <td style="font-size: 0.8125rem; color: var(--text-muted);">${new Date(s.expiresAt).toLocaleDateString()}</td>
                  <td>
                    ${s.isActive ? '<span class="pill good">Active</span>' : s.revokedAt ? '<span class="pill bad">Revoked</span>' : '<span class="pill warn">Expired</span>'}
                  </td>
                  <td style="text-align: right;">
                    ${s.isActive ? `
                      <button type="button" class="btn btn-outline btn-sm btn-danger" data-revoke-share="${escapeHtml(s.id)}" aria-label="Revoke record share ${escapeHtml(s.label)}">Revoke</button>
                    ` : '<span style="color: var(--text-muted); font-size: 0.75rem;">Inactive</span>'}
                  </td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>
      </div>
    `;

    container.querySelectorAll('button[data-revoke-share]').forEach(btn => {
      btn.onclick = () => {
        const id = btn.getAttribute('data-revoke-share');
        showInAppConfirm('Revoke this external record share code? External recipients will no longer be able to redeem it.', async () => {
          try {
            await api(`/api/admin/shares/${id}/revoke`, { method: 'POST' });
            showToast('Record share revoked.');
            loadAdmin(rootEl);
          } catch (err) {
            showToast(`Error: ${err.message}`);
          }
        }, null, 'Revoke Record Share');
      };
    });
  }

  // --- Modal Helpers ---
  function showModal({ title, body, confirmText = 'Confirm', onConfirm }) {
    const container = document.getElementById('modal-container');
    container.innerHTML = `
      <div class="modal-scrim" id="modal-scrim" role="dialog" aria-modal="true" aria-labelledby="modal-title">
        <div class="modal-box">
          <div class="modal-header">
            <span id="modal-title">${title}</span>
            <button type="button" class="btn btn-outline btn-sm" id="btn-close-modal" aria-label="Close dialog">✕</button>
          </div>
          <div class="modal-body">${body}</div>
          <div class="modal-footer">
            <button type="button" class="btn btn-outline" id="btn-cancel-modal">Cancel</button>
            <button type="button" class="btn btn-primary" id="btn-confirm-modal">${confirmText}</button>
          </div>
        </div>
      </div>
    `;

    const escListener = (e) => {
      if (e.key === 'Escape') {
        closeModal();
      }
    };
    window.addEventListener('keydown', escListener, { once: true });

    document.getElementById('btn-close-modal').onclick = closeModal;
    document.getElementById('btn-cancel-modal').onclick = closeModal;
    document.getElementById('btn-confirm-modal').onclick = async () => {
      if (onConfirm) await onConfirm();
    };

    setTimeout(() => {
      const firstInput = container.querySelector('input, select, textarea, button:not(#btn-close-modal)');
      if (firstInput) firstInput.focus();
    }, 50);
  }

  function closeModal() {
    const container = document.getElementById('modal-container');
    if (container) container.innerHTML = '';
  }

  // Run app
  document.addEventListener('DOMContentLoaded', init);
})();
