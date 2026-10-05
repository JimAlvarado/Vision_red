(() => {
  const base={timeZone:'America/Mexico_City',hour:'2-digit',minute:'2-digit',second:'2-digit',hour12:true};
  const clock=new Intl.DateTimeFormat('es-MX',base);
  const stamp=new Intl.DateTimeFormat('es-MX',{...base,day:'2-digit',month:'2-digit',year:'numeric'});
  function format(formatter,value){const date=new Date(value);if(!Number.isFinite(date.getTime()))return '—';return formatter.formatToParts(date).map(part=>part.type==='dayPeriod'?part.value.replace(/[.\s]/g,'').toUpperCase():part.value).join('').replace(/[\u00a0\u202f]/g,' ');}
  window.VisionTime={time:value=>format(clock,value),dateTime:value=>format(stamp,value)};
})();
