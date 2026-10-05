const $ = id => document.getElementById(id);
let busy = false;
const states = { starting: 'Solicitando código a Microsoft…', waiting: 'Esperando tu autorización en Microsoft…', connected: 'Buzón autorizado.', disconnected: 'Buzón pendiente de autorización.', error: 'Se requiere atención.' };
async function api(path, method = 'GET') {
  const response = await fetch(path, { method, cache: 'no-store', headers: method === 'GET' ? {} : { 'X-Topology-Editor': '1' } });
  const data = await response.json();
  if (!response.ok) throw new Error(data.error || 'No se pudo completar la operación.');
  return data;
}
async function refresh() {
  try {
    const data = await api('/api/email/status');
    const auth = data.authorization;
    $('sender').textContent = $('expected').textContent = data.senderAddress;
    $('recipient').textContent = data.testRecipient;
    $('automaticState').textContent = data.automaticAlertsEnabled ? 'Envío automático habilitado. Destinatarios: ' + data.recipients.join(', ') : 'Envío automático desactivado.';
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
refresh(); setInterval(refresh, 4000);
