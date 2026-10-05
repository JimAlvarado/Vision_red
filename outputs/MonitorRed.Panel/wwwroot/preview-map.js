// Vista de demostración sobre el lienzo. No cambia el inventario ni sus conexiones.
let mapPreviewActive = true;
const previewButton = document.createElement('button');
previewButton.id = 'mapPreviewButton';
previewButton.type = 'button';
previewButton.textContent = 'Ocultar vista previa';
previewButton.setAttribute('aria-pressed', 'true');
$('#previewStates').before(previewButton);
const previewBand = document.createElement('div');
previewBand.id = 'previewBand';
previewBand.innerHTML = '<span class="preview-badge">DEMOSTRACIÓN</span><span>Estados y enlaces simulados para revisar las animaciones. No son lecturas reales.</span>';
$('#canvas').append(previewBand);
const originalEdges = edges;
const originalRender = render;
function previewPath(a, b, status) {
  const x = a.x + 226, y = a.y + 49, x2 = b.x, y2 = b.y + 49;
  const curve = Math.max(85, Math.abs(x2 - x) * .45);
  const group = document.createElementNS(svgNS, 'g');
  group.setAttribute('class', 'edge-group state-' + status + ' preview-edge');
  const path = document.createElementNS(svgNS, 'path');
  path.setAttribute('class', 'edge');
  path.setAttribute('d', `M${x},${y} C${x + curve},${y} ${x2 - curve},${y2} ${x2},${y2}`);
  const title = document.createElementNS(svgNS, 'title');
  title.textContent = 'Enlace de demostración, no guardado';
  group.append(path, title);
  $('#edgePaths').append(group);
  createLinkPacket(group, path, status);
}
edges = function () {
  originalEdges();
  if (!mapPreviewActive || model.devices.length < 3) return;
  previewPath(model.devices[0], model.devices[1], 'online');
  previewPath(model.devices[1], model.devices[2], 'offline');
};
render = function () {
  originalRender();
  if (!mapPreviewActive) return;
  const examples = [
    ['online', '● Vista previa: conectado'],
    ['online', '● Vista previa: conectado'],
    ['offline', '× Vista previa: sin conexión'],
    ['pending', '◌ Vista previa: pendiente']
  ];
  model.devices.slice(0, examples.length).forEach((device, index) => {
    const node = [...document.querySelectorAll('#nodes .node')].find(element => element.dataset.id === device.id);
    if (!node) return;
    node.classList.remove('state-unknown');
    node.classList.add('state-' + examples[index][0]);
    const label = node.querySelector('.node-state');
    label.textContent = examples[index][1];
    label.title = 'Estado simulado. El monitoreo en vivo aún no está conectado.';
  });
};
previewButton.onclick = () => {
  mapPreviewActive = !mapPreviewActive;
  previewButton.textContent = mapPreviewActive ? 'Ocultar vista previa' : 'Mostrar vista previa';
  previewButton.setAttribute('aria-pressed', String(mapPreviewActive));
  previewBand.hidden = !mapPreviewActive;
  render();
};
// app.js inicia la carga de datos antes de cargar este archivo. Si ya terminó, repintar.
if (ready) render();

