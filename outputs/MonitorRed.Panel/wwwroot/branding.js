(() => {
  const mobile = !!document.getElementById('loginForm');
  const image = () => { const img = document.createElement('img'); img.src = '/vision-logo.svg'; img.alt = 'VISION · Aluminio'; img.className = 'vision-emblem'; return img; };
  const brand = document.querySelector('header .brand');
  if (brand) brand.replaceChildren(image());
  const oldMobileLogo = document.querySelector('header .logo');
  if (oldMobileLogo) { oldMobileLogo.replaceWith(image()); document.querySelector('header strong').textContent = 'VISION'; }
  if (!brand && !oldMobileLogo) { const holder = document.createElement('div'); holder.className = 'vision-page-brand'; holder.append(image()); document.querySelector('main')?.prepend(holder); }
  const title = mobile ? document.querySelector('#dashboard .heading h1') : brand ? document.querySelector('.heading h1') : null;
  if (brand) {
    document.body.classList.add('vision-compact');
    const heading = document.querySelector('.workspace > .heading');
    const copy = heading?.querySelector('div');
    if (copy) { copy.classList.add('vision-header-title'); brand.after(copy); }
    const add = document.getElementById('addDevice');
    if (add) document.querySelector('.header-right').before(add);
    heading?.remove();
  }
  const applyTitle = value => { if (title) title.textContent = value; if (brand) document.title = `Vision · ${value}`; };
  if (mobile) {
    const settings=document.createElement('div');settings.className='vision-settings';
    settings.innerHTML='<button type="button" aria-label="Configuración" aria-expanded="false"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" aria-hidden="true"><path d="m10 2-.6 3-2 .9L5 4.7 2.7 8l2.2 2v3l-2.2 2L5 18.3 7.4 17l2 .9.6 3h4l.6-3 2-.9 2.4 1.3 2.3-3.3-2.2-2v-3l2.2-2L19 4.7 16.6 6l-2-.9L14 2z"/><circle cx="12" cy="11.5" r="3.3"/></svg></button><section class="vision-settings-panel" hidden><h2>Configuración</h2><a href="#events">Eventos</a><p>Consulta del historial de Vision.</p></section>';
    document.body.append(settings);const button=settings.querySelector('button'),panel=settings.querySelector('section');
    button.onclick=()=>{panel.hidden=!panel.hidden;button.setAttribute('aria-expanded',String(!panel.hidden));};
    settings.querySelector('a').onclick=e=>{e.preventDefault();panel.hidden=true;button.setAttribute('aria-expanded','false');document.dispatchEvent(new Event('vision-open-events'));};
    document.addEventListener('pointerdown',e=>{if(!settings.contains(e.target)){panel.hidden=true;button.setAttribute('aria-expanded','false');}});
    return;
  }
  document.querySelector('header nav')?.remove();
  document.querySelector('.rail-bottom')?.remove();
  const settings = document.createElement('div'); settings.className = 'vision-settings';
  settings.innerHTML = `<button type="button" aria-label="Configuración" aria-expanded="false" aria-controls="visionSettingsPanel"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" aria-hidden="true"><path d="m10 2-.6 3-2 .9L5 4.7 2.7 8l2.2 2v3l-2.2 2L5 18.3 7.4 17l2 .9.6 3h4l.6-3 2-.9 2.4 1.3 2.3-3.3-2.2-2v-3l2.2-2L19 4.7 16.6 6l-2-.9L14 2z"/><circle cx="12" cy="11.5" r="3.3"/></svg></button><section id="visionSettingsPanel" class="vision-settings-panel" hidden><h2>Configuración</h2><a href="/">Topología</a><a href="/correo.html">Correo</a><a href="/acceso-celular.html">Móvil</a><form><label for="topologyTitleInput">Título de la topología</label><input id="topologyTitleInput" required maxlength="100" value="Topología de red"><button type="submit">Guardar título</button><p role="status" aria-live="polite"></p></form></section>`;
  document.body.append(settings);
  const titleForm = settings.querySelector('form');
  settings.querySelector('h2').after(titleForm);
  const networkSection = document.createElement('details'); networkSection.className = 'mobile-networks-settings';
  networkSection.innerHTML = '<summary>Rangos de acceso móvil</summary><p>Agrega las redes o IP que pueden abrir Vision móvil. Usa una red como 192.168.50.0/24 o una IP como 192.168.50.25. Solo se permiten direcciones privadas.</p><form id="mobileNetworksForm"><label for="mobileRangeInput">Red o IP permitida</label><input id="mobileRangeInput" autocomplete="off" placeholder="192.168.50.0/24"><button type="button" id="mobileRangeAdd">Agregar rango</button><ul id="mobileRangesList"></ul><button type="submit" id="mobileRangesSave" disabled>Guardar rangos</button><p id="mobileRangesStatus" role="status" aria-live="polite">Cargando rangos…</p></form>';
  settings.querySelector('section').append(networkSection);
  const eventsLink=document.createElement('a');eventsLink.href='#events';eventsLink.textContent='Eventos';networkSection.before(eventsLink);
  eventsLink.onclick=e=>{e.preventDefault();close();document.dispatchEvent(new Event('vision-open-events'));};
  const toggle = settings.querySelector('button'), panel = settings.querySelector('section'), input = settings.querySelector('input'), message = settings.querySelector('[role=status]');
  const close = () => { panel.hidden = true; toggle.setAttribute('aria-expanded','false'); };
  toggle.onclick = () => { panel.hidden = !panel.hidden; toggle.setAttribute('aria-expanded',String(!panel.hidden)); };
  document.addEventListener('pointerdown', e => { if (!settings.contains(e.target)) close(); });
  document.addEventListener('keydown', e => { if (e.key === 'Escape') close(); });
  fetch('/api/display-settings').then(r => r.ok ? r.json() : null).then(v => { if(v) { input.value = v.topologyTitle; applyTitle(v.topologyTitle); } }).catch(() => { message.textContent = 'No se pudo cargar el título.'; });
  settings.querySelector('form').onsubmit = async e => {
    e.preventDefault(); const button = e.target.querySelector('button'); button.disabled = true;
    try { const r = await fetch('/api/display-settings',{method:'PUT',headers:{'Content-Type':'application/json','X-Topology-Editor':'1'},body:JSON.stringify({topologyTitle:input.value.trim()})}); if(!r.ok) throw Error('No se pudo guardar el título.'); const v = await r.json(); applyTitle(v.topologyTitle); input.value = v.topologyTitle; message.textContent = 'Título guardado para PC y móvil.'; } catch(err) { message.textContent = err.message; } finally { button.disabled = false; }
  };
  let ranges = [], loaded = false, saving = false;
  const rangeInput = settings.querySelector('#mobileRangeInput'), rangeStatus = settings.querySelector('#mobileRangesStatus');
  const rangeSave = settings.querySelector('#mobileRangesSave'), rangeAdd = settings.querySelector('#mobileRangeAdd');
  const renderRanges = () => {
    const list = settings.querySelector('#mobileRangesList'); list.replaceChildren();
    for (const range of ranges) {
      const row = document.createElement('li'), label = document.createElement('span'), remove = document.createElement('button');
      label.textContent = range; remove.type = 'button'; remove.textContent = 'Quitar'; remove.setAttribute('aria-label', 'Quitar ' + range); remove.disabled = saving;
      remove.onclick = () => { ranges = ranges.filter(value => value !== range); renderRanges(); rangeStatus.textContent = 'Cambios pendientes. Guarda los rangos para aplicarlos.'; };
      row.append(label, remove); list.append(row);
    }
    rangeSave.disabled = !loaded || saving || !ranges.length; rangeAdd.disabled = !loaded || saving; rangeInput.disabled = !loaded || saving;
  };
  const addRange = () => {
    const text = rangeInput.value.trim(), parts = text.split('/'), octets = parts[0].split('.');
    const prefix = parts.length === 1 ? 32 : Number(parts[1]);
    const validOctets = octets.length === 4 && octets.every(value => /^\d{1,3}$/.test(value) && Number(value) <= 255);
    const bytes = octets.map(Number);
    const privateIp = bytes[0] === 10 || bytes[0] === 172 && bytes[1] >= 16 && bytes[1] <= 31 || bytes[0] === 192 && bytes[1] === 168;
    if (!validOctets || !privateIp || parts.length > 2 || !Number.isInteger(prefix) || prefix < 16 || prefix > 32 || parts.length === 2 && !/^\d{2}$/.test(parts[1])) {
      rangeStatus.textContent = 'Escribe una IP privada o una red con prefijo /16 a /32, por ejemplo 192.168.50.0/24.'; return;
    }
    const range = bytes.join('.') + '/' + prefix;
    if (ranges.includes(range)) { rangeStatus.textContent = 'Ese rango ya está en la lista.'; return; }
    if (ranges.length >= 20) { rangeStatus.textContent = 'Puedes guardar hasta 20 rangos.'; return; }
    ranges.push(range); rangeInput.value = ''; renderRanges(); rangeStatus.textContent = 'Cambios pendientes. Guarda los rangos para aplicarlos.';
  };
  rangeAdd.onclick = addRange;
  rangeInput.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); addRange(); } });
  const loadRanges = async () => {
    if (loaded || saving) return;
    try {
      const response = await fetch('/api/mobile/access', { cache: 'no-store' });
      if (!response.ok) throw Error('No se pudieron cargar los rangos. Cierra y abre este apartado para reintentar.');
      const data = await response.json(); ranges = [...(data.allowedSubnets || [])]; loaded = true;
      rangeStatus.textContent = 'Rangos actuales. Al guardar, se aplican a Vision y al firewall sin reiniciar.';
    } catch (error) { rangeStatus.textContent = error.message; }
    renderRanges();
  };
  networkSection.addEventListener('toggle', () => { if (networkSection.open) loadRanges(); });
  settings.querySelector('#mobileNetworksForm').onsubmit = async e => {
    e.preventDefault(); if (!loaded || saving || !ranges.length) return;
    if (rangeInput.value.trim()) { rangeStatus.textContent = 'Primero pulsa Agregar rango para incluir la IP que escribiste.'; return; }
    saving = true; renderRanges(); rangeStatus.textContent = 'Guardando rangos…';
    try {
      const response = await fetch('/api/mobile/networks', { method: 'PUT', headers: { 'Content-Type': 'application/json', 'X-Topology-Editor': '1' }, body: JSON.stringify({ allowedSubnets: ranges }) });
      const result = await response.json(); if (!response.ok) throw Error(result.error || 'No se pudieron guardar los rangos.');
      ranges = result.allowedSubnets; rangeStatus.textContent = 'Rangos guardados y aplicados. Las IP fuera de la lista no pueden abrir Vision móvil.';
    } catch (error) { rangeStatus.textContent = error.message; }
    finally { saving = false; renderRanges(); }
  };
  renderRanges();
})();
