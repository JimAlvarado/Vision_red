const $ = id => document.getElementById(id);
let busy = false;
let draftRecipients = [], savedRecipients = [], recipientsLoaded = false, savingRecipients = false, recipientGeneration = 0, ownerNotificationAddress = null;
const recipientsDirty = () => JSON.stringify(draftRecipients) !== JSON.stringify(savedRecipients);
function renderRecipients() {
  const list = $('recipientList'); list.replaceChildren();
  $('recipientCount').textContent = draftRecipients.length + (draftRecipients.length === 1 ? ' correo' : ' correos');
  for (const [index, address] of draftRecipients.entries()) {
    const row = document.createElement('li'), text = document.createElement('span'), remove = document.createElement('button');
    text.textContent = address; remove.type = 'button'; remove.className = 'remove-recipient'; remove.textContent = 'Quitar';
    remove.setAttribute('aria-label', 'Quitar ' + address); remove.disabled = savingRecipients;
    if (ownerNotificationAddress && ownerNotificationAddress.toLowerCase() === address.toLowerCase()) {
      remove.textContent = 'Autor'; remove.disabled = true; remove.setAttribute('aria-label', 'Correo del autor');
      remove.title = 'El autor recibe las alertas de red y los avisos de administración.';
    }
    remove.addEventListener('click', () => { draftRecipients.splice(index, 1); $('recipientError').textContent = ''; renderRecipients(); });
    row.append(text, remove); list.append(row);
  }
  if (!draftRecipients.length) { const row = document.createElement('li'); row.className = 'empty-recipients'; row.textContent = 'Agrega al menos un correo para recibir notificaciones.'; list.append(row); }
  $('recipientEmail').disabled = $('addRecipient').disabled = !recipientsLoaded || savingRecipients;
  $('saveRecipients').disabled = !recipientsLoaded || savingRecipients || !recipientsDirty() || !draftRecipients.length;
  $('saveRecipients').textContent = savingRecipients ? 'Guardando…' : 'Guardar cambios';
  $('recipientFeedback').textContent = savingRecipients ? 'Guardando la lista…' : recipientsDirty() ? 'Tienes cambios sin guardar.' : 'Lista actualizada.';
}
const states = { starting: 'Solicitando código a Microsoft…', waiting: 'Esperando tu autorización en Microsoft…', connected: 'Buzón autorizado.', disconnected: 'Buzón pendiente de autorización.', error: 'Se requiere atención.' };
async function api(path, method = 'GET', body) {
  const response = await fetch(path, { method, cache: 'no-store', headers: method === 'GET' ? {} : { 'X-Topology-Editor': '1', ...(body ? { 'Content-Type': 'application/json' } : {}) }, ...(body ? { body: JSON.stringify(body) } : {}) });
  const data = await response.json();
  if (!response.ok) throw new Error(data.error || 'No se pudo completar la operación.');
  return data;
}
async function refresh() {
  const generation = recipientGeneration;
  try {
    const data = await api('/api/email/status');
    const auth = data.authorization;
    ownerNotificationAddress = data.ownerNotificationsEnabled ? data.ownerNotificationAddress : null;
    $('sender').textContent = $('expected').textContent = data.senderAddress;
    $('recipient').textContent = data.testRecipient;
    $('automaticState').textContent = data.automaticAlertsEnabled ? 'Envío automático habilitado.' : 'Envío automático desactivado.';
    $('ownerState').textContent = data.ownerNotificationsEnabled ? 'Avisos al autor habilitados: ' + data.ownerNotificationAddress + '. Nuevos destinatarios e inicios de sesión en la consulta.' : 'Avisos al autor pendientes de configurar en el servidor.';
    if (generation === recipientGeneration && !savingRecipients && !recipientsDirty()) {
      if (!recipientsLoaded || JSON.stringify(savedRecipients) !== JSON.stringify(data.recipients)) {
        draftRecipients = [...data.recipients]; savedRecipients = [...data.recipients]; recipientsLoaded = true; renderRecipients();
      }
    }
    $('status').textContent = auth.error || states[auth.state] || auth.state;
    $('instructions').hidden = auth.state !== 'waiting';
    $('code').textContent = auth.userCode || '';
    if (auth.verificationUrl) {
      const url = new URL(auth.verificationUrl);
      if (url.protocol === 'https:' && ['microsoft.com', 'www.microsoft.com', 'login.microsoftonline.com'].includes(url.hostname)) $('verification').href = url.href;
    }
    $('expiration').textContent = auth.expiresAtUtc ? 'Vence: ' + VisionTime.time(auth.expiresAtUtc) : '';
    $('authorize').disabled = busy || ['starting', 'waiting'].includes(auth.state);
    const test = data.test;
    $('send').disabled = busy || auth.state !== 'connected' || ['accepted', 'sending', 'unknown'].includes(test?.state);
    if (test) {
      const labels = { accepted: 'Microsoft aceptó la prueba. Revisa la bandeja de entrada y correo no deseado del destinatario.', sending: 'Prueba registrada en envío. Si Vision se reinició, revisa Elementos enviados antes de repetir.', unknown: 'Resultado sin confirmar. Revisa Elementos enviados antes de repetir.', failed: 'Microsoft rechazó la prueba.' };
      $('result').textContent = (labels[test.state] || test.state) + (test.error ? ' ' + test.error : '');
    }
    const alerts = await api('/api/notifications');
    const list = $('alerts'); list.replaceChildren();
    const statuses = { 'awaiting-configuration': 'Pendiente', retry: 'Reintento programado', sending: 'Enviando', accepted: 'Aceptado por Microsoft', failed: 'Requiere revisión', unknown: 'Envío sin confirmar', expired: 'Aviso antiguo: consultar incidente' };
    if (!alerts.length) { const p = document.createElement('p'); p.textContent = 'Sin avisos automáticos registrados.'; list.append(p); }
    for (const alert of alerts.slice(0, 20)) { const p = document.createElement('p'); p.textContent = alert.subject + ' · ' + (statuses[alert.status] || alert.status) + (alert.lastError ? ' · ' + alert.lastError : ''); list.append(p); }
  } catch (error) { $('status').textContent = error.message; }
}
$('authorize').addEventListener('click', async () => {
  busy = true; $('authorize').disabled = true;
  try { await api('/api/email/authorize', 'POST'); } catch (error) { $('result').textContent = error.message; }
  finally { busy = false; await refresh(); }
});
$('send').addEventListener('click', async () => {
  busy = true; $('send').disabled = true; $('result').textContent = 'Enviando prueba…';
  try { await api('/api/email/test', 'POST'); } catch (error) { $('result').textContent = error.message; }
  finally { busy = false; await refresh(); }
});
$('recipientForm').addEventListener('submit', event => {
  event.preventDefault(); if (!recipientsLoaded || savingRecipients) return;
  const input = $('recipientEmail'), address = input.value.trim(); input.value = address;
  if (!input.reportValidity()) return;
  if (draftRecipients.some(value => value.toLowerCase() === address.toLowerCase())) { $('recipientError').textContent = 'Este correo ya está en la lista.'; return; }
  if (draftRecipients.length >= 50) { $('recipientError').textContent = 'Puedes agregar hasta 50 destinatarios.'; return; }
  draftRecipients.push(address); input.value = ''; $('recipientError').textContent = ''; renderRecipients(); input.focus();
});
$('saveRecipients').addEventListener('click', async () => {
  if (!recipientsLoaded || savingRecipients || !recipientsDirty() || !draftRecipients.length) return;
  savingRecipients = true; recipientGeneration++; $('recipientError').textContent = ''; renderRecipients();
  try {
    const data = await api('/api/email/recipients', 'PUT', { recipients: [...draftRecipients] });
    draftRecipients = [...data.recipients]; savedRecipients = [...data.recipients];
    $('recipientEmail').value = '';
  } catch (error) { $('recipientError').textContent = error.message; }
  finally { savingRecipients = false; renderRecipients(); }
});
window.addEventListener('beforeunload', event => { if (recipientsDirty()) { event.preventDefault(); event.returnValue = ''; } });
refresh(); setInterval(refresh, 4000);
