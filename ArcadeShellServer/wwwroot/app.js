// ArcadeShell Remote — Mobile Web UI
(function () {
  'use strict';

  const $ = (sel) => document.querySelector(sel);
  const $$ = (sel) => document.querySelectorAll(sel);

  let config = null;

  // ── Auth ────────────────────────────────────────────────────────────────
  const loginScreen = $('#loginScreen');
  const mainScreen = $('#mainScreen');
  const pinInput = $('#pinInput');
  const btnLogin = $('#btnLogin');
  const loginError = $('#loginError');

  btnLogin.addEventListener('click', doLogin);
  pinInput.addEventListener('keydown', (e) => { if (e.key === 'Enter') doLogin(); });

  async function doLogin() {
    loginError.classList.add('hidden');
    const pin = pinInput.value.trim();
    if (!pin) return;

    try {
      const res = await fetch('/api/auth', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ pin })
      });
      if (!res.ok) {
        loginError.textContent = 'PIN incorrecto';
        loginError.classList.remove('hidden');
        pinInput.value = '';
        pinInput.focus();
        return;
      }
      loginScreen.classList.remove('active');
      mainScreen.classList.add('active');
      loadStatus();
      loadConfig();
    } catch {
      loginError.textContent = 'Error de conexión';
      loginError.classList.remove('hidden');
    }
  }

  // ── Tabs ────────────────────────────────────────────────────────────────
  $$('.tab').forEach((tab) => {
    tab.addEventListener('click', () => {
      $$('.tab').forEach((t) => t.classList.remove('active'));
      $$('.tab-content').forEach((c) => c.classList.remove('active'));
      tab.classList.add('active');
      $(`#tab-${tab.dataset.tab}`).classList.add('active');

      if (tab.dataset.tab === 'status') loadStatus();
    });
  });

  // ── Status ──────────────────────────────────────────────────────────────
  $('#btnRefreshStatus').addEventListener('click', loadStatus);

  async function loadStatus() {
    try {
      const res = await fetch('/api/status');
      if (res.status === 401) return showLogin();
      const data = await res.json();
      $('#statHostname').textContent = data.hostname;
      $('#statUptime').textContent = formatUptime(data.uptime);
      $('#statServer').textContent = data.serverIp + ':' + data.serverPort;
      $('#statInput').textContent = data.inputMethod;
      $('#statMusic').textContent = data.musicEnabled ? 'Activada' : 'Desactivada';
    } catch {
      $('#statHostname').textContent = 'Error';
    }
  }

  function formatUptime(secs) {
    const h = Math.floor(secs / 3600);
    const m = Math.floor((secs % 3600) / 60);
    return h > 0 ? `${h}h ${m}m` : `${m}m`;
  }

  // ── Config Load ─────────────────────────────────────────────────────────
  async function loadConfig() {
    try {
      const res = await fetch('/api/config');
      if (res.status === 401) return showLogin();
      config = await res.json();
      populateSettings();
      populateOptions();
    } catch {
      toast('Error cargando configuración', true);
    }
  }

  function populateSettings() {
    if (!config) return;
    $('#cfgTitle').value = config.ui?.title || '';
    $('#cfgTopMost').checked = config.ui?.topMost ?? true;
    $('#cfgBootSplash').checked = config.arranque?.bootSplashEnabled ?? true;
    $('#cfgMusicEnabled').checked = config.music?.enabled ?? false;
    $('#cfgVolume').value = config.music?.volume ?? 100;
    $('#cfgVolumeVal').textContent = config.music?.volume ?? 100;
    $('#cfgThumbVol').value = config.music?.thumbVideoVolume ?? 0;
    $('#cfgThumbVolVal').textContent = config.music?.thumbVideoVolume ?? 0;
    $('#cfgPreset').value = config.theme?.preset || 'neon-green';
    $('#cfgRemotePort').value = config.remoteAccess?.port ?? 8484;
    $('#cfgRemotePin').value = config.remoteAccess?.pin || '';
  }

  // Volume slider live display
  $('#cfgVolume').addEventListener('input', (e) => { $('#cfgVolumeVal').textContent = e.target.value; });
  $('#cfgThumbVol').addEventListener('input', (e) => { $('#cfgThumbVolVal').textContent = e.target.value; });

  // ── Options ─────────────────────────────────────────────────────────────
  function populateOptions() {
    const list = $('#optionsList');
    list.innerHTML = '';
    if (!config?.options) return;

    config.options.forEach((opt, i) => {
      list.appendChild(createOptionItem(opt, i));
    });
  }

  function createOptionItem(opt, index) {
    const div = document.createElement('div');
    div.className = 'option-item';
    div.innerHTML = `
      <div class="option-header">
        <span class="option-num">Opción ${index + 1}</span>
        <button class="btn-remove" data-idx="${index}">Eliminar</button>
      </div>
      <input type="text" placeholder="Nombre" value="${esc(opt.label || '')}" data-field="label">
      <input type="text" placeholder="Ruta del ejecutable" value="${esc(opt.exe || '')}" data-field="exe">
      <input type="text" placeholder="Imagen (opcional)" value="${esc(opt.image || '')}" data-field="image">
    `;
    div.querySelector('.btn-remove').addEventListener('click', () => {
      config.options.splice(index, 1);
      populateOptions();
    });
    return div;
  }

  function esc(s) { return s.replace(/"/g, '&quot;').replace(/</g, '&lt;'); }

  $('#btnAddOption').addEventListener('click', () => {
    if (!config) return;
    if (!config.options) config.options = [];
    config.options.push({ label: '', exe: '', image: '', thumbVideo: null, waitForProcessName: null });
    populateOptions();
    // Scroll to bottom
    const list = $('#optionsList');
    list.lastElementChild?.scrollIntoView({ behavior: 'smooth' });
  });

  // ── Save Options ────────────────────────────────────────────────────────
  $('#btnSaveOptions').addEventListener('click', async () => {
    collectOptionsFromUI();
    await saveConfig();
  });

  function collectOptionsFromUI() {
    if (!config) return;
    const items = $$('.option-item');
    const original = config.options || [];
    config.options = [];
    items.forEach((item, i) => {
      const inputs = item.querySelectorAll('input[data-field]');
      const base = original[i] || {};
      const opt = { ...base };
      inputs.forEach((inp) => { opt[inp.dataset.field] = inp.value; });
      config.options.push(opt);
    });
  }

  // ── Save Settings ───────────────────────────────────────────────────────
  $('#btnSaveSettings').addEventListener('click', async () => {
    collectSettingsFromUI();
    await saveConfig();
  });

  function collectSettingsFromUI() {
    if (!config) return;
    config.ui = config.ui || {};
    config.ui.title = $('#cfgTitle').value;
    config.ui.topMost = $('#cfgTopMost').checked;

    config.arranque = config.arranque || {};
    config.arranque.bootSplashEnabled = $('#cfgBootSplash').checked;

    config.music = config.music || {};
    config.music.enabled = $('#cfgMusicEnabled').checked;
    config.music.volume = parseInt($('#cfgVolume').value, 10);
    config.music.thumbVideoVolume = parseInt($('#cfgThumbVol').value, 10);

    config.theme = config.theme || {};
    config.theme.preset = $('#cfgPreset').value;

    config.remoteAccess = config.remoteAccess || {};
    config.remoteAccess.port = parseInt($('#cfgRemotePort').value, 10);
    config.remoteAccess.pin = $('#cfgRemotePin').value;
  }

  async function saveConfig() {
    try {
      const res = await fetch('/api/config', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(config)
      });
      if (res.status === 401) return showLogin();
      if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        toast(err.error || 'Error guardando', true);
        return;
      }
      toast('Guardado ✓');
    } catch {
      toast('Error de conexión', true);
    }
  }

  // ── Helpers ─────────────────────────────────────────────────────────────
  function showLogin() {
    mainScreen.classList.remove('active');
    loginScreen.classList.add('active');
    pinInput.value = '';
    pinInput.focus();
  }

  function toast(msg, isError) {
    const el = $('#toast');
    el.textContent = msg;
    el.style.background = isError ? 'var(--danger)' : 'var(--accent)';
    el.classList.remove('hidden');
    setTimeout(() => el.classList.add('hidden'), 2500);
  }

  // Auto-focus PIN input
  pinInput.focus();
})();
