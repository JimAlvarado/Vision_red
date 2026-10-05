const screenRoot=document.documentElement;
const screenButton=document.getElementById('focusMode');
function setMonitorScreen(active){
  document.body.classList.toggle('focus-map',active);
  screenButton.setAttribute('aria-pressed',String(active));
  screenButton.textContent=active?'⛶ Salir de pantalla completa':'⛶ Pantalla completa';
  if(active){document.getElementById('inspector').classList.remove('show');document.querySelectorAll('dialog[open]').forEach(dialog=>dialog.close());}
}
screenButton.onclick=async()=>{
  if(document.fullscreenElement){await document.exitFullscreen();return;}
  if(document.body.classList.contains('focus-map')){setMonitorScreen(false);return;}
  setMonitorScreen(true);
  try{await screenRoot.requestFullscreen();}catch{}
};
document.addEventListener('fullscreenchange',()=>setMonitorScreen(document.fullscreenElement===screenRoot));
document.addEventListener('keydown',async event=>{
  if(event.key!=='Escape'||!document.body.classList.contains('focus-map'))return;
  setMonitorScreen(false);
  if(document.fullscreenElement)try{await document.exitFullscreen();}catch{}
});