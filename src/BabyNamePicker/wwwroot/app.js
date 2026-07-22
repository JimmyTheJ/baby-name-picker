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
  randomBtn: document.getElementById('random-btn'),
  searchResults: document.getElementById('search-results'),
  yearSelect: document.getElementById('year-select'),
  yearGender: document.getElementById('year-gender'),
  yearLimit: document.getElementById('year-limit'),
  yearResults: document.getElementById('year-results')
};

function getSelectedGender() {
  const selected = document.querySelector('input[name="gender"]:checked');
  return selected ? selected.value : 'Any';
}

function getSelectedRarity() {
  const selected = document.querySelector('input[name="rarity"]:checked');
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

function rarityLabel(peakRank) {
  if (!peakRank) return '';
  if (peakRank <= 100) return 'Top 100';
  if (peakRank <= 500) return 'Top 500';
  return 'Uncommon';
}

function rarityBadgeClass(peakRank) {
  if (!peakRank) return 'bg-slate-100 text-slate-600';
  if (peakRank <= 100) return 'bg-blush-100 text-blush-700';
  if (peakRank <= 500) return 'bg-sky-100 text-sky-700';
  return 'bg-slate-100 text-slate-600';
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
  const badge = item.peakRank
    ? `<span class="rounded-full px-2 py-0.5 text-xs font-semibold ${rarityBadgeClass(item.peakRank)}">${rarityLabel(item.peakRank)}</span>`
    : '';
  card.innerHTML = `
    <div class="flex items-start justify-between gap-3">
      <div>
        <div class="flex items-center gap-2">
          <h3 class="text-lg font-bold text-slate-900">${escapeHtml(item.name)}</h3>
          ${badge}
        </div>
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

function renderEmptyResults(container) {
  container.innerHTML = '<p class="rounded-xl bg-white p-4 text-center text-slate-600 ring-1 ring-slate-200">No names found. Try another search.</p>';
}

async function runSearch() {
  const results = await api.get(`/api/names?${getSearchParams().toString()}`);
  els.searchResults.replaceChildren();

  if (!results.length) {
    renderEmptyResults(els.searchResults);
    return;
  }

  results.forEach(item => els.searchResults.appendChild(renderNameCard(item, id => showDetail(id, els.searchResults))));
}

function getSearchParams() {
  const q = els.searchInput.value.trim();
  const gender = getSelectedGender();
  const rarity = getSelectedRarity();
  const params = new URLSearchParams();
  if (q) params.set('q', q);
  if (gender !== 'Any') params.set('gender', gender);
  if (rarity !== 'Any') params.set('rarity', rarity);
  return params;
}

async function runRandom() {
  const response = await fetch(`/api/names/random?${getSearchParams().toString()}`);
  if (response.status === 404) {
    renderEmptyResults(els.searchResults);
    return;
  }
  if (!response.ok) throw new Error(`Request failed: ${response.status}`);
  renderDetail(els.searchResults, await response.json());
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
  const limit = els.yearLimit.value;
  const results = await api.get(`/api/popularity?year=${year}&gender=${gender}&limit=${limit}`);
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
      if (exact) await showDetail(exact.id, els.yearResults);
    });
    els.yearResults.appendChild(row);
  });
}

function renderMetadata(metadata) {
  if (!metadata) return '';

  const sections = [];
  if (metadata.meaning) {
    sections.push(`
      <div>
        <h3 class="text-sm font-semibold uppercase tracking-wide text-slate-500">Meaning</h3>
        <p class="mt-1 text-slate-700">${escapeHtml(metadata.meaning)}</p>
      </div>`);
  }
  if (metadata.origins?.length) {
    sections.push(`
      <div>
        <h3 class="text-sm font-semibold uppercase tracking-wide text-slate-500">Origin</h3>
        <p class="mt-1 text-slate-700">${escapeHtml(metadata.origins.join(', '))}</p>
      </div>`);
  }
  if (metadata.pronunciation) {
    sections.push(`
      <div>
        <h3 class="text-sm font-semibold uppercase tracking-wide text-slate-500">Pronunciation</h3>
        <p class="mt-1 text-slate-700">${escapeHtml(metadata.pronunciation)}</p>
      </div>`);
  }
  if (metadata.description) {
    sections.push(`
      <div>
        <h3 class="text-sm font-semibold uppercase tracking-wide text-slate-500">About</h3>
        <p class="mt-1 text-slate-700">${escapeHtml(metadata.description)}</p>
      </div>`);
  }
  if (metadata.themes?.length) {
    sections.push(`
      <div>
        <h3 class="text-sm font-semibold uppercase tracking-wide text-slate-500">Themes</h3>
        <div class="mt-2 flex flex-wrap gap-2">
          ${metadata.themes.map(theme => `<span class="rounded-full bg-slate-100 px-2.5 py-1 text-xs font-semibold text-slate-700">${escapeHtml(theme)}</span>`).join('')}
        </div>
      </div>`);
  }
  if (metadata.variants?.length) {
    sections.push(`
      <div>
        <h3 class="text-sm font-semibold uppercase tracking-wide text-slate-500">Variants</h3>
        <p class="mt-1 text-slate-700">${escapeHtml(metadata.variants.join(', '))}</p>
      </div>`);
  }

  return sections.length
    ? `<div class="mb-4 space-y-4 rounded-xl bg-slate-50 p-4">${sections.join('')}</div>`
    : '';
}

function renderDetail(container, detail) {
  container.replaceChildren();

  const panel = document.createElement('article');
  panel.className = 'rounded-2xl bg-white p-5 shadow-sm ring-1 ring-slate-200';

  const header = document.createElement('div');
  header.className = 'mb-4';
  const peakBadge = detail.peakRank
    ? `<span class="ml-2 rounded-full px-2 py-0.5 text-xs font-semibold ${rarityBadgeClass(detail.peakRank)}">Peak #${detail.peakRank}</span>`
    : '';
  header.innerHTML = `
    <div class="flex items-center flex-wrap gap-1">
      <h2 class="text-2xl font-bold text-slate-900">${escapeHtml(detail.name)}</h2>
      ${peakBadge}
    </div>
    <p class="text-sm text-slate-600">${genderLabel(detail.gender)}</p>`;
  panel.appendChild(header);

  const slant = document.createElement('div');
  slant.className = 'mb-4';
  slant.innerHTML = renderSlant(detail.maleShare);
  panel.appendChild(slant);

  if (detail.metadata) {
    const metadata = document.createElement('div');
    metadata.innerHTML = renderMetadata(detail.metadata);
    panel.appendChild(metadata);
  }

  if (detail.nicknames?.length) {
    const nicknames = document.createElement('div');
    nicknames.className = 'mb-4';
    nicknames.innerHTML = `
      <h3 class="text-sm font-semibold uppercase tracking-wide text-slate-500">Nicknames</h3>
      <p class="mt-1 text-slate-700">${escapeHtml(detail.nicknames.join(', '))}</p>`;
    panel.appendChild(nicknames);
  }

  const yearsSection = document.createElement('div');
  yearsSection.innerHTML = '<h3 class="text-sm font-semibold uppercase tracking-wide text-slate-500">Popularity by year</h3>';
  const yearsList = document.createElement('div');
  yearsList.className = 'mt-2 max-h-64 space-y-1 overflow-y-auto';
  detail.yearStats.forEach(stat => {
    const row = document.createElement('div');
    row.className = 'flex justify-between rounded-lg bg-slate-50 px-3 py-2 text-sm';
    row.innerHTML = `
      <span class="font-medium text-slate-800">${stat.year}</span>
      <span class="text-slate-600">#${stat.rank} ${genderLabel(stat.sex)} · ${stat.count.toLocaleString()}</span>`;
    yearsList.appendChild(row);
  });
  yearsSection.appendChild(yearsList);
  panel.appendChild(yearsSection);

  container.appendChild(panel);
}

async function showDetail(id, container) {
  const detail = await api.get(`/api/names/${id}`);
  renderDetail(container, detail);
}

function setTab(tab) {
  state.activeTab = tab;
  const isSearch = tab === 'search';
  els.tabSearch.setAttribute('aria-selected', String(isSearch));
  els.tabYear.setAttribute('aria-selected', String(!isSearch));
  els.panelSearch.classList.toggle('hidden', !isSearch);
  els.panelYear.classList.toggle('hidden', isSearch);
}

let searchDebounceTimer;

function scheduleSearch() {
  clearTimeout(searchDebounceTimer);
  searchDebounceTimer = setTimeout(() => runSearch().catch(showError), 300);
}

els.tabSearch.addEventListener('click', () => setTab('search'));
els.tabYear.addEventListener('click', () => setTab('year'));
els.randomBtn.addEventListener('click', () => runRandom().catch(showError));
els.yearSelect.addEventListener('change', () => loadPopularity().catch(showError));
els.yearGender.addEventListener('change', () => loadPopularity().catch(showError));
els.yearLimit.addEventListener('change', () => loadPopularity().catch(showError));
els.searchInput.addEventListener('input', scheduleSearch);
document.querySelectorAll('input[name="gender"]').forEach(input => {
  input.addEventListener('change', scheduleSearch);
});
document.querySelectorAll('input[name="rarity"]').forEach(input => {
  input.addEventListener('change', scheduleSearch);
});

function showError(error) {
  console.error(error);
  alert('Something went wrong. Please try again.');
}

loadYears()
  .then(() => loadPopularity())
  .catch(showError);
runSearch().catch(showError);
