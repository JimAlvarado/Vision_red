let signature = '', loading = false;
async function loadAccess() {
  if (loading) return;
  loading = true;
  try {
    const response = await fetch('/api/mobile/access', { cache: 'no-store' });
    if (!response.ok) throw new Error('No se pudo consultar el acceso.');
    const data = await response.json();
    const nextSignature = JSON.stringify(data);
    if (nextSignature === signature) return;
    const codes = document.getElementById('code');
    codes.replaceChildren();
    (data.codes || [data.code]).forEach((code, index) => {
      const access = (data.accesses || []).find(item => item.slot === index + 1);
      const row = document.createElement('div'); row.className = 'access-row';
      const label = document.createElement('p'); label.className = 'label'; label.textContent = 'Código ' + (index + 1);
      const value = document.createElement('strong'); value.textContent = code;
      const state = document.createElement('p'); state.className = 'access-state';
      state.textContent = access?.inUse ? 'En uso · ' + (access.device || 'Navegador de consulta') : 'Disponible · Sin caducidad';
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
    const link = document.getElementById('link');
    const url = new URL(data.url);
    const octets = url.hostname.split('.').map(Number);
    const privateHost = octets.length === 4 && octets.every(n => Number.isInteger(n) && n >= 0 && n <= 255) &&
      (octets[0] === 10 || (octets[0] === 172 && octets[1] >= 16 && octets[1] <= 31) || (octets[0] === 192 && octets[1] === 168));
    if (data.mode === 'private-vpn' && privateHost && url.protocol === 'http:' && url.port === '5081' && url.pathname === '/' && !url.username && !url.password) {
      link.textContent = data.url; link.href = data.url; link.target = '_blank'; link.rel = 'noopener noreferrer';
    } else { link.textContent = 'Acceso VPN pendiente de configuración.'; link.removeAttribute('href'); }
    document.getElementById('vpnState').textContent = data.listeningOnVpn ? 'Vision está escuchando en la interfaz VPN. Confirma el acceso desde el celular.' : 'La interfaz VPN no estaba disponible al iniciar Vision. Conecta la VPN y reinicia Vision.';
    signature = nextSignature;
  } catch { document.getElementById('vpnState').textContent = 'No se pudo actualizar el estado de los accesos.'; }
  finally { loading = false; }
}
loadAccess();
setInterval(loadAccess, 10000);
