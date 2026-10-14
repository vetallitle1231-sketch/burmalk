import * as THREE from './assets/vendor/three.module.js';
export async function mount(){
 const stage=document.getElementById('model-stage'),canvas=document.getElementById('model-canvas');
 const [info,buffer]=await Promise.all([fetch('./assets/model/pc.json').then(r=>{if(!r.ok)throw Error('Model manifest');return r.json();}),fetch('./assets/model/pc.bin').then(r=>{if(!r.ok)throw Error('Model geometry');return r.arrayBuffer();})]);
 const renderer=new THREE.WebGLRenderer({canvas,alpha:true,antialias:true,powerPreference:'low-power'});
 renderer.setPixelRatio(Math.min(window.devicePixelRatio||1,1.1));renderer.outputColorSpace=THREE.SRGBColorSpace;renderer.toneMapping=THREE.ACESFilmicToneMapping;renderer.toneMappingExposure=1.05;
 const scene=new THREE.Scene(),assembly=new THREE.Group();scene.add(assembly);
 scene.add(new THREE.HemisphereLight(0xc7d5e9,0x1d252b,2.1));
 const key=new THREE.DirectionalLight(0xffffff,2.5);key.position.set(-6,8,10);scene.add(key);
 const fill=new THREE.DirectionalLight(0x79b7c7,1.4);fill.position.set(6,2,-5);scene.add(fill);
 const camera=new THREE.OrthographicCamera(-4.3,4.3,4,-4,.1,60),rotors=[],meshes=[];
 const data=(type,range)=>new type(buffer,range[0],range[1]);
 for(const entry of info.meshes){
  const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.BufferAttribute(data(Float32Array,entry.position),3));geometry.setAttribute('normal',new THREE.BufferAttribute(data(Float32Array,entry.normal),3));geometry.setIndex(new THREE.BufferAttribute(data(Uint32Array,entry.index),1));
  const color=new THREE.Color().setRGB(entry.color[0],entry.color[1],entry.color[2],THREE.SRGBColorSpace);
  const material=new THREE.MeshStandardMaterial({color,metalness:.33,roughness:.48,side:THREE.DoubleSide,transparent:entry.color[3]<1,opacity:entry.color[3],depthWrite:entry.color[3]===1});
  if(entry.glow){material.emissive.copy(color);material.emissiveIntensity=.32;}
  const mesh=new THREE.Mesh(geometry,material);mesh.userData.part=entry.part;mesh.userData.glow=entry.glow;mesh.userData.originalEmission=material.emissive.clone();mesh.userData.originalIntensity=material.emissiveIntensity;meshes.push(mesh);
  if(entry.rotor){const group=new THREE.Group();group.position.fromArray(entry.rotor.center);group.add(mesh);assembly.add(group);rotors.push({group,axis:new THREE.Vector3().fromArray(entry.rotor.axis),angle:0,speed:entry.rotor.role==='gpu'?165:entry.rotor.role==='cpu'?130:100});}
  else assembly.add(mesh);
 }
 let yaw=23,pitch=12,drag=null,queued=0,visible=true,raf=0,lastFrame=0,moving=false,renderCount=0;
 const motion=document.getElementById('model-motion'),reduced=matchMedia('(prefers-reduced-motion: reduce)');
 const descriptions={all:'Та же модель, что в приложении. Нажми на деталь, чтобы рассмотреть её.',gpu:'Подробная видеокарта: три вентилятора, радиатор, тепловые трубки, задняя пластина и кабель питания.',cpu:'Процессор с башенным воздушным кулером. В приложении можно выбрать СЖО и изменить сборку.',ram:'Модули оперативной памяти. В редакторе приложения меняются их количество и оформление.',board:'Материнская плата с разъёмами, радиаторами и кабелями. Доступны размеры ATX, mATX и ITX.',case:'Корпус с сетчатой передней панелью, вентиляторами и отсеком питания.',disks:'Накопители внутри сборки. Их количество меняется в редакторе приложения.',cooling:'Вентиляторы корпуса и охлаждение. Движение этой модели иллюстративное.'};
 function cameraPosition(){const y=THREE.MathUtils.degToRad(yaw),p=THREE.MathUtils.degToRad(pitch),d=15;camera.position.set(Math.sin(y)*Math.cos(p)*d,Math.sin(p)*d,Math.cos(y)*Math.cos(p)*d);camera.lookAt(0,-.05,0);camera.updateMatrixWorld();}
 function render(){if(queued){cancelAnimationFrame(queued);queued=0;}cameraPosition();renderer.render(scene,camera);stage.dataset.rendered='true';stage.dataset.frames=String(++renderCount);}
 function requestRender(){if(!queued)queued=requestAnimationFrame(render);}
 function resize(){const width=stage.clientWidth,height=stage.clientHeight;renderer.setSize(width,height,false);const span=7.7;camera.left=-span*width/height/2;camera.right=span*width/height/2;camera.top=span/2;camera.bottom=-span/2;camera.updateProjectionMatrix();requestRender();}
 function select(part){document.getElementById('model-detail').textContent=descriptions[part]||descriptions.all;document.querySelectorAll('[data-part]').forEach(b=>{const active=b.dataset.part===part;b.setAttribute('aria-pressed',String(active));b.classList.toggle('active',active);});for(const mesh of meshes){const on=part!=='all'&&mesh.userData.part===part;mesh.material.emissive.copy(on?new THREE.Color(0x45d9ce):mesh.userData.originalEmission);mesh.material.emissiveIntensity=on?.5:mesh.userData.originalIntensity;}stage.dataset.selected=part;requestRender();}
 document.querySelectorAll('[data-part]').forEach(b=>b.addEventListener('click',()=>select(b.dataset.part)));
 document.getElementById('model-reset').addEventListener('click',e=>{e.stopPropagation();yaw=23;pitch=12;select('all');});
 const raycaster=new THREE.Raycaster(),pointer=new THREE.Vector2();
 stage.addEventListener('pointerdown',e=>{if(e.button!==0||e.target.closest('button'))return;drag={x:e.clientX,y:e.clientY,yaw,pitch,moved:false};stage.setPointerCapture(e.pointerId);});
 stage.addEventListener('pointermove',e=>{if(!drag)return;const dx=e.clientX-drag.x,dy=e.clientY-drag.y;drag.moved=drag.moved||Math.hypot(dx,dy)>5;if(drag.moved){yaw=drag.yaw+dx*.35;pitch=Math.max(-20,Math.min(48,drag.pitch+dy*.23));requestRender();}});
 stage.addEventListener('pointerup',e=>{if(!drag)return;const wasDrag=drag.moved;drag=null;if(stage.hasPointerCapture(e.pointerId))stage.releasePointerCapture(e.pointerId);if(wasDrag)return;const rect=canvas.getBoundingClientRect();pointer.set((e.clientX-rect.left)/rect.width*2-1,-(e.clientY-rect.top)/rect.height*2+1);raycaster.setFromCamera(pointer,camera);const hit=raycaster.intersectObjects(meshes)[0];if(hit)select(hit.object.userData.part);});stage.addEventListener('pointercancel',()=>drag=null);
 stage.addEventListener('keydown',e=>{if(!e.key.startsWith('Arrow'))return;e.preventDefault();if(e.key==='ArrowLeft')yaw-=9;if(e.key==='ArrowRight')yaw+=9;if(e.key==='ArrowUp')pitch=Math.min(48,pitch+6);if(e.key==='ArrowDown')pitch=Math.max(-20,pitch-6);requestRender();});
 function tick(now){raf=0;if(!moving)return;if(now-lastFrame>=50){const dt=lastFrame?Math.min((now-lastFrame)/1000,.15):.05;lastFrame=now;for(const rotor of rotors){rotor.angle=(rotor.angle+dt*rotor.speed)%360;rotor.group.quaternion.setFromAxisAngle(rotor.axis,THREE.MathUtils.degToRad(rotor.angle));}render();}raf=requestAnimationFrame(tick);}
 function updateMotion(){moving=motion.checked&&visible&&!document.hidden&&document.hasFocus()&&!reduced.matches;stage.dataset.animating=String(moving);if(!moving&&raf){cancelAnimationFrame(raf);raf=0;lastFrame=0;}if(moving&&!raf)raf=requestAnimationFrame(tick);}
 motion.addEventListener('change',updateMotion);document.addEventListener('visibilitychange',updateMotion);window.addEventListener('blur',updateMotion);window.addEventListener('focus',updateMotion);reduced.addEventListener('change',updateMotion);
 new IntersectionObserver(entries=>{visible=entries[0].isIntersecting;updateMotion();}).observe(stage);new ResizeObserver(resize).observe(stage);
 canvas.addEventListener('webglcontextlost',e=>{e.preventDefault();moving=false;if(raf)cancelAnimationFrame(raf);stage.classList.remove('ready');motion.disabled=true;stage.dataset.status='poster';document.getElementById('model-hint').textContent='Предпросмотр 3D-сборки';});
 stage.dataset.modelVersion=info.version;stage.dataset.triangles=String(info.triangles);stage.dataset.rotors=String(rotors.length);stage.dataset.animating='false';stage.dataset.status='ready';stage.classList.add('ready');document.getElementById('model-hint').textContent='Перетащи, чтобы повернуть';document.querySelectorAll('[data-part],#model-reset').forEach(button=>button.disabled=false);resize();select('all');
}
