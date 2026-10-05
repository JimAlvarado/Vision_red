fetch('/api/mobile/access', { cache: 'no-store' }).then(r => r.json()).then(data => {
  const codes = document.getElementById('code');
  codes.replaceChildren();
  (data.codes || [data.code]).forEach((code, index) => {
    const row = document.createElement('div');
    const label = document.createElement('p');
    label.className = 'label';
    label.textContent = 'Código ' + (index + 1);
    const value = document.createElement('strong');
    value.textContent = code;
    row.append(label, value);
    codes.append(row);
  });
  const link = document.getElementById('link');
  const url = new URL(data.url);
  const octets = url.hostname.split('.').map(Number);
  const privateHost = octets.length === 4 && octets.every(n => Number.isInteger(n) && n >= 0 && n <= 255) &&
    (octets[0] === 10 || (octets[0] === 172 && octets[1] >= 16 && octets[1] <= 31) || (octets[0] === 192 && octets[1] === 168));
  if (data.mode === 'private-vpn' && privateHost && url.protocol === 'http:' && url.port === '5081' && url.pathname === '/' && !url.username && !url.password) { link.textContent = data.url; link.href = data.url; link.target = '_blank'; link.rel = 'noopener noreferrer'; }
  else link.textContent = 'Acceso VPN pendiente de configuración.';
  document.getElementById('vpnState').textContent = data.listeningOnVpn ? 'Vision está escuchando en la interfaz VPN. Confirma el acceso desde el celular.' : 'La interfaz VPN no estaba disponible al iniciar Vision. Conecta la VPN y reinicia Vision.';
}).catch(() => { document.getElementById('code').textContent = 'No se pudo consultar el acceso.'; });
