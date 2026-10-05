(() => {
  const canvas = document.createElement('canvas'); canvas.className = 'vision-particle-canvas'; canvas.setAttribute('aria-hidden','true'); document.body.prepend(canvas);
  const context = canvas.getContext('2d'); if (!context) return;
  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  function movementEnabled(){return true;}
  let width = 0, height = 0, points = [], frame = 0, last = 0, cursor = null, exclusion = null;
  function resize() {
    width = innerWidth; height = innerHeight; const ratio = Math.min(devicePixelRatio || 1, 2);
    canvas.width = Math.round(width * ratio); canvas.height = Math.round(height * ratio); context.setTransform(ratio,0,0,ratio,0,0);
    const count = width < 600 ? 28 : Math.min(78, Math.round(width * height / 21000));
    points = Array.from({length:count}, () => ({x:Math.random()*width,y:Math.random()*height,vx:(Math.random()-.5)*.45,vy:(Math.random()-.5)*.45}));
    measure(); if (!movementEnabled()) draw(0);
  }
  function measure() { exclusion = document.querySelector('#canvas, #mapFrame')?.getBoundingClientRect(); }
  function draw(step) {
    context.clearRect(0,0,width,height);
    const reach = width < 600 ? 115 : 150;
    const activeCursor = cursor && performance.now() - cursor.at < 1600;
    for (const p of points) {
      if (step) {
        p.x += p.vx * step; p.y += p.vy * step;
        if (activeCursor) { const dx = cursor.x-p.x, dy = cursor.y-p.y, distance = Math.hypot(dx,dy); if(distance>12 && distance<230) { const pull=(1-distance/230)*.018*step; p.x+=dx*pull; p.y+=dy*pull; } }
        if(p.x<0) p.x=width; if(p.x>width) p.x=0; if(p.y<0) p.y=height; if(p.y>height) p.y=0;
      }
    }
    for(let i=0;i<points.length;i++) {
      const p=points[i];
      for(let j=i+1;j<points.length;j++) { const q=points[j], distance=Math.hypot(p.x-q.x,p.y-q.y); if(distance<reach) { context.strokeStyle=`rgba(105,224,208,${.36*(1-distance/reach)})`; context.lineWidth=.9; context.beginPath(); context.moveTo(p.x,p.y); context.lineTo(q.x,q.y); context.stroke(); } }
      if(activeCursor) { const distance=Math.hypot(p.x-cursor.x,p.y-cursor.y); if(distance<reach) { context.strokeStyle=`rgba(126,235,218,${.46*(1-distance/reach)})`; context.beginPath();context.moveTo(p.x,p.y);context.lineTo(cursor.x,cursor.y);context.stroke(); } }
      context.fillStyle='rgba(126,235,218,.82)';context.beginPath();context.arc(p.x,p.y,2,0,Math.PI*2);context.fill();
    }
    // Clear the actual topology region, including its border, on every frame.
    if(exclusion?.width && exclusion?.height) context.clearRect(exclusion.left-2,exclusion.top-2,exclusion.width+4,exclusion.height+4);
  }
  function tick(now) { frame=0; if(document.hidden || !movementEnabled()) return; if(now-last>=33) { const step=Math.min((now-last)/16.67,3);last=now;measure();draw(step); } frame=requestAnimationFrame(tick); }
  function resume() { cancelAnimationFrame(frame);frame=0;last=performance.now();measure(); if(!movementEnabled()) draw(0); else if(!document.hidden) frame=requestAnimationFrame(tick); }
  document.addEventListener('vision-motion-change',resume);
  window.addEventListener('storage',e=>{if(e.key==='vision-particle-motion')resume();});
  document.addEventListener('pointermove',e=>{cursor={x:e.clientX,y:e.clientY,at:performance.now()};},{passive:true});
  document.addEventListener('pointerdown',e=>{cursor={x:e.clientX,y:e.clientY,at:performance.now()};},{passive:true});
  document.addEventListener('pointerleave',()=>{cursor=null;});
  document.addEventListener('visibilitychange',resume);window.addEventListener('pageshow',resume);window.addEventListener('pagehide',()=>cancelAnimationFrame(frame));
  window.addEventListener('resize',()=>{resize();resume();});window.addEventListener('scroll',measure,{passive:true});reduced.addEventListener?.('change',resume);
  resize();resume();
})();
