// One SVG packet per link. Failed packets travel only to the midpoint and break apart.
function createLinkPacket(container, path, status) {
  const failed = status === 'offline' || status === 'unreachable' || status === 'inconsistent';
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const make = tag => document.createElementNS(svgNS, tag);
  const packet = make('g');
  packet.setAttribute('class', 'flow-packet');
  const dot = make('circle');
  dot.setAttribute('class', 'flow-dot');
  dot.setAttribute('r', '4.8');
  packet.append(dot);
  const length = path.getTotalLength();
  const midpoint = path.getPointAtLength(length / 2);
  if (reduced) {
    packet.setAttribute('transform', `translate(${midpoint.x} ${midpoint.y})`);
    container.append(packet);
    return packet;
  }
  const motion = make('animateMotion');
  motion.setAttribute('dur', failed ? '2.8s' : '2.2s');
  motion.setAttribute('repeatCount', 'indefinite');
  if (failed) {
    const points = [];
    for (let i = 0; i <= 18; i++) {
      const p = path.getPointAtLength(length * i / 36);
      points.push(`${i ? 'L' : 'M'}${p.x.toFixed(2)},${p.y.toFixed(2)}`);
    }
    motion.setAttribute('path', points.join(' '));
    motion.setAttribute('keyPoints', '0;1;1');
    motion.setAttribute('keyTimes', '0;0.72;1');
    motion.setAttribute('calcMode', 'linear');
    const fade = make('animate');
    fade.setAttribute('attributeName', 'opacity');
    fade.setAttribute('values', '1;1;0;0');
    fade.setAttribute('keyTimes', '0;0.72;0.82;1');
    fade.setAttribute('dur', '2.8s');
    fade.setAttribute('repeatCount', 'indefinite');
    dot.append(fade);
    const shrink = make('animate');
    shrink.setAttribute('attributeName', 'r');
    shrink.setAttribute('values', '4.8;4.8;6.5;0');
    shrink.setAttribute('keyTimes', '0;0.72;0.82;1');
    shrink.setAttribute('dur', '2.8s');
    shrink.setAttribute('repeatCount', 'indefinite');
    dot.append(shrink);
    for (const [dx, dy] of [[-11,-8],[10,-9],[-9,9],[12,7]]) {
      const spark = make('circle');
      spark.setAttribute('class', 'packet-spark');
      spark.setAttribute('r', '2.2');
      spark.setAttribute('opacity', '0');
      const fadeSpark = make('animate');
      fadeSpark.setAttribute('attributeName', 'opacity');
      fadeSpark.setAttribute('values', '0;0;1;0');
      fadeSpark.setAttribute('keyTimes', '0;0.72;0.78;1');
      fadeSpark.setAttribute('dur', '2.8s');
      fadeSpark.setAttribute('repeatCount', 'indefinite');
      const moveX = make('animate');
      moveX.setAttribute('attributeName', 'cx');
      moveX.setAttribute('values', `0;0;${dx}`);
      moveX.setAttribute('keyTimes', '0;0.72;1');
      moveX.setAttribute('dur', '2.8s');
      moveX.setAttribute('repeatCount', 'indefinite');
      const moveY = make('animate');
      moveY.setAttribute('attributeName', 'cy');
      moveY.setAttribute('values', `0;0;${dy}`);
      moveY.setAttribute('keyTimes', '0;0.72;1');
      moveY.setAttribute('dur', '2.8s');
      moveY.setAttribute('repeatCount', 'indefinite');
      spark.append(fadeSpark, moveX, moveY);
      packet.append(spark);
    }
  } else motion.setAttribute('path', path.getAttribute('d'));
  packet.append(motion);
  container.append(packet);
  return packet;
}
