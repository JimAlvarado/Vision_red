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
  const eventsLink=document.createElement('a');eventsLink.href='#events';eventsLink.textContent='Eventos';settings.querySelector('form').before(eventsLink);
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
})();
