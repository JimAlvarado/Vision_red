(() => {
  const panel=document.createElement('dialog');panel.id='eventsDialog';panel.setAttribute('aria-labelledby','eventsTitle');
  panel.innerHTML='<div class="events-heading"><div><span>VISION · HISTORIAL</span><h2 id="eventsTitle">Eventos de la red</h2></div><button type="button" aria-label="Cerrar eventos">×</button></div><p class="events-intro">Registro permanente en CSV · Horas de Ciudad de México · Actualización cada 5 segundos</p><form id="eventsFilters"><label>Buscar<input name="search" maxlength="200" placeholder="Equipo, IP o referencia"></label><label>Categoría<select name="category"><option value="">Todas</option><option value="monitor">Monitoreo</option><option value="incident">Incidentes</option><option value="email">Correo</option><option value="configuration">Configuración</option><option value="system">Sistema</option><option value="access">Accesos</option></select></label><label>Nivel<select name="severity"><option value="">Todos</option><option value="info">Información</option><option value="success">Recuperación / correcto</option><option value="warning">Revisión</option><option value="critical">Alerta</option></select></label><label>Desde<input type="date" name="since"></label><label>Hasta<input type="date" name="until"></label><button>Filtrar</button></form><p id="eventsStatus" role="status"></p><div id="eventsRows"></div><div class="events-pagination"><button id="eventsPrevious" type="button">Anterior</button><span id="eventsPage"></span><button id="eventsNext" type="button">Siguiente</button></div>';
  document.body.append(panel);
  const activeBox=document.createElement('section');activeBox.className='events-active';activeBox.hidden=true;panel.querySelector('#eventsStatus').before(activeBox);
  const filters=panel.querySelector('form'),rows=panel.querySelector('#eventsRows'),status=panel.querySelector('#eventsStatus');let page=1,busy=false,requestVersion=0;
  const names={monitor:'Monitoreo',incident:'Incidente',email:'Correo',configuration:'Configuración',system:'Sistema',access:'Acceso'};
  const levels={info:'Información',success:'Correcto',warning:'Revisión',critical:'Alerta'};
  const states={online:'IP responde',offline:'IP sin respuesta',pending:'Confirmando',unknown:'Sin datos',unreachable:'Acceso sin confirmar','awaiting-configuration':'Pendiente de envío',sending:'Enviando',accepted:'Aceptado por Microsoft',retry:'Reintento programado',failed:'Falló',expired:'Caducado',connected:'Sesión iniciada',disconnected:'Sesión cerrada'};
  function text(tag,value,className){const el=document.createElement(tag);el.textContent=value;if(className)el.className=className;return el;}
  async function load(){
    if(busy)return;busy=true;const version=requestVersion;
    try{
      const query=new URLSearchParams({page:String(page)});for(const key of ['search','category','severity'])if(filters.elements[key].value)query.set(key,filters.elements[key].value);
      for(const key of ['since','until'])if(filters.elements[key].value)query.set(key,new Date(filters.elements[key].value+(key==='since'?'T00:00:00-06:00':'T23:59:59.999-06:00')).toISOString());
      const [response,incidentResponse]=await Promise.all([fetch('/api/events?'+query,{cache:'no-store',credentials:'same-origin'}),fetch('/api/incidents',{cache:'no-store',credentials:'same-origin'})]);
      if(!response.ok)throw Error(response.status===401?'Introduce el código de acceso para consultar eventos.':'No se pudo consultar el historial.');
      if(!incidentResponse.ok)throw Error('No se pudo actualizar el estado de los incidentes.');
      const [data,incidents]=await Promise.all([response.json(),incidentResponse.json()]);if(version!==requestVersion)return;rows.replaceChildren();
      const active=incidents.filter(i=>!i.closedAtUtc),featured=active.length?active:[...incidents].sort((a,b)=>Date.parse(b.openedAtUtc)-Date.parse(a.openedAtUtc)).slice(0,1);activeBox.replaceChildren();activeBox.hidden=!featured.length;
      if(featured.length){activeBox.append(text('h3',active.length?`Desconexión confirmada · ${active.length} incidente${active.length===1?'':'s'} activo${active.length===1?'':'s'}`:'Última desconexión registrada · Recuperado'));
        for(const incident of featured){const entry=document.createElement('div');entry.append(text('strong',`${incident.name} · ${incident.ip==='monitor-access'?'Acceso a la red':incident.ip}`),text('p','Desconexión: '+VisionTime.dateTime(incident.openedAtUtc)));if(incident.closedAtUtc)entry.append(text('p','Reconexión: '+VisionTime.dateTime(incident.closedAtUtc)));else entry.append(text('p','Estado: sin respuesta'));activeBox.append(entry);}
        activeBox.append(text('small','Estado actual del monitor. El historial y sus filtros se muestran debajo.'));}
      for(const event of data.items){
        const row=document.createElement('article');row.className='event-entry '+event.severity;
        const head=document.createElement('div');head.className='event-entry-heading';head.append(text('strong',event.deviceName||names[event.category]||'Vision'),text('span',levels[event.severity]||event.severity,'event-level'));row.append(head);
        const descriptions={Success:'Respuesta de conectividad recibida.',TimedOut:'La IP no respondió dentro del tiempo de espera.',DestinationHostUnreachable:'No se pudo alcanzar el equipo desde el monitor.'};
        const labels={device_down:'DESCONEXIÓN CONFIRMADA',network_down:'PÉRDIDA DE ACCESO A LA RED',device_recovery:'RECONEXIÓN CONFIRMADA',network_recovery:'ACCESO A LA RED RESTABLECIDO'};
        if(labels[event.kind])row.append(text('strong',labels[event.kind],'event-kind'));
        row.append(text('p',descriptions[event.description]||event.description));
        const time=VisionTime.dateTime(event.occurredAtUtc);
        row.append(text('small',`${time} · ${names[event.category]||event.category}${event.deviceIp?' · '+(event.deviceIp==='monitor-access'?'Acceso a la red':event.deviceIp):''}`));
        if(event.previousState||event.currentState)row.append(text('div',`${states[event.previousState]||event.previousState||'Inicio'} → ${states[event.currentState]||event.currentState}`,'event-transition'));
        if(event.referenceId)row.append(text('small','Referencia: '+event.referenceId,'event-reference'));rows.append(row);
      }
      if(!data.items.length)rows.append(text('p','No hay eventos que coincidan con estos filtros.'));
      status.textContent=`${data.total} eventos encontrados · Datos actualizados`;
      panel.querySelector('#eventsPage').textContent=`Página ${data.page} de ${Math.max(1,Math.ceil(data.total/data.pageSize))}`;
      panel.querySelector('#eventsPrevious').disabled=page===1;panel.querySelector('#eventsNext').disabled=page*data.pageSize>=data.total;
    }catch(error){status.textContent=error.message;}finally{busy=false;if(version!==requestVersion&&panel.open)load();}
  }
  document.addEventListener('vision-open-events',()=>{if(!panel.open)panel.showModal();load();});
  panel.querySelector('[aria-label="Cerrar eventos"]').onclick=()=>panel.close();
  filters.onsubmit=e=>{e.preventDefault();page=1;requestVersion++;load();};
  panel.querySelector('#eventsPrevious').onclick=()=>{page=Math.max(1,page-1);requestVersion++;load();};
  panel.querySelector('#eventsNext').onclick=()=>{page++;requestVersion++;load();};
  setInterval(()=>{if(panel.open&&!document.hidden)load();},5000);
})();
