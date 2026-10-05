const $=s=>document.querySelector(s), svgNS='http://www.w3.org/2000/svg';
let model={devices:[],edges:[],revision:0}, selected=null, source=null, scale=1, pan={x:20,y:20}, gesture=null, change=0, saved=0, saving=false, failed=false, ready=false;
const icons={switch:'<rect x="3" y="7" width="26" height="18" rx="3"/><path d="M7 12h3m4 0h3m4 0h3M7 18h3m4 0h3m4 0h3"/>',camera:'<rect x="3" y="8" width="19" height="17" rx="4"/><path d="m22 13 7-4v15l-7-4M9 8l2-4h5l2 4"/>',server:'<rect x="6" y="3" width="20" height="26" rx="3"/><path d="M6 12h20M6 21h20M10 7h2m-2 9h2m-2 9h2"/>',router:'<rect x="4" y="15" width="24" height="12" rx="3"/><path d="M9 15V5m14 10V5M9 21h3m7 0h3"/>',printer:'<path d="M9 11V3h14v8M8 23H3V11h26v12h-5M9 19h14v10H9zM23 15h2"/>',other:'<rect x="3" y="4" width="26" height="19" rx="3"/><path d="M16 23v6m-7 0h14"/>'};
const icon=t=>`<svg viewBox="0 0 32 32" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">${icons[t]||icons.other}</svg>`;
const toast=(message)=>{$('#toast').textContent=message;$('#toast').hidden=false;clearTimeout(toast.timer);toast.timer=setTimeout(()=>$('#toast').hidden=true,4500);};
function saveStatus(text){$('#saveState').textContent=text;}
function changed(){change++;saveStatus('Cambios pendientes');clearTimeout(changed.timer);changed.timer=setTimeout(persist,300);}
async function persist(){
 if(saving||saved===change||failed)return;
 saving=true;saveStatus('Guardando…');const version=change;
 try{const response=await fetch('/api/topology',{method:'PUT',headers:{'Content-Type':'application/json','X-Topology-Editor':'1'},body:JSON.stringify(model)});const result=await response.json();if(!response.ok)throw Error(result.error||'No se pudo guardar');model.revision=result.revision;saved=version;saveStatus(saved===change?'Guardado en esta PC':'Cambios pendientes');}
 catch(e){failed=true;saveStatus('Sin guardar · clic para reintentar');toast(e.message);}
 finally{saving=false;if(!failed&&saved!==change)persist();}
}
$('#saveState').onclick=()=>{failed=false;persist();};
window.addEventListener('beforeunload',e=>{if(saved!==change){e.preventDefault();e.returnValue='';}});
function saveView(){if(!ready)return;try{sessionStorage.setItem('vision-map-view',JSON.stringify({scale,pan}));}catch{}}
function restoreView(){try{const view=JSON.parse(sessionStorage.getItem('vision-map-view'));if(view&&Number.isFinite(view.scale)&&view.scale>=.15&&view.scale<=2&&Number.isFinite(view.pan?.x)&&Number.isFinite(view.pan?.y)){scale=view.scale;pan={...view.pan};}}catch{}}
function transform(){$('#world').style.transform=`translate(${pan.x}px,${pan.y}px) scale(${scale})`;$('#zoomLabel').textContent=Math.round(scale*100)+'%';saveView();}
window.addEventListener('pagehide',saveView);
window.addEventListener('pageshow',()=>{gesture=null;if(ready){restoreView();transform();}});
function edges(){const root=$('#edgePaths');root.replaceChildren();for(const edge of model.edges){const a=model.devices.find(d=>d.id===edge.source),b=model.devices.find(d=>d.id===edge.target);if(!a||!b)continue;const x=a.x+226,y=a.y+49,x2=b.x,y2=b.y+49,curve=Math.max(85,Math.abs(x2-x)*.45);const path=`M${x},${y} C${x+curve},${y} ${x2-curve},${y2} ${x2},${y2}`;const g=document.createElementNS(svgNS,'g');g.classList.add('edge-group','state-unknown');for(const cls of ['edge','edge-hit']){const p=document.createElementNS(svgNS,'path');p.setAttribute('d',path);p.setAttribute('class',cls);g.append(p);}const title=document.createElementNS(svgNS,'title');title.textContent=`${a.name} → ${b.name}. Clic para eliminar conexión.`;g.append(title);g.addEventListener('pointerdown',e=>e.stopPropagation());g.onclick=()=>{if(confirm(`¿Eliminar la conexión de ${a.name} a ${b.name}?`)){model.edges=model.edges.filter(v=>v!==edge);changed();render();}};root.append(g);}}
function render(){
 const container=$('#nodes');container.replaceChildren();const q=$('#search').value.toLocaleLowerCase();
 for(const d of model.devices){const node=document.createElement('div');node.className='node state-unknown'+(selected===d.id?' selected':'')+(source===d.id?' connecting':'')+(q&&!`${d.name} ${d.ip}`.toLocaleLowerCase().includes(q)?' dim':'');node.dataset.id=d.id;node.style.left=d.x+'px';node.style.top=d.y+'px';node.tabIndex=0;node.setAttribute('aria-label',`${d.name}, ${d.ip}. Enter para editar.`);
 const glyph=document.createElement('div');glyph.className='node-icon';glyph.innerHTML=icon(d.type);node.append(glyph);
 const copy=document.createElement('div');copy.className='node-copy';for(const [cls,text] of [['node-name',d.name],['node-ip',d.ip],['node-state','● Sin datos']]){const el=document.createElement('div');el.className=cls;el.textContent=text;el.title=text;copy.append(el);}node.append(copy);
 if(d.critical){const critical=document.createElement('span');critical.className='critical-mark';critical.textContent='◆';critical.title='Equipo crítico';node.append(critical);}
 for(const direction of ['in','out']){const p=document.createElement('button');p.type='button';p.className=`port ${direction}`;p.title=direction==='out'?'Conectar desde este equipo':'Conectar hacia este equipo';p.setAttribute('aria-label',p.title+' '+d.name);p.onpointerdown=e=>e.stopPropagation();p.onclick=e=>{e.stopPropagation();if(direction==='out'){source=d.id;render();}else if(source){connect(source,d.id);}else toast('Primero selecciona la salida derecha del equipo de origen.');};node.append(p);}
 node.onpointerdown=e=>{if(e.button!==0||e.target.closest('button'))return;e.stopPropagation();select(d.id,false,false);gesture={type:'node',id:d.id,startX:e.clientX,startY:e.clientY,x:d.x,y:d.y,moved:false};};
 node.ondblclick=e=>{if(e.target.closest('button'))return;e.stopPropagation();select(d.id,false);};
 node.onkeydown=e=>{if(e.key==='Enter'){select(d.id);return;}const delta={ArrowLeft:[-10,0],ArrowRight:[10,0],ArrowUp:[0,-10],ArrowDown:[0,10]}[e.key];if(delta){e.preventDefault();d.x+=delta[0];d.y+=delta[1];node.style.left=d.x+'px';node.style.top=d.y+'px';edges();changed();}};
 container.append(node);
 }
 edges();$('#count').textContent=model.devices.length;$('#empty').hidden=model.devices.length>0;inspector();
}
function select(id,redraw=true,openDetail=true){selected=id;if(openDetail)$('#inspector').classList.add('show');if(redraw)render();else{document.querySelectorAll('.node').forEach(el=>el.classList.toggle('selected',el.dataset.id===id));inspector();}}
function inspector(){const d=model.devices.find(v=>v.id===selected);$('#noSelection').hidden=!!d;$('#deviceForm').hidden=!d;if(!d)return;
 const f=$('#deviceForm');for(const key of ['name','ip','type','note'])f.elements[key].value=d[key]||'';f.elements.critical.checked=d.critical;$('#detailTitle').textContent=d.name;$('#detailIcon').innerHTML=icon(d.type);
 $('#incoming').replaceChildren();const incoming=model.edges.filter(e=>e.target===d.id);if(!incoming.length){const p=document.createElement('p');p.className='form-note';p.textContent='Sin conexiones entrantes.';$('#incoming').append(p);}for(const edge of incoming){const row=document.createElement('div');row.className='connection-item';const text=document.createElement('span');text.textContent=model.devices.find(v=>v.id===edge.source)?.name;const remove=document.createElement('button');remove.type='button';remove.textContent='×';remove.title='Eliminar conexión';remove.onclick=()=>{model.edges=model.edges.filter(e=>e!==edge);changed();render();};row.append(text,remove);$('#incoming').append(row);}
 const select=$('#parentSelect');select.replaceChildren();for(const device of model.devices.filter(v=>v.id!==d.id&&!incoming.some(e=>e.source===v.id))){const option=document.createElement('option');option.value=device.id;option.textContent=device.name;select.append(option);}$('#connectParent').disabled=!select.options.length;
}
function connect(a,b){if(a===b){toast('Elige otro equipo como destino.');return;}if(model.edges.some(e=>e.source===a&&e.target===b)){toast('Esa conexión ya existe.');return;}model.edges.push({source:a,target:b});source=null;changed();render();}
$('#connectParent').onclick=()=>connect($('#parentSelect').value,selected);
$('#closeInspector').onclick=()=>{selected=null;$('#inspector').classList.remove('show');render();};
$('#canvas').onpointerdown=e=>{if(e.button!==0||e.target.closest('button,.node,.edge-group'))return;gesture={type:'pan',startX:e.clientX,startY:e.clientY,x:pan.x,y:pan.y};};
window.addEventListener('pointermove',e=>{if(!gesture)return;const dx=e.clientX-gesture.startX,dy=e.clientY-gesture.startY;if(gesture.type==='pan'){pan={x:gesture.x+dx,y:gesture.y+dy};transform();}else{const d=model.devices.find(v=>v.id===gesture.id);if(!d)return;d.x=gesture.x+dx/scale;d.y=gesture.y+dy/scale;gesture.moved=gesture.moved||Math.abs(dx)+Math.abs(dy)>2;const node=document.querySelector(`.node[data-id="${d.id}"]`);node.style.left=d.x+'px';node.style.top=d.y+'px';edges();}});
window.addEventListener('pointerup',()=>{if(gesture?.type==='node'&&gesture.moved)changed();gesture=null;});
window.addEventListener('blur',()=>{if(gesture?.type==='node'&&gesture.moved)changed();gesture=null;});
function zoom(next,x,y){next=Math.min(2,Math.max(.15,next));pan.x=x-(x-pan.x)*next/scale;pan.y=y-(y-pan.y)*next/scale;scale=next;transform();}
$('#canvas').addEventListener('wheel',e=>{e.preventDefault();const box=$('#canvas').getBoundingClientRect();zoom(scale*Math.exp(-e.deltaY*.001),e.clientX-box.left,e.clientY-box.top);},{passive:false});
$('#zoomIn').onclick=()=>zoom(scale*1.2,$('#canvas').clientWidth/2,$('#canvas').clientHeight/2);
$('#zoomOut').onclick=()=>zoom(scale/1.2,$('#canvas').clientWidth/2,$('#canvas').clientHeight/2);
function fit(){if(!model.devices.length){pan={x:20,y:20};scale=1;transform();return;}const minX=Math.min(...model.devices.map(d=>d.x)),minY=Math.min(...model.devices.map(d=>d.y)),maxX=Math.max(...model.devices.map(d=>d.x+226)),maxY=Math.max(...model.devices.map(d=>d.y+98));scale=Math.max(.15,Math.min(1.2,($('#canvas').clientWidth-100)/(maxX-minX),($('#canvas').clientHeight-110)/(maxY-minY)));pan={x:($('#canvas').clientWidth-(maxX-minX)*scale)/2-minX*scale,y:($('#canvas').clientHeight-(maxY-minY)*scale)/2-minY*scale-15};transform();}
$('#fit').onclick=fit;$('#search').oninput=render;
$('#search').onkeydown=e=>{if(e.key==='Enter'){const q=e.target.value.toLocaleLowerCase();const d=model.devices.find(d=>`${d.name} ${d.ip}`.toLocaleLowerCase().includes(q));if(d){scale=1;pan={x:$('#canvas').clientWidth/2-d.x-113,y:$('#canvas').clientHeight/2-d.y-49};transform();select(d.id);}}};
window.addEventListener('keydown',e=>{if(e.key==='Escape'){source=null;render();}});
function validIp(value){if(value.includes(':')){try{return new URL(`http://[${value}]/`).hostname.length>0;}catch{return false;}}const p=value.split('.');return p.length===4&&p.every(v=>/^\d{1,3}$/.test(v)&&Number(v)<=255&&String(Number(v))===v);}
function check(name,ip,id){if(!name.trim()){toast('Escribe el nombre del equipo.');return false;}if(!validIp(ip)){toast('Escribe una dirección IP válida.');return false;}if(model.devices.some(d=>d.ip===ip&&d.id!==id)){toast('Esa IP ya está registrada.');return false;}return true;}
$('#deviceForm').onsubmit=e=>{e.preventDefault();const f=e.target,d=model.devices.find(v=>v.id===selected),name=f.elements.name.value.trim(),ip=f.elements.ip.value.trim();if(!d||!check(name,ip,d.id))return;Object.assign(d,{name,ip,type:f.elements.type.value,note:f.elements.note.value.trim(),critical:f.elements.critical.checked});changed();render();toast('Datos actualizados.');};
$('#deleteDevice').onclick=()=>{const d=model.devices.find(v=>v.id===selected);if(!d||!confirm(`¿Eliminar ${d.name} y sus conexiones del inventario visual?`))return;model.devices=model.devices.filter(v=>v.id!==d.id);model.edges=model.edges.filter(v=>v.source!==d.id&&v.target!==d.id);selected=null;changed();render();};
$('#addDevice').onclick=()=>{if(!ready)return;$('#addForm').reset();$('#addDialog').showModal();};$('#cancelAdd').onclick=()=>$('#addDialog').close();
$('#addForm').onsubmit=e=>{e.preventDefault();const f=e.target,name=f.elements.name.value.trim(),ip=f.elements.ip.value.trim();if(!check(name,ip))return;const d={id:crypto.randomUUID(),name,ip,type:f.elements.type.value,critical:f.elements.critical.checked,note:'',x:($('#canvas').clientWidth/2-pan.x)/scale-113,y:($('#canvas').clientHeight/2-pan.y)/scale-49};model.devices.push(d);selected=d.id;$('#addDialog').close();changed();render();$('#inspector').classList.add('show');toast('Equipo agregado. El monitor lo comprobará automáticamente.');};
async function init(){try{const response=await fetch('/api/topology');if(!response.ok)throw Error('No se pudo cargar el inventario');model=await response.json();ready=true;render();scale=1;pan={x:-45,y:-35};restoreView();transform();saveStatus('Guardado en esta PC');}catch(e){toast(e.message);saveStatus('Error al cargar');$('#addDevice').disabled=true;}}
init();


