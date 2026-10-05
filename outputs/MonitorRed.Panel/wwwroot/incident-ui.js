const incidentButton=document.createElement('button');
incidentButton.id='incidentButton';
incidentButton.type='button';
incidentButton.textContent='Incidentes 0';
$('#focusMode').before(incidentButton);
const incidentDialog=document.createElement('dialog');
incidentDialog.id='incidentDialog';
incidentDialog.innerHTML='<div class="dialog-title"><div><div class="eyebrow">VISION / HISTORIAL</div><h2>Incidentes</h2></div><button type="button" id="closeIncidents" aria-label="Cerrar incidentes">×</button></div><p>Caídas confirmadas y recuperaciones registradas por el monitor.</p><div id="incidentList"></div>';
document.body.append(incidentDialog);
$('#closeIncidents').onclick=()=>incidentDialog.close();
incidentButton.onclick=()=>{incidentDialog.showModal();loadIncidents();};
async function loadIncidents(){
  try{
    const response=await fetch('/api/incidents',{cache:'no-store'});
    if(!response.ok)throw Error('No se pudo leer el historial');
    const incidents=await response.json();
    const active=incidents.filter(i=>!i.closedAtUtc).length;
    $('#activeCount').textContent=active;
    incidentButton.textContent='Incidentes '+active;
    incidentButton.classList.toggle('has-incidents',active>0);
    const list=$('#incidentList');list.replaceChildren();
    if(!incidents.length){const empty=document.createElement('p');empty.textContent='Aún no hay caídas individuales confirmadas.';list.append(empty);return;}
    for(const incident of incidents.slice(0,30)){
      const row=document.createElement('div');row.className='incident-row';
      const label=document.createElement('strong');label.textContent=incident.name+' · '+incident.ip;
      const status=document.createElement('span');status.className=incident.closedAtUtc?'resolved':'active';status.textContent=incident.closedAtUtc?'Recuperado':'Activo';
      const time=document.createElement('small');time.textContent='Inicio: '+VisionTime.dateTime(incident.openedAtUtc)+(incident.closedAtUtc?' · Recuperación: '+VisionTime.dateTime(incident.closedAtUtc):'');
      row.append(label,status,time);list.append(row);
    }
  }catch{incidentButton.textContent='Incidentes —';}
}
loadIncidents();setInterval(loadIncidents,15000);
