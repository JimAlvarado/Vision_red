// Estados recibidos del monitor de esta PC. La vista previa se mantiene separada.
const monitored = new Map();
let monitorCondition = 'starting';
let monitorCheckedAt = null;
let incidentDetectionEnabled = false;

const monitorBand = document.createElement('div');
monitorBand.id = 'monitorBand';
monitorBand.hidden = true;
$('#canvas').append(monitorBand);
function effectiveCondition() {
  if (!monitorCheckedAt || Date.now() - Date.parse(monitorCheckedAt) > 30000) return 'stale';
  return monitorCondition;
}
function visualState(ip) {
  if (effectiveCondition() !== 'operating') return effectiveCondition() === 'no-reachability' ? 'unreachable' : 'unknown';
  const value = monitored.get(ip)?.status || 'unknown';
  return ['online','offline','pending','unreachable'].includes(value) ? value : 'unknown';
}
const labels = {
  online:'● IP responde',offline:'× IP sin respuesta',pending:'◌ Confirmando',
  unreachable:'— Sin acceso desde esta PC',unknown:'● Sin datos'
};
const routeWarningLabel='● La IP responde, pero la ruta dibujada anterior no responde. Comprueba la identidad del equipo y sus enlaces.';
function routeEvidence(id, visiting=new Set()) {
  if(visiting.has(id))return null;
  const device=model.devices.find(d=>d.id===id);
  if(!device)return null;
  const state=visualState(device.ip);
  if(state==='offline'||state==='unreachable')return false;
  if(state!=='online')return null;
  const parents=model.edges.filter(edge=>edge.target===id);
  if(!parents.length)return true;
  const next=new Set(visiting);next.add(id);
  const results=parents.map(edge=>routeEvidence(edge.source,next));
  return results.includes(true)?true:results.includes(null)?null:false;
}
const displayRender = render;
const displayEdges = edges;
function paintEdges(){

  const groups=[...document.querySelectorAll('#edgePaths .edge-group:not(.preview-edge)')];
  model.edges.forEach((edge,index)=>{
    const group=groups[index],a=model.devices.find(d=>d.id===edge.source),b=model.devices.find(d=>d.id===edge.target);
    if(!group||!a||!b)return;
    const destination=visualState(b.ip),origin=visualState(a.ip);
    const status=origin==='offline'||destination==='offline'?'offline':
      origin==='unreachable'||destination==='unreachable'?'unreachable':
      origin==='online'&&destination==='online'&&routeEvidence(a.id)===false?'inconsistent':
      origin==='online'&&destination==='online'?'online':
      origin==='pending'||destination==='pending'?'pending':'unknown';
    if(group.dataset.monitorState===status)return;
    group.classList.remove('state-online','state-offline','state-pending','state-unreachable','state-inconsistent','state-unknown');
    group.classList.add('state-'+status);
    group.dataset.monitorState=status;
    group.querySelector('.flow-packet')?.remove();
    const path=group.querySelector('.edge');
    if(status==='online'||status==='offline'||status==='unreachable'||status==='inconsistent')
      createLinkPacket(group,path,status);
  });
}
edges=function(){
  displayEdges();
  paintEdges();
};
function paintNodes(){

  const nodes=new Map([...document.querySelectorAll('#nodes .node')].map(node=>[node.dataset.id,node]));
  model.devices.forEach(device=>{
    const node=nodes.get(device.id);
    if(!node)return;
    const status=visualState(device.ip);
    const routeWarning=status==='online'&&routeEvidence(device.id)===false;
    node.classList.remove('state-online','state-offline','state-pending','state-unreachable','state-unknown');
    node.classList.add('state-'+status);
    node.classList.toggle('route-warning',routeWarning);
    const label=node.querySelector('.node-state');
    label.textContent=routeWarning?'● IP responde · revisar ruta':labels[status];
    label.title=routeWarning?routeWarningLabel:labels[status];
  });
  const selectedDevice=model.devices.find(d=>d.id===selected);
  if(selectedDevice)$('#detailStatus').textContent=
    visualState(selectedDevice.ip)==='online'&&routeEvidence(selectedDevice.id)===false?
      routeWarningLabel:labels[visualState(selectedDevice.ip)];
}
render=function(){
  displayRender();
  paintNodes();
  paintEdges();
};
function showHealth(){
  const condition=effectiveCondition();
  const title=document.querySelector('.monitor-status b');
  const subtitle=document.querySelector('.monitor-status small');
  const stamp=monitorCheckedAt?VisionTime.time(monitorCheckedAt):'sin revisión';
  if(condition==='operating'){
    title.textContent=incidentDetectionEnabled?'Monitoreo activo':'Observación de IPs';
    subtitle.textContent='Última revisión '+stamp;
    const mismatches=model.devices.filter(d=>visualState(d.ip)==='online'&&routeEvidence(d.id)===false);
    const mismatchText=mismatches.length===1?
      `${mismatches[0].name} responde al ping, pero su ruta dibujada no responde. Revisa si los enlaces representan la conexión física.`:
      `${mismatches.length} equipos responden al ping, pero sus rutas dibujadas no responden. Revisa las conexiones físicas.`;
    monitorBand.textContent=(incidentDetectionEnabled?'':'Modo de observación: pings visibles, incidentes y avisos desactivados. ')+
      (mismatches.length?mismatchText:'');
    monitorBand.hidden=(incidentDetectionEnabled&&mismatches.length===0);
  }else if(condition==='no-reachability'){
    title.textContent='Sin acceso a la red';subtitle.textContent='Última revisión '+stamp;
    monitorBand.textContent='Sin respuesta de la red desde esta PC. No se declaran caídas individuales.';
    monitorBand.hidden=false;
  }else{
    title.textContent='Sin datos recientes';subtitle.textContent='Revisa el monitor';
    monitorBand.textContent='No hay comprobaciones recientes. Los estados se muestran como desconocidos.';
    monitorBand.hidden=false;
  }
  document.querySelector('.monitor-status .status-orb').className='status-orb monitor-'+condition;
}
async function loadMonitor(){
  try{
    const response=await fetch('/api/status',{cache:'no-store'});
    if(!response.ok)throw Error('Monitor no disponible');
    const data=await response.json();
    monitorCondition=data.condition;monitorCheckedAt=data.checkedAtUtc;
    incidentDetectionEnabled=data.incidentDetectionEnabled===true;
    monitored.clear();for(const item of data.devices||[])monitored.set(item.ip,item);
    $('#onlineCount').textContent=data.devices.filter(d=>d.status==='online').length;
    $('#offlineCount').textContent=data.devices.filter(d=>d.status==='offline').length;

  }catch{monitorCondition='probe-error';monitorCheckedAt=null;incidentDetectionEnabled=false;monitored.clear();}
  showHealth();
  paintNodes();paintEdges();
}
loadMonitor();setInterval(loadMonitor,5000);
