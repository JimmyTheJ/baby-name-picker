const api = {
  async get(path) {
    const response = await fetch(path);
    if (!response.ok) throw new Error(`Request failed: ${response.status}`);
    return response.json();
  }
};

const state = {
  activeTab: 'search',
  years: []
};

const els = {
  tabSearch: document.getElementById('tab-search'),
  tabYear: document.getElementById('tab-year'),
  panelSearch: document.getElementById('panel-search'),
  panelYear: document.getElementById('panel-year'),
  searchInput: document.getElementById('search-input'),
  searchBtn: document.getElementById('search-btn'),
  randomBtn: document.getElementById('random-btn'),
  searchResults: document.getElementById('search-results'),
  yearSelect: document.getElementById('year-select'),
  yearGender: document.getElementById('year-gender'),
  yearLoadBtn: document.getElementById('year-load-btn'),
  yearResults: document.getElementById('year-results'),
  detailPanel: document.getElementById('detail-panel'),
  detailBackdrop: document.getElementById('detail-backdrop'),
  detailClose: document.getElementById('detail-close'),
  detailName: document.getElementById('detail-name'),
  detailGender: document.getElementById('detail-gender'),
  detailSlant: document.getElementById('detail-slant'),
  detailNicknames: document.getElementById('detail-nicknames'),
  detailYears: document.getElementById('detail-years')
};

function getSelectedGender() {
  const selected = document.querySelector('input[name="gender"]:checked');
  return selected ? selected.value : 'Any';
}

function genderLabel(gender) {
  switch (gender) {
    case 'Male': return 'Boy';
    case 'Female': return 'Girl';
    case 'Unisex': return 'Unisex';
    default: return gender;
  }
}

function slantLabel(maleShare) {
  if (maleShare >= 0.85) return 'Mostly boy';
  if (maleShare <= 0.15) return 'Mostly girl';
  if (maleShare >= 0.4 && maleShare <= 0.6) return 'Balanced';
  return maleShare > 0.5 ? 'Leans boy' : 'Leans girl';
}

function renderSlant(maleShare) {
  const percent = Math.round(maleShare * 100);
  return `
    <div>
      <div class="mb-1 flex justify-between text-xs font-medium text-slate-600">
        <span>Girl</span>
        <span>${slantLabel(maleShare)} (${percent}% male share)</span>
        <span>Boy</span>
      </div>
      <div class="slant-bar">
        <div class="slant-marker" style="left: ${percent}%"></div>
      </div>
    </div>`;
}

function renderNameCard(item, onClick) {
  const card = document.createElement('article');
  card.className = 'name-card';
  card.innerHTML = `
    <div class="flex items-start justify-between gap-3">
      <div>
        <h3 class="text-lg font-bold text-slate-900">${escapeHtml(item.name)}</h3>
        <p class="text-sm text-slate-600">${genderLabel(item.gender)}</p>
      </div>
      ${item.nicknames?.length ? `<p class="text-xs text-slate-500">${escapeHtml(item.nicknames.slice(0, 3).join(', '))}</p>` : ''}
    </div>`;
  card.addEventListener('click', () => onClick(item.id));
  return card;
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

async function runSearch() {
  const q = els.searchInput.value.trim();
  const gender = getSelectedGender();
  const params = new URLSearchParams();
  if (q) params.set('q', q);
  if (gender !== 'Any') params.set('gender', gender);

  const results = await api.get(`/api/names?${params.toString()}`);
  els.searchResults.replaceChildren();

  if (!results.length) {
    els.searchResults.innerHTML = '<p class="rounded-xl bg-white p-4 text-center text-slate-600 ring-1 ring-slate-200">No names found. Try another search.</p>';
    return;
  }

  results.forEach(item => els.searchResults.appendChild(renderNameCard(item, showDetail)));
}

async function runRandom() {
  const gender = getSelectedGender();
  const params = new URLSearchParams();
  if (gender !== 'Any') params.set('gender', gender);
  const result = await api.get(`/api/names/random?${params.toString()}`);
  if (result) await showDetail(result.id);
}

async function loadYears() {
  state.years = await api.get('/api/years');
  els.yearSelect.replaceChildren();
  state.years.forEach(year => {
    const option = document.createElement('option');
    option.value = year;
    option.textContent = year;
    els.yearSelect.appendChild(option);
  });
}

async function loadPopularity() {
  const year = els.yearSelect.value;
  const gender = els.yearGender.value;
  const results = await api.get(`/api/popularity?year=${year}&gender=${gender}`);
  els.yearResults.replaceChildren();

  results.forEach(item => {
    const row = document.createElement('button');
    row.type = 'button';
    row.className = 'name-card w-full text-left';
    row.innerHTML = `
      <div class="flex items-center justify-between gap-3">
        <div class="flex items-center gap-3">
          <span class="inline-flex h-8 w-8 items-center justify-center rounded-full bg-slate-100 text-sm font-bold text-slate-700">#${item.rank}</span>
          <div>
            <p class="font-semibold text-slate-900">${escapeHtml(item.name)}</p>
            <p class="text-xs text-slate-500">${genderLabel(item.gender)} · ${item.count.toLocaleString()} births</p>
          </div>
        </div>
      </div>`;
    row.addEventListener('click', async () => {
      const matches = await api.get(`/api/names?q=${encodeURIComponent(item.name)}`);
      const exact = matches.find(m => m.name.toLowerCase() === item.name.toLowerCase());
      if (exact) await showDetail(exact.id);
    });
    els.yearResults.appendChild(row);
  });
}

async function showDetail(id) {
  const detail = await api.get(`/api/names/${id}`);
  els.detailName.textContent = detail.name;
  els.detailGender.textContent = genderLabel(detail.gender);
  els.detailSlant.innerHTML = renderSlant(detail.maleShare);

  if (detail.nicknames?.length) {
    els.detailNicknames.innerHTML = `
      <h3 class="text-sm font-semibold uppercase tracking-wide text-slate-500">Nicknames</h3>
      <p class="mt-1 text-slate-700">${escapeHtml(detail.nicknames.join(', '))}</p>`;
  } else {
    els.detailNicknames.innerHTML = '';
  }

  els.detailYears.replaceChildren();
  detail.yearStats.forEach(stat => {
    const row = document.createElement('div');
    row.className = 'flex justify-between rounded-lg bg-slate-50 px-3 py-2 text-sm';
    row.innerHTML = `
      <span class="font-medium text-slate-800">${stat.year}</span>
      <span class="text-slate-600">#${stat.rank} ${genderLabel(stat.sex)} · ${stat.count.toLocaleString()}</span>`;
    els.detailYears.appendChild(row);
  });

  els.detailPanel.classList.remove('hidden');
  els.detailBackdrop.classList.remove('hidden');
}

function hideDetail() {
  els.detailPanel.classList.add('hidden');
  els.detailBackdrop.classList.add('hidden');
}

function setTab(tab) {
  state.activeTab = tab;
  const isSearch = tab === 'search';
  els.tabSearch.setAttribute('aria-selected', String(isSearch));
  els.tabYear.setAttribute('aria-selected', String(!isSearch));
  els.panelSearch.classList.toggle('hidden', !isSearch);
  els.panelYear.classList.toggle('hidden', isSearch);
}

els.tabSearch.addEventListener('click', () => setTab('search'));
els.tabYear.addEventListener('click', () => setTab('year'));
els.searchBtn.addEventListener('click', () => runSearch().catch(showError));
els.randomBtn.addEventListener('click', () => runRandom().catch(showError));
els.yearLoadBtn.addEventListener('click', () => loadPopularity().catch(showError));
els.searchInput.addEventListener('keydown', event => {
  if (event.key === 'Enter') runSearch().catch(showError);
});
els.detailClose.addEventListener('click', hideDetail);
els.detailBackdrop.addEventListener('click', hideDetail);

function showError(error) {
  console.error(error);
  alert('Something went wrong. Please try again.');
}

loadYears().catch(showError);
runSearch().catch(showError);
