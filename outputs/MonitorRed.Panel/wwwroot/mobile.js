const $ = id => document.getElementById(id), ns = 'http://www.w3.org/2000/svg';
let snapshot = null, topology = { devices: [], edges: [] }, history = [], filter = 'all', loading = false, revision = -1, loginSubmissionVersion = 0;
let view = { x: 0, y: 0, w: 1000, h: 600 }, bounds = { ...view }, dragMoved = false;
let touchedIp = null, mapViewReady = false, lastDetailTap = null;
function detailTap(device, event) {
  const now=performance.now(), x=event.clientX, y=event.clientY;
  if(lastDetailTap && lastDetailTap.id===device.id && now-lastDetailTap.at<=450 && Math.hypot(x-lastDetailTap.x,y-lastDetailTap.y)<25) {lastDetailTap=null;detail(device);}
  else lastDetailTap={id:device.id,at:now,x,y};
}
const pointers = new Map(); let lastGesture = null;
let refreshCompletion = Promise.resolve();
const labels = { online: 'IP responde', offline: 'IP sin respuesta', pending: 'Confirmando', unreachable: 'Acceso sin confirmar', 'probe-error': 'Error de lectura', unknown: 'Sin datos' };
const date = value => VisionTime.dateTime(value);
async function api(path, method = 'GET', data) {
  const controller=new AbortController(), timeout=setTimeout(()=>controller.abort(),12000);
  let response;
  try { response = await fetch(path, { signal:controller.signal, method, cache: 'no-store', credentials: 'same-origin', headers: method === 'GET' ? {} : { 'Content-Type': 'application/json', 'X-Vision-Mobile': '1' }, body: data ? JSON.stringify(data) : undefined }); }
  catch(error) { throw Error(error.name==='AbortError'?'Vision tardó en responder. Comprueba la VPN y vuelve a intentar.':'No se pudo conectar con Vision. Comprueba la VPN y la dirección.'); }
  finally { clearTimeout(timeout); }
  if (response.status === 401 && path !== '/api/mobile/login') {
    const error=Error('La sesión no está disponible. Introduce el código nuevamente; si persiste, permite las cookies para esta dirección.');error.signInRequired=true;throw error;
  }
  if (!response.ok) { const error = await response.json().catch(() => ({})); throw Error(error.error || 'No se pudo consultar Vision.'); }
  return response.json();
}
function element(tag, text, className) { const node = document.createElement(tag); if (text !== undefined) node.textContent = text; if (className) node.className = className; return node; }
function stateOf(ip) { return snapshot?.devices.find(d => d.ip === ip)?.status || 'unknown'; }
function detail(device) {
  const reading = snapshot?.devices.find(d => d.ip === device.ip);
  $('detailName').textContent = device.name; const body = $('detailBody'); body.replaceChildren();
  body.append(element('p', 'IP: ' + device.ip), element('p', 'Estado: ' + (labels[reading?.status] || 'Sin datos')),
    element('p', 'Prioridad: ' + (device.critical ? 'Crítico' : 'Normal')), element('p', 'Latencia: ' + (reading?.latencyMs == null ? 'Sin respuesta medida' : reading.latencyMs + ' ms')));
  const parents = topology.edges.filter(e => e.target === device.id).map(e => topology.devices.find(d => d.id === e.source)?.name).filter(Boolean);
  body.append(element('p', 'Conexiones entrantes: ' + (parents.join(', ') || 'Sin conexiones dibujadas')));
  if (device.note) body.append(element('p', device.note));
  if (!$('detail').open) { if (typeof $('detail').showModal === 'function') $('detail').showModal(); else $('detail').setAttribute('open', ''); }
}
function renderDevices() {
  const search = $('search').value.toLocaleLowerCase(); const list = $('devices'); list.replaceChildren();
  const devices = topology.devices.filter(d => (d.name + ' ' + d.ip).toLocaleLowerCase().includes(search) && (filter === 'all' || filter === 'online' && stateOf(d.ip) === 'online' || filter === 'attention' && stateOf(d.ip) !== 'online'));
  for (const device of devices) {
    const state = stateOf(device.ip), reading = snapshot?.devices.find(d => d.ip === device.ip);
    const card = element('article', undefined, 'card'); card.tabIndex = 0; card.setAttribute('role', 'button'); card.setAttribute('aria-label', 'Ver detalle de ' + device.name);
    card.append(element('h2', device.name), element('div', device.ip, 'ip'));
    const row = element('div', undefined, 'row'); row.append(element('span', labels[state] || state, 'badge ' + state));
    row.append(element('span', reading?.latencyMs == null ? (device.critical ? 'Crítico' : 'Normal') : reading.latencyMs + ' ms', device.critical ? 'critical' : ''));
    card.append(row); card.onclick = e => detailTap(device,e); card.onkeydown = e => { if (e.key === 'Enter') detail(device); }; list.append(card);
  }
  if (!devices.length) list.append(element('p', 'No hay equipos que coincidan con el filtro.', 'muted'));
}
function renderIncidents() {
  const list = $('incidents'); list.replaceChildren();
  for (const incident of history.slice(0, 50)) {
    const row = element('article', undefined, 'card');
    row.append(element('h2', incident.name), element('span', incident.closedAtUtc ? 'Recuperado' : 'Activo', 'badge ' + (incident.closedAtUtc ? 'online' : 'offline')));
    if (incident.ip !== 'monitor-access') row.append(element('p', incident.ip, 'muted'));
    row.append(element('p', 'Inicio: ' + date(incident.openedAtUtc), 'muted'));
    if (incident.closedAtUtc) row.append(element('p', 'Recuperación: ' + date(incident.closedAtUtc), 'muted'));
    list.append(row);
  }
  if (!history.length) list.append(element('p', 'Sin incidentes registrados. Los equipos sin respuesta desde antes de activar las alertas requieren revisión de su IP y acceso.', 'muted'));
}
function svg(tag, attrs = {}) { const node = document.createElementNS(ns, tag); for (const [key, value] of Object.entries(attrs)) node.setAttribute(key, value); return node; }
function applyView() { $('map').setAttribute('viewBox', `${view.x} ${view.y} ${view.w} ${view.h}`); try { sessionStorage.setItem('vision-mobile-map-view',JSON.stringify(view)); } catch {} }
function drawMap() {
  const map = $('map'); map.replaceChildren();
  if (!topology.devices.length) return;
  const minX = Math.min(...topology.devices.map(d => d.x)), minY = Math.min(...topology.devices.map(d => d.y));
  const maxX = Math.max(...topology.devices.map(d => d.x + 226)), maxY = Math.max(...topology.devices.map(d => d.y + 98));
  bounds = { x: minX - 60, y: minY - 60, w: Math.max(300, maxX - minX + 120), h: Math.max(300, maxY - minY + 120) };
  const lines = svg('g'); map.append(lines);
  for (const edge of topology.edges) {
    const a = topology.devices.find(d => d.id === edge.source), b = topology.devices.find(d => d.id === edge.target); if (!a || !b) continue;
    const startX = a.x + 226, startY = a.y + 49, endX = b.x, endY = b.y + 49, distance = Math.max(80, Math.abs(endX - startX) * .45);
    const path = `M ${startX} ${startY} C ${startX + distance} ${startY} ${endX - distance} ${endY} ${endX} ${endY}`;
    const group = svg('g', { class: 'map-edge', 'data-source': a.ip, 'data-target': b.ip });
    group.append(svg('path', { d: path }));
    const packet = svg('circle', { r: 5 }); const motion = svg('animateMotion', { dur: '3s', repeatCount: 'indefinite', path, keyPoints: '0;1', keyTimes: '0;1', calcMode: 'linear' });
    packet.append(motion); group.append(packet); lines.append(group);
  }
  for (const device of topology.devices) {
    const group = svg('g', { transform: `translate(${device.x} ${device.y})`, class: 'map-node', 'data-ip': device.ip, tabindex: '0', role: 'button', 'aria-label': device.name + ' ' + device.ip });
    group.append(svg('rect', { width: 226, height: 98, rx: 12 }));
    const name = svg('text', { x: 14, y: 31 }); name.textContent = device.name.length > 26 ? device.name.slice(0, 25) + '…' : device.name;
    const ip = svg('text', { x: 14, y: 54, class: 'map-ip' }); ip.textContent = device.ip;
    const status = svg('text', { x: 14, y: 78, class: 'map-state' }); group.append(name, ip, status);
    group.onkeydown = e => { if (e.key === 'Enter') detail(device); }; map.append(group);
  }
  if (!mapViewReady) {
    view = { ...bounds };
    try { const saved=JSON.parse(sessionStorage.getItem('vision-mobile-map-view')); if(saved && ['x','y','w','h'].every(k=>Number.isFinite(saved[k])) && saved.w>=180 && saved.h>0) view=saved; } catch {}
    mapViewReady=true;
  }
  applyView(); updateMap();
}
function updateMap() {
  for (const node of $('map').querySelectorAll('.map-node')) { const state = stateOf(node.dataset.ip); node.setAttribute('class', 'map-node ' + state); node.querySelector('.map-state').textContent = labels[state] || state; }
  for (const edge of $('map').querySelectorAll('.map-edge')) {
    const a = stateOf(edge.dataset.source), b = stateOf(edge.dataset.target);
    const state = a === 'online' && b === 'online' ? 'online' : a === 'offline' || b === 'offline' ? 'offline' : a === 'unreachable' || b === 'unreachable' ? 'unreachable' : 'pending';
    if (edge.dataset.state !== state) {
      edge.dataset.state = state; edge.setAttribute('class', 'map-edge ' + state);
      edge.querySelector('animateMotion').setAttribute('keyPoints', state === 'online' ? '0;1' : '0;0.5');
      edge.querySelector('circle').style.display = state === 'pending' ? 'none' : '';
    }
  }
}
function zoom(factor) { const w = Math.min(bounds.w * 2, Math.max(180, view.w * factor)), h = view.h * w / view.w; view = { x: view.x + (view.w - w) / 2, y: view.y + (view.h - h) / 2, w, h }; applyView(); }
$('map').addEventListener('pointerdown', e => { if (!pointers.size) { dragMoved = false; touchedIp = e.target.closest('.map-node')?.dataset.ip || null; } pointers.set(e.pointerId, { x: e.clientX, y: e.clientY }); $('map').setPointerCapture(e.pointerId); lastGesture = gesture(); });
function gesture() { const p = [...pointers.values()]; return p.length === 1 ? { x: p[0].x, y: p[0].y, distance: 0 } : p.length >= 2 ? { x: (p[0].x + p[1].x) / 2, y: (p[0].y + p[1].y) / 2, distance: Math.hypot(p[0].x - p[1].x, p[0].y - p[1].y) } : null; }
$('map').addEventListener('pointermove', e => {
  if (!pointers.has(e.pointerId)) return; pointers.set(e.pointerId, { x: e.clientX, y: e.clientY }); const current = gesture();
  if (current && lastGesture) {
    const dx = current.x - lastGesture.x, dy = current.y - lastGesture.y;
    if (Math.abs(dx) + Math.abs(dy) > 2 || current.distance) dragMoved = true;
    const scale = Math.min($('map').clientWidth / view.w, $('map').clientHeight / view.h);
    view.x -= dx / scale; view.y -= dy / scale;
    if (current.distance && lastGesture.distance) zoom(lastGesture.distance / current.distance); else applyView();
  }
  lastGesture = current;
});
for (const event of ['pointerup', 'pointercancel']) $('map').addEventListener(event, e => { pointers.delete(e.pointerId); if (event === 'pointerup' && !pointers.size && !dragMoved && touchedIp) { const device = topology.devices.find(d => d.ip === touchedIp); if (device) detailTap(device,e); } else lastDetailTap=null; touchedIp = null; lastGesture = gesture(); });
$('fit').onclick = () => { view = { ...bounds }; applyView(); }; $('zoomIn').onclick = () => zoom(.75); $('zoomOut').onclick = () => zoom(1.33);
async function refresh(reportError=false) {
  if (loading) {await refreshCompletion;if(reportError)return refresh(true);return;}
  loading = true;
  let completed;refreshCompletion=new Promise(resolve=>completed=resolve);
  const refreshVersion=loginSubmissionVersion;
  try {
    const [status, model, incidents, mail, display] = await Promise.all([api('/api/status'), api('/api/topology'), api('/api/incidents'), api('/api/mobile/overview'), api('/api/display-settings')]);
    if(refreshVersion!==loginSubmissionVersion)return;
    document.querySelector('#dashboard .heading h1').textContent = display.topologyTitle;
    snapshot = status; topology = model; history = incidents;
    $('login').hidden = true; $('dashboard').hidden = $('logout').hidden = false;
    $('total').textContent = topology.devices.length; $('online').textContent = status.devices.filter(d => d.status === 'online').length;
    $('offline').textContent = status.devices.filter(d => d.status === 'offline').length; $('active').textContent = incidents.filter(i => !i.closedAtUtc).length;
    const stale = !status.checkedAtUtc || Date.now() - new Date(status.checkedAtUtc).getTime() > 35000;
    $('healthTitle').textContent = stale ? 'Lectura pendiente o desactualizada' : status.condition === 'operating' ? 'Monitor en operación' : 'Acceso a la red sin confirmar';
    $('healthDot').style.background = stale ? '#9bacc2' : status.condition === 'operating' ? '#7ee4cf' : '#f5c56c';
    $('updated').textContent = status.checkedAtUtc ? 'Última lectura: ' + date(status.checkedAtUtc) : 'Esperando primera lectura';
    $('warning').hidden = !stale && status.condition === 'operating' && status.incidentDetectionEnabled;
    $('warning').textContent = stale ? 'Los datos están desactualizados. Confirma la conexión con Vision.' : status.condition !== 'operating' ? 'Vision no puede confirmar el acceso a la red. Revisar VPN y rutas; no se asume que todos los equipos estén apagados.' : 'Modo de observación: incidentes desactivados.';
    $('mailState').textContent = mail.automaticAlertsEnabled ? `${mail.pending} pendientes · ${mail.needsAttention} requieren revisión` : 'Envío automático desactivado';
    $('mailCount').textContent = mail.accepted + ' aceptados por Microsoft';
    renderDevices(); renderIncidents(); if (model.revision !== revision) { revision = model.revision; drawMap(); } else updateMap();
  } catch (error) {
    if(refreshVersion!==loginSubmissionVersion)return;
    if(error.signInRequired){$('login').hidden=false;$('dashboard').hidden=$('logout').hidden=true;}
    if(reportError)throw error;
    if (!$('dashboard').hidden) { $('warning').hidden = false; $('warning').textContent = 'No se pudo actualizar Vision. Se conserva la última lectura; revisa su hora antes de interpretarla.'; $('healthTitle').textContent = 'Conexión con Vision interrumpida'; $('healthDot').style.background = '#f5c56c'; }
  } finally { loading = false;completed(); }
}
$('loginForm').onsubmit = async e => { e.preventDefault(); const button = e.target.querySelector('button'); if(button.disabled)return;loginSubmissionVersion++;button.disabled = true;button.textContent='Verificando acceso…'; $('loginError').textContent = ''; try { await api('/api/mobile/login', 'POST', { code: $('code').value }); $('code').value = ''; await refresh(true); } catch (error) { $('login').hidden=false;$('dashboard').hidden=$('logout').hidden=true;$('loginError').textContent = error.message; } finally { button.disabled = false;button.textContent='Entrar al panel'; } };
$('logout').onclick = async () => { loginSubmissionVersion++;await api('/api/mobile/logout', 'POST'); $('dashboard').hidden = $('logout').hidden = true; $('login').hidden = false; };
$('search').oninput = renderDevices; $('refresh').onclick = refresh;
for (const button of document.querySelectorAll('[data-filter]')) button.onclick = () => { filter = button.dataset.filter; for (const b of document.querySelectorAll('[data-filter]')) b.setAttribute('aria-pressed', b === button); renderDevices(); };
for (const name of ['devices', 'map', 'incidents']) $('tab-' + name).onclick = () => { for (const item of ['devices', 'map', 'incidents']) { $(item + 'Pane').hidden = item !== name; $('tab-' + item).setAttribute('aria-selected', item === name); } };
$('closeDetail').onclick = () => { if (typeof $('detail').close === 'function') $('detail').close(); else $('detail').removeAttribute('open'); };
// Resume a valid cookie without letting a late startup response reset a new sign-in.
(async()=>{const controller=new AbortController(),timer=setTimeout(()=>controller.abort(),12000);try{const response=await fetch('/api/mobile/overview',{credentials:'same-origin',cache:'no-store',signal:controller.signal});if(response.ok && loginSubmissionVersion===0)await refresh();}catch{}finally{clearTimeout(timer);}})();
setInterval(() => { if (!$('dashboard').hidden) refresh(); }, 5000);
document.addEventListener('visibilitychange', () => { if (!document.hidden && !$('dashboard').hidden) refresh(); });
