let signature = '', loading = false, latest = null, sending = false;
// Requires a dotted domain such as empresa.com, like the server.
const validEmailDomain = address => { const domain = address.split('@').pop() || ''; return domain.includes('.') && !domain.startsWith('.') && !domain.endsWith('.') && !domain.includes('..'); };
const invitationFor = (data, slot) => (data?.invitations || []).find(item => item.slot === slot);
const freeSlots = data => (data?.codes || [data?.code]).map((_, index) => index + 1).filter(slot => !(data.accesses || []).find(item => item.slot === slot)?.inUse);
function updateInviteHint() {
  const hint = document.getElementById('inviteHint');
  if (!latest) return;
  const slot = Number(document.getElementById('inviteSlot').value);
  const invitation = slot ? invitationFor(latest, slot) : null;
  hint.textContent = !freeSlots(latest).length ? 'Todos los códigos están en uso. Libera uno para invitar a otra persona.' :
    invitation ? 'Este código ya se envió a ' + invitation.email + '. Si lo envías a otra persona, solo una podrá usarlo a la vez.' :
    'La persona recibirá el código, el enlace y los pasos para entrar desde su celular o tablet.';
}
function renderInviteOptions(data) {
  const select = document.getElementById('inviteSlot'), previous = select.value, free = freeSlots(data);
  select.replaceChildren();
  for (const slot of free) {
    const option = document.createElement('option'), invitation = invitationFor(data, slot);
    option.value = String(slot);
    option.textContent = 'Código ' + slot + ' · Libre' + (invitation ? ' · ya enviado a ' + invitation.email : '');
    select.append(option);
  }
  if (!free.length) { const option = document.createElement('option'); option.value = ''; option.textContent = 'No hay códigos libres'; select.append(option); }
  // Keep the administrator's choice; otherwise prefer a free code that was never sent.
  const preferred = free.find(slot => !invitationFor(data, slot)) ?? free[0];
  select.value = free.includes(Number(previous)) ? previous : String(preferred ?? '');
  select.disabled = !free.length || sending;
  document.getElementById('inviteSend').disabled = !free.length || sending;
  updateInviteHint();
}
async function loadAccess() {
  if (loading) return;
  loading = true;
  try {
    const response = await fetch('/api/mobile/access', { cache: 'no-store' });
    if (!response.ok) throw new Error('No se pudo consultar el acceso.');
    const data = await response.json();
    const nextSignature = JSON.stringify(data);
    if (nextSignature === signature) return;
    latest = data;
    const codes = document.getElementById('code');
    codes.replaceChildren();
    (data.codes || [data.code]).forEach((code, index) => {
      const access = (data.accesses || []).find(item => item.slot === index + 1);
      const invitation = invitationFor(data, index + 1);
      const row = document.createElement('div'); row.className = 'access-row';
      const label = document.createElement('p'); label.className = 'label'; label.textContent = 'Código ' + (index + 1);
      const value = document.createElement('strong'); value.textContent = code;
      const state = document.createElement('p'); state.className = 'access-state';
      state.textContent = (access?.inUse ? 'En uso · ' + (access.device || 'Navegador de consulta') : 'Libre') +
        (invitation ? ' · Invitación enviada a ' + invitation.email + ' el ' + VisionTime.dateTime(invitation.sentAtUtc) : '');
      row.append(label, value, state);
      if (access?.inUse) {
        const release = document.createElement('button'); release.type = 'button'; release.textContent = 'Liberar acceso';
        release.onclick = async () => {
          if (!window.confirm('¿Liberar el acceso ' + (index + 1) + '? La sesión actual se cerrará y este código podrá usarse en otro dispositivo.')) return;
          release.disabled = true;
          try {
            const result = await fetch('/api/mobile/access/' + (index + 1) + '/release', {
              method: 'POST', headers: { 'X-Topology-Editor': '1' }
            });
            if (!result.ok) throw new Error('No se pudo liberar el acceso. Intenta de nuevo.');
            signature = ''; await loadAccess();
          } catch (error) { state.textContent = error.message; release.disabled = false; }
        };
        row.append(release);
      }
      codes.append(row);
    });
    renderInviteOptions(data);
    const link = document.getElementById('link');
    const url = new URL(data.url);
    const octets = url.hostname.split('.').map(Number);
    const privateHost = octets.length === 4 && octets.every(n => Number.isInteger(n) && n >= 0 && n <= 255) &&
      (octets[0] === 10 || (octets[0] === 172 && octets[1] >= 16 && octets[1] <= 31) || (octets[0] === 192 && octets[1] === 168));
    if (data.mode === 'private-vpn' && privateHost && url.protocol === 'http:' && url.port === '5081' && url.pathname === '/' && !url.username && !url.password) {
      link.textContent = data.url; link.href = data.url; link.target = '_blank'; link.rel = 'noopener noreferrer';
    } else { link.textContent = 'Acceso VPN pendiente de configuración.'; link.removeAttribute('href'); }
    const networks = data.allowedSubnets || [];
    document.getElementById('networks').textContent = networks.length ? 'Redes autorizadas: ' + networks.join(', ') : '';
    document.getElementById('vpnState').textContent = data.listeningOnVpn ? 'Vision está escuchando en la interfaz VPN. Confirma el acceso desde el dispositivo móvil.' : 'La interfaz VPN no estaba disponible al iniciar Vision. Conecta la VPN y reinicia Vision.';
    signature = nextSignature;
  } catch { document.getElementById('vpnState').textContent = 'No se pudo actualizar el estado de los accesos.'; }
  finally { loading = false; }
}
document.getElementById('inviteSlot').addEventListener('change', updateInviteHint);
document.getElementById('inviteForm').addEventListener('submit', async event => {
  event.preventDefault();
  if (sending || !latest) return;
  const input = document.getElementById('inviteEmail'), result = document.getElementById('inviteResult'), button = document.getElementById('inviteSend');
  const email = input.value.trim(); input.value = email;
  if (!input.reportValidity()) return;
  if (!validEmailDomain(email)) { result.textContent = 'Escribe el correo completo, con dominio (por ejemplo nombre@empresa.com).'; return; }
  const slot = Number(document.getElementById('inviteSlot').value);
  if (!slot) return;
  const invitation = invitationFor(latest, slot);
  const warning = invitation && invitation.email.toLowerCase() !== email.toLowerCase() ? '\n\nEste código ya se envió a ' + invitation.email + '; solo una persona podrá usarlo a la vez.' : '';
  if (!window.confirm('¿Enviar a ' + email + ' una invitación con el código ' + slot + '?' + warning)) return;
  sending = true; button.disabled = true; button.textContent = 'Enviando…'; result.textContent = '';
  try {
    const response = await fetch('/api/mobile/invite', {
      method: 'POST', cache: 'no-store', headers: { 'X-Topology-Editor': '1', 'Content-Type': 'application/json' }, body: JSON.stringify({ email, slot })
    });
    const data = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(data.error || 'No se pudo enviar la invitación.');
    result.textContent = 'Invitación enviada a ' + data.email + ' con el código ' + data.slot + '. Microsoft aceptó el envío; pide a la persona revisar también el correo no deseado.';
    input.value = '';
  } catch (error) { result.textContent = error.message; }
  finally { sending = false; button.textContent = 'Enviar invitación'; signature = ''; await loadAccess(); }
});
loadAccess();
setInterval(loadAccess, 10000);
