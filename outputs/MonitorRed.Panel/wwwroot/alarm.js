(() => {
  const banner=document.createElement('section');banner.className='vision-alarm';banner.hidden=true;banner.setAttribute('role','alert');banner.setAttribute('aria-live','assertive');
  banner.innerHTML='<div class="vision-alarm-top"><svg class="vision-alarm-symbol" viewBox="0 0 48 48" fill="none" aria-hidden="true"><path d="M21 7a3.5 3.5 0 0 1 6 0l18 31a3.5 3.5 0 0 1-3 5H6a3.5 3.5 0 0 1-3-5Z" fill="currentColor" fill-opacity=".08" stroke="currentColor" stroke-width="1.8"/><path d="M24 17v12" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"/><circle cx="24" cy="35" r="1.7" fill="currentColor"/></svg><div><span class="vision-alarm-caption"></span><h2></h2></div></div><p class="vision-alarm-description"></p><ul></ul><button type="button">Reconocer y silenciar</button><p class="vision-alarm-muted"></p>';
  document.body.append(banner);
  const acknowledgeButton=banner.querySelector('button');
  const caption=banner.querySelector('.vision-alarm-caption'), heading=banner.querySelector('h2'), description=banner.querySelector('.vision-alarm-description'), list=banner.querySelector('ul'), note=banner.querySelector('.vision-alarm-muted');
  let audio=null, enabled=true, active=[], demoUntil=0, polling=false,lastSignature='';
  let nativeSound=false, soundKnown=location.port!=='5080';
  const soundQueue=new Map();let nextToneAt=0;
  const sounded=new Set();try{for(const id of JSON.parse(localStorage.getItem('vision-played-tones')||'[]'))sounded.add(id);}catch{}
  let retained=new Map();
  try{for(const item of JSON.parse(localStorage.getItem('vision-pending-alerts')||'[]'))if(item && typeof item.id==='string' && typeof item.name==='string' && typeof item.ip==='string')retained.set(item.id,item);}catch{}
  function saveRetained(){try{localStorage.setItem('vision-pending-alerts',JSON.stringify([...retained.values()]));}catch{}}
  let paintPending=false,postingTone=false;
  function requestTone(key,frequency,expires=Date.now()+120000){if(!sounded.has(key))soundQueue.set(key,{frequency,expires});}
  function flushTones(){
    if(!soundKnown || paintPending || postingTone || document.hidden || !soundQueue.size)return;
    paintPending=true;
    // Commit the visible notice before audio begins, including fullscreen.
    requestAnimationFrame(()=>requestAnimationFrame(()=>{paintPending=false;playNextTone();}));
  }
  async function playNextTone(){
    if(postingTone || document.hidden)return;
    for(const [key,tone] of soundQueue){
      if(tone.expires<Date.now()){soundQueue.delete(key);continue;}
      if((key.startsWith('recovery:')?recovery:banner).hidden)continue;
      if(!nativeSound && (audio?.state!=='running' || Date.now()<nextToneAt))return;
      if(nativeSound){
        postingTone=true;
        try{const [kind,id]=key.split(':');const response=await fetch('/api/sound/announce',{method:'POST',headers:{'Content-Type':'application/json','X-Topology-Editor':'1'},body:JSON.stringify({id,kind})});if(!response.ok || !(await response.json()).handled)return;}
        catch{return;}finally{postingTone=false;}
      }else if(!beep(tone.frequency))return;
      sounded.add(key);soundQueue.delete(key);try{localStorage.setItem('vision-played-tones',JSON.stringify([...sounded].slice(-600)));}catch{}
      nextToneAt=Date.now()+600;if(!nativeSound)break;
    }
  }
  let acknowledgments=new Set();
  function readAcknowledgments(){try{const saved=JSON.parse(localStorage.getItem('vision-acknowledged-incidents')||'[]');if(Array.isArray(saved))acknowledgments=new Set(saved.filter(v=>typeof v==='string'));}catch{}}
  readAcknowledgments();
  function beep(frequency=880){
    if(!enabled || audio?.state!=='running')return false;
    const now=audio.currentTime;
    const oscillator=audio.createOscillator(),gain=audio.createGain();oscillator.type='sine';oscillator.frequency.value=frequency;gain.gain.setValueAtTime(0,now);gain.gain.linearRampToValueAtTime(.45,now+.02);gain.gain.setValueAtTime(.45,now+.38);gain.gain.linearRampToValueAtTime(0,now+.5);oscillator.connect(gain);gain.connect(audio.destination);oscillator.start(now);oscillator.stop(now+.51);oscillator.onended=()=>{oscillator.disconnect();gain.disconnect();};return true;
  }
  const recovery=document.createElement('section');recovery.className='vision-alarm vision-recovery';recovery.hidden=true;recovery.setAttribute('role','status');recovery.setAttribute('aria-live','polite');
  recovery.innerHTML='<div class="vision-alarm-top"><svg class="vision-alarm-symbol" viewBox="0 0 48 48" fill="none" aria-hidden="true"><circle cx="24" cy="24" r="20" fill="currentColor" fill-opacity=".08" stroke="currentColor" stroke-width="1.8"/><path d="m14 24 7 7 14-15" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/></svg><div><span class="vision-alarm-caption"></span><h2>Conectividad restablecida</h2></div></div><p class="vision-alarm-description"></p><ul></ul><button type="button">Cerrar aviso</button><p class="vision-alarm-muted">La recuperación quedó registrada. Consulta Correo para comprobar el envío.</p>';
  document.body.append(recovery);
  let recoveryUntil=0;
  const recoveredIds=new Set();try{for(const id of JSON.parse(sessionStorage.getItem('vision-seen-recoveries')||'[]'))recoveredIds.add(id);}catch{}
  function showRecovery(items,demo=false){
    recoveryUntil=Date.now()+15000;recovery.hidden=false;
    recovery.querySelector('.vision-alarm-caption').textContent=demo?'DEMOSTRACIÓN · SIN ENVÍO':'VISION · RECUPERACIÓN CONFIRMADA';
    recovery.querySelector('.vision-alarm-description').textContent=demo?'Ejemplo de reconexión. Esta demostración no cambia equipos ni envía correos.':'El monitor confirmó que volvió la respuesta de conectividad. El aviso rojo permanece hasta reconocerlo manualmente.';
    const rows=recovery.querySelector('ul');rows.replaceChildren();
    for(const incident of items){const row=document.createElement('li'),name=document.createElement('strong');name.textContent=incident.name;
      const seconds=Math.max(0,Math.round((Date.parse(incident.closedAtUtc)-Date.parse(incident.openedAtUtc))/1000));
      const duration=Number.isFinite(seconds)?`${Math.floor(seconds/60)} min ${seconds%60} s`:'ejemplo';
      row.append(name,document.createTextNode(` · ${incident.ip==='monitor-access'?'Acceso a la red':incident.ip} · Duración: ${duration}`));rows.append(row);
    }
    recovery.querySelector('.vision-alarm-muted').hidden=demo;
    if(demo)beep(880);else for(const incident of items)requestTone('recovery:'+incident.id,880,Date.parse(incident.closedAtUtc)+120000);
    flushTones();
  }
  recovery.querySelector('button').onclick=()=>{recoveryUntil=0;recovery.hidden=true;};
  function update(){
    const login=document.getElementById('login');if(login && !login.hidden){banner.hidden=true;recovery.hidden=true;return;}
    const demo=Date.now()<demoUntil, pending=[...retained.values()].filter(i=>!acknowledgments.has(i.id));
    const showing=demo || pending.length>0;banner.hidden=!showing;if(!showing){lastSignature='';return;}
    const signature=demo?'demo':pending.map(i=>i.id+':'+(i.closedAtUtc||'')).sort().join('|');
    if(signature!==lastSignature){
      caption.textContent=demo?'DEMOSTRACIÓN · SIN ENVÍO':'VISION · ALERTA DE CONECTIVIDAD';
      heading.textContent=demo?'Ejemplo de alerta':pending.every(i=>i.closedAtUtc)?'Pérdida pendiente de reconocer':pending.some(i=>i.ip==='monitor-access' && !i.closedAtUtc)?'Acceso a la red interrumpido':pending.length===1?'Equipo sin respuesta':`${pending.length} avisos de pérdida`;
      description.textContent=demo?'Así se mostrará una pérdida confirmada. Esta prueba no cambia equipos ni envía correos.':'Pérdida de respuesta confirmada. Este aviso permanece hasta que lo cierres manualmente, aunque la conectividad se restablezca.';
      list.replaceChildren();
      for(const incident of demo?[{name:'Switch de ejemplo',ip:'EJEMPLO'}]:pending){const item=document.createElement('li');const name=document.createElement('strong');name.textContent=incident.name;item.append(name,document.createTextNode((incident.ip==='monitor-access'?' · Acceso a la red':` · ${incident.ip}`)+(incident.closedAtUtc?' · Recuperado; pendiente de reconocer':'')));list.append(item);}
      acknowledgeButton.textContent=demo?'Cerrar demostración':'Reconocer y cerrar';lastSignature=signature;
      if(demo)beep();
    }
    note.textContent=nativeSound?'Un pitido desde Windows por evento · Reconocer no modifica el historial ni el correo.':enabled && audio?.state==='running'?'Un pitido por evento · Reconocer no modifica el historial ni el correo.':'Toca o haz clic en esta página para permitir el sonido del navegador. Comprueba el volumen del dispositivo.';
    if(!demo)flushTones();
  }
  async function unlockAudio(){
    try{if(nativeSound)return;const Audio=window.AudioContext||window.webkitAudioContext;if(!Audio)return;if(!audio){audio=new Audio();audio.addEventListener('statechange',()=>{flushTones();update();});}if(audio.state!=='running')await audio.resume();flushTones();update();}catch{note.textContent='El navegador bloqueó el sonido. Toca la página y comprueba el volumen.';}
  }
  for(const type of ['click','pointerup','touchend','keydown'])document.addEventListener(type,unlockAudio,{passive:true});
  if(!soundKnown)fetch('/api/sound',{cache:'no-store'}).then(r=>r.ok?r.json():{}).then(s=>{nativeSound=s.available===true;}).catch(()=>{}).finally(()=>{soundKnown=true;unlockAudio();flushTones();update();});else unlockAudio();
  acknowledgeButton.onclick=()=>{if(Date.now()<demoUntil)demoUntil=0;else{for(const incident of retained.values()){acknowledgments.add(incident.id);soundQueue.delete('down:'+incident.id);}retained.clear();saveRetained();try{localStorage.setItem('vision-acknowledged-incidents',JSON.stringify([...acknowledgments]));}catch{}}update();};
  async function poll(){
    if(polling)return;
    // No protected-data calls before the mobile sign-in is complete.
    const login=document.getElementById('login');if(login && !login.hidden){active=[];banner.hidden=true;recovery.hidden=true;return;}
    polling=true;
    try{const response=await fetch('/api/incidents',{cache:'no-store'});if(response.ok){const incidents=await response.json();active=incidents.filter(i=>!i.closedAtUtc);
      for(const incident of incidents){if(!incident.closedAtUtc && !acknowledgments.has(incident.id)){retained.set(incident.id,incident);requestTone('down:'+incident.id,880);}else if(retained.has(incident.id))retained.set(incident.id,incident);}
      saveRetained();update();
      const fresh=incidents.filter(i=>i.closedAtUtc && !recoveredIds.has(i.id) && Date.now()-Date.parse(i.closedAtUtc)>=0 && Date.now()-Date.parse(i.closedAtUtc)<120000);
      if(fresh.length){for(const incident of fresh)recoveredIds.add(incident.id);try{sessionStorage.setItem('vision-seen-recoveries',JSON.stringify([...recoveredIds].slice(-300)));}catch{}showRecovery(fresh);}
    }else if(response.status===401){active=[];banner.hidden=true;recovery.hidden=true;}}
    catch{if(!banner.hidden)note.textContent='No se pudo actualizar. Se conserva el último aviso; revisa la conexión.';}
    finally{polling=false;}
  }
  window.addEventListener('storage',e=>{if(e.key==='vision-acknowledged-incidents'){readAcknowledgments();for(const id of acknowledgments)retained.delete(id);update();}if(e.key==='vision-played-tones'){try{for(const id of JSON.parse(e.newValue||'[]')){sounded.add(id);soundQueue.delete(id);}}catch{}}});
  document.addEventListener('visibilitychange',()=>{if(!document.hidden){unlockAudio();poll();}});
  poll();setInterval(poll,5000);setInterval(()=>{const login=document.getElementById('login');if(!login || login.hidden){update();flushTones();}if(Date.now()>=recoveryUntil)recovery.hidden=true;},1000);
})();
