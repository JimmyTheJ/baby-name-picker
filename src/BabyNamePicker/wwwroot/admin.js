const api = {
  async request(path, options = {}) {
    const response = await fetch(path, {
      credentials: 'same-origin',
      headers: {
        'Content-Type': 'application/json',
        ...(options.headers || {})
      },
      ...options
    });

    let body = null;
    const text = await response.text();
    if (text) {
      try {
        body = JSON.parse(text);
      } catch {
        body = { raw: text };
      }
    }

    if (!response.ok) {
      const message = body?.error || `Request failed: ${response.status}`;
      const error = new Error(message);
      error.status = response.status;
      error.body = body;
      throw error;
    }

    return body;
  },

  get(path) {
    return this.request(path);
  },

  post(path, data) {
    return this.request(path, { method: 'POST', body: JSON.stringify(data ?? {}) });
  },

  del(path) {
    return this.request(path, { method: 'DELETE' });
  }
};

const els = {
  disabledPanel: document.getElementById('disabled-panel'),
  loginPanel: document.getElementById('login-panel'),
  consolePanel: document.getElementById('console-panel'),
  sessionBar: document.getElementById('session-bar'),
  sessionUser: document.getElementById('session-user'),
  loginForm: document.getElementById('login-form'),
  loginUsername: document.getElementById('login-username'),
  loginPassword: document.getElementById('login-password'),
  loginError: document.getElementById('login-error'),
  logoutBtn: document.getElementById('logout-btn'),
  enrichBatch: document.getElementById('enrich-batch'),
  enrichBatchSuggestions: document.getElementById('enrich-batch-suggestions'),
  enrichProvider: document.getElementById('enrich-provider'),
  enrichForce: document.getElementById('enrich-force'),
  enrichStatus: document.getElementById('enrich-status'),
  enrichRun: document.getElementById('enrich-run'),
  genderBatch: document.getElementById('gender-batch'),
  genderBatchSuggestions: document.getElementById('gender-batch-suggestions'),
  genderFilter: document.getElementById('gender-filter'),
  genderFilterSuggestions: document.getElementById('gender-filter-suggestions'),
  genderProvider: document.getElementById('gender-provider'),
  genderForce: document.getElementById('gender-force'),
  genderRun: document.getElementById('gender-run'),
  consoleOutput: document.getElementById('console-output'),
  consoleClear: document.getElementById('console-clear')
};

let eventSource = null;
let defaults = null;

function show(el) {
  el.classList.remove('hidden');
  if (el === els.sessionBar) {
    el.classList.add('flex');
  }
}

function hide(el) {
  el.classList.add('hidden');
  if (el === els.sessionBar) {
    el.classList.remove('flex');
  }
}

function appendConsoleLine(entry) {
  const time = new Date(entry.utc).toISOString().replace('T', ' ').replace(/\.\d+Z$/, 'Z');
  const level = (entry.level || 'Info').padEnd(7);
  const source = (entry.source || 'App').padEnd(10);
  const line = `[${time}] ${level} ${source} ${entry.message}\n`;
  els.consoleOutput.textContent += line;
  els.consoleOutput.scrollTop = els.consoleOutput.scrollHeight;
}

function stopLogStream() {
  if (eventSource) {
    eventSource.close();
    eventSource = null;
  }
}

function startLogStream() {
  stopLogStream();
  els.consoleOutput.textContent = '';
  eventSource = new EventSource('/api/admin/logs/stream');
  eventSource.onmessage = (event) => {
    try {
      appendConsoleLine(JSON.parse(event.data));
    } catch {
      // ignore malformed
    }
  };
  eventSource.onerror = () => {
    // Browser will retry; surface a one-line note if disconnected mid-session.
  };
}

function suggestionChip(label, onClick) {
  const btn = document.createElement('button');
  btn.type = 'button';
  btn.className = 'rounded-full border border-slate-200 bg-white px-3 py-1 text-xs font-semibold text-slate-600 hover:border-blush-500 hover:text-blush-700';
  btn.textContent = label;
  btn.addEventListener('click', onClick);
  return btn;
}

function fillProviderSelect(select, providers, configured) {
  select.innerHTML = '';
  const blank = document.createElement('option');
  blank.value = '';
  blank.textContent = configured ? `Configured default (${configured})` : 'Configured default';
  select.appendChild(blank);
  for (const p of providers || []) {
    const opt = document.createElement('option');
    opt.value = p;
    opt.textContent = p;
    select.appendChild(opt);
  }
}

function applyDefaults(data) {
  defaults = data;
  const enrich = data.enrichNames;
  const gender = data.reclassifyGender;

  els.enrichBatch.value = enrich.batchSize;
  els.enrichForce.checked = !!enrich.force;
  fillProviderSelect(els.enrichProvider, enrich.suggestions.provider, enrich.suggestions.configuredProvider);
  els.enrichBatchSuggestions.replaceChildren(
    ...(enrich.suggestions.batchSize || []).map((n) =>
      suggestionChip(String(n), () => { els.enrichBatch.value = n; }))
  );

  els.genderBatch.value = gender.batchSize;
  els.genderForce.checked = !!gender.force;
  fillProviderSelect(els.genderProvider, gender.suggestions.provider, gender.suggestions.configuredProvider);

  els.genderFilter.innerHTML = '';
  for (const f of gender.suggestions.filter || []) {
    const opt = document.createElement('option');
    opt.value = f;
    opt.textContent = f;
    if (f === gender.filter) opt.selected = true;
    els.genderFilter.appendChild(opt);
  }

  els.genderBatchSuggestions.replaceChildren(
    ...(gender.suggestions.batchSize || []).map((n) =>
      suggestionChip(String(n), () => { els.genderBatch.value = n; }))
  );
  els.genderFilterSuggestions.replaceChildren(
    ...(gender.suggestions.filter || []).map((f) =>
      suggestionChip(f, () => { els.genderFilter.value = f; }))
  );
}

async function refreshEnrichmentStatus() {
  try {
    const status = await api.get('/api/admin/enrichment-status');
    els.enrichStatus.textContent =
      `${status.enrichedNames}/${status.totalNames} enriched · ${status.pendingNames} pending`;
  } catch {
    els.enrichStatus.textContent = '';
  }
}

async function showAuthenticated(username) {
  hide(els.disabledPanel);
  hide(els.loginPanel);
  show(els.consolePanel);
  show(els.sessionBar);
  els.sessionUser.textContent = username ? `Signed in as ${username}` : 'Signed in';

  const data = await api.get('/api/admin/pipeline-defaults');
  applyDefaults(data);
  await refreshEnrichmentStatus();
  startLogStream();
}

function showLogin() {
  stopLogStream();
  hide(els.disabledPanel);
  hide(els.consolePanel);
  hide(els.sessionBar);
  show(els.loginPanel);
  els.loginError.classList.add('hidden');
}

function showDisabled() {
  stopLogStream();
  hide(els.loginPanel);
  hide(els.consolePanel);
  hide(els.sessionBar);
  show(els.disabledPanel);
}

async function bootstrap() {
  const status = await api.get('/api/admin/status');
  if (!status.enabled) {
    showDisabled();
    return;
  }

  if (status.authenticated) {
    await showAuthenticated();
    return;
  }

  showLogin();
}

els.loginForm.addEventListener('submit', async (event) => {
  event.preventDefault();
  els.loginError.classList.add('hidden');
  try {
    const result = await api.post('/api/admin/login', {
      username: els.loginUsername.value,
      password: els.loginPassword.value
    });
    els.loginPassword.value = '';
    await showAuthenticated(result.username);
  } catch (err) {
    els.loginError.textContent = err.status === 429
      ? 'Too many login attempts. Try again later.'
      : (err.message || 'Login failed.');
    els.loginError.classList.remove('hidden');
  }
});

els.logoutBtn.addEventListener('click', async () => {
  try {
    await api.post('/api/admin/logout');
  } catch {
    // still return to login UI
  }
  showLogin();
});

els.consoleClear.addEventListener('click', async () => {
  try {
    await api.del('/api/admin/logs');
  } catch {
    // ignore
  }
  els.consoleOutput.textContent = '';
});

els.enrichRun.addEventListener('click', async () => {
  els.enrichRun.disabled = true;
  try {
    const provider = els.enrichProvider.value || null;
    const result = await api.post('/api/admin/enrich-names', {
      batchSize: Number(els.enrichBatch.value) || 25,
      force: els.enrichForce.checked,
      provider
    });
    appendConsoleLine({
      utc: new Date().toISOString(),
      level: 'Info',
      source: 'UI',
      message: `Enrichment finished: ${result.enriched}/${result.processed} ok, ${result.failed} failed.`
    });
    await refreshEnrichmentStatus();
  } catch (err) {
    appendConsoleLine({
      utc: new Date().toISOString(),
      level: 'Error',
      source: 'UI',
      message: err.message
    });
  } finally {
    els.enrichRun.disabled = false;
  }
});

els.genderRun.addEventListener('click', async () => {
  els.genderRun.disabled = true;
  try {
    const provider = els.genderProvider.value || null;
    const result = await api.post('/api/admin/reclassify-gender', {
      batchSize: Number(els.genderBatch.value) || 25,
      force: els.genderForce.checked,
      filter: els.genderFilter.value || 'UnisexOnly',
      provider
    });
    appendConsoleLine({
      utc: new Date().toISOString(),
      level: 'Info',
      source: 'UI',
      message: `Gender recalculation finished: ${result.updated}/${result.processed} updated, ${result.failed} failed.`
    });
  } catch (err) {
    appendConsoleLine({
      utc: new Date().toISOString(),
      level: 'Error',
      source: 'UI',
      message: err.message
    });
  } finally {
    els.genderRun.disabled = false;
  }
});

bootstrap().catch((err) => {
  showLogin();
  els.loginError.textContent = err.message || 'Unable to load admin status.';
  els.loginError.classList.remove('hidden');
});
