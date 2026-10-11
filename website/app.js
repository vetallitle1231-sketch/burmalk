"use strict";
const shots = [
 {file:"dashboard",title:"Твой собственный центр управления",description:"Живые графики и карточки. В 1.1.8 их можно свободно размещать и скрывать."},
 {file:"games",title:"Твои игры в одной библиотеке",description:"Обнаруженные игры, поиск, иконки и запуск. Игры можно добавлять вручную."},
 {file:"health",title:"Состояние, которое понятно",description:"Цветная оценка, причины и доступные датчики. Неполные данные отмечаются отдельно."},
 {file:"benchmark",title:"Проверь CPU, RAM и видеокарту",description:"Минутная проверка CPU/RAM и отдельный двухминутный GPU-тест с собственной шкалой баллов."},
 {file:"gpu",title:"Видеокарта под наблюдением",description:"Частота ядра, нагрузка и доступные регулировки вентилятора. Поддержка зависит от оборудования."},
 {file:"disks",title:"Скорость выбранного диска",description:"Выбор локального тома и измерение последовательного чтения и записи временного файла."}
];
const shotUrl = index => `assets/screenshots/${shots[index].file}.png`;
let activeShot = 0;
const tabs = [...document.querySelectorAll(".gallery-tab")];
function selectShot(index) {
 activeShot = (index + shots.length) % shots.length;
 const shot = shots[activeShot];
 document.getElementById("gallery-image").src = shotUrl(activeShot);
 document.getElementById("gallery-image").alt = shot.title;
 document.getElementById("gallery-title").textContent = shot.title;
 document.getElementById("gallery-description").textContent = shot.description;
 document.getElementById("gallery-open").dataset.shot = String(activeShot);
 tabs.forEach((tab,i)=>{tab.classList.toggle("active",i===activeShot);tab.setAttribute("aria-pressed",String(i===activeShot));});
}
 tabs.forEach(tab=>tab.addEventListener("click",()=>selectShot(Number(tab.dataset.index))));
const lightbox = document.getElementById("lightbox");
let lightboxIndex = 0;
function updateLightbox(index) {
 lightboxIndex=(index+shots.length)%shots.length;
 document.getElementById("lightbox-image").src=shotUrl(lightboxIndex);
 document.getElementById("lightbox-image").alt=shots[lightboxIndex].title;
 document.getElementById("lightbox-title").textContent=shots[lightboxIndex].title;
 document.getElementById("lightbox-count").textContent=`${lightboxIndex+1} / ${shots.length}`;
}
function openLightbox(index){updateLightbox(index);lightbox.showModal();document.body.classList.add("modal-open");}
 document.querySelectorAll(".image-open").forEach(b=>b.addEventListener("click",()=>openLightbox(Number(b.dataset.shot))));
 document.getElementById("expand-gallery").addEventListener("click",()=>openLightbox(activeShot));
 document.getElementById("close-lightbox").addEventListener("click",()=>lightbox.close());
 document.getElementById("previous-shot").addEventListener("click",()=>updateLightbox(lightboxIndex-1));
 document.getElementById("next-shot").addEventListener("click",()=>updateLightbox(lightboxIndex+1));
 lightbox.addEventListener("close",()=>document.body.classList.remove("modal-open"));
 lightbox.addEventListener("click",event=>{if(event.target===lightbox){const r=lightbox.getBoundingClientRect();if(event.clientX<r.left||event.clientX>r.right||event.clientY<r.top||event.clientY>r.bottom)lightbox.close();}});
 lightbox.addEventListener("keydown",event=>{if(event.key==="ArrowRight"){event.preventDefault();updateLightbox(lightboxIndex+1);}if(event.key==="ArrowLeft"){event.preventDefault();updateLightbox(lightboxIndex-1);}});
// Lightweight local CSS model, with no live hardware access.
function makeBox(part,x,y,z,w,h,depth,label,face,edge){
 const box=document.createElement("div");box.className="cube";box.dataset.part=part;
 Object.assign(box.style,{left:`${x}px`,top:`${y}px`,width:`${w}px`,height:`${h}px`,transform:`translateZ(${z}px)`});
 box.style.setProperty("--face",face);box.style.setProperty("--edge",edge);
 const faces=[
 ["front",w,h,`translateZ(${depth/2}px)`], ["back",w,h,`rotateY(180deg) translateZ(${depth/2}px)`],
 ["left",depth,h,`translateX(${-depth/2}px) rotateY(-90deg)`], ["right",depth,h,`translateX(${w-depth/2}px) rotateY(90deg)`],
 ["top",w,depth,`translateY(${-depth/2}px) rotateX(90deg)`], ["bottom",w,depth,`translateY(${h-depth/2}px) rotateX(-90deg)`]];
 faces.forEach(([side,fw,fh,transform])=>{const f=document.createElement("div");f.className=`cube-face cube-${side}`;Object.assign(f.style,{width:`${fw}px`,height:`${fh}px`,transform});if(side==="front")f.textContent=label;box.append(f);});
 document.getElementById("components").append(box);
}
makeBox("board",18,20,-75,210,275,7,"","#112d3c","#3b6276");
makeBox("cpu",45,65,-25,70,70,42,"CPU","#203f50","#62969e");
makeBox("ram",135,53,-40,17,110,20,"","#644f87","#ad94d7");
makeBox("ram",160,53,-40,17,110,20,"","#644f87","#ad94d7");
makeBox("gpu",30,198,0,165,37,100,"GPU / BURMALK","#173d4c","#6a94a5");
makeBox("psu",23,257,-14,112,43,130,"POWER","#192b40","#3d5670");
const components={
 cpu:{kicker:"ПРОЦЕССОР",title:"Нагрузка. Частота. Температура.",description:"Следи за загрузкой и частотой процессора, а при доступном аппаратном датчике — за температурой. История на графике помогает заметить изменение нагрузки.",tags:["Загрузка","MHz","°C"]},
 gpu:{kicker:"ВИДЕОКАРТА",title:"Знай, как работает твоя GPU.",description:"Температура, частота ядра, загрузка и видеопамять. Через RTSS доступные показатели можно вывести в игру, а в 1.1.8 — запустить отдельный 3D-тест.",tags:["Температура","Частота ядра","VRAM"]},
 ram:{kicker:"ОПЕРАТИВНАЯ ПАМЯТЬ",title:"Видно, сколько места осталось.",description:"Следи за занятым и доступным объёмом RAM. В 1.1.8 есть проверка записи и чтения 128 МиБ памяти; она не заменяет диагностику всех модулей.",tags:["Занято / доступно","История","Проверка RAM"]}
};
function selectComponent(part){const item=components[part];document.getElementById("component-kicker").textContent=item.kicker;document.getElementById("component-title").textContent=item.title;document.getElementById("component-description").textContent=item.description;document.getElementById("component-tags").replaceChildren(...item.tags.map(t=>{const s=document.createElement("span");s.textContent=t;return s;}));document.querySelectorAll("[data-component]").forEach(b=>{const selected=b.dataset.component===part;b.classList.toggle("active",selected);b.setAttribute("aria-pressed",String(selected));});document.querySelectorAll(".cube").forEach(c=>c.classList.toggle("is-selected",c.dataset.part===part));}
 document.querySelectorAll("[data-component]").forEach(b=>b.addEventListener("click",()=>selectComponent(b.dataset.component)));
 selectComponent("cpu");
const model=document.getElementById("pc-model"),stage=document.getElementById("model-stage"),rotation=document.getElementById("rotation");
let angle=-24,drag=null;
function rotate(value){angle=Math.max(-55,Math.min(55,Number(value)));model.style.transform=`rotateX(-12deg) rotateY(${angle}deg)`;rotation.value=String(Math.round(angle));}
 rotation.addEventListener("input",()=>rotate(rotation.value));
 document.getElementById("reset-model").addEventListener("click",()=>rotate(-24));
 stage.addEventListener("pointerdown",e=>{if(e.button!==0)return;drag={x:e.clientX,y:e.clientY,angle};stage.setPointerCapture(e.pointerId);});
 stage.addEventListener("pointermove",e=>{if(drag&&Math.abs(e.clientX-drag.x)>=Math.abs(e.clientY-drag.y))rotate(drag.angle+(e.clientX-drag.x)*.4);});
 stage.addEventListener("pointerup",()=>{drag=null;});stage.addEventListener("pointercancel",()=>{drag=null;});
 stage.addEventListener("keydown",e=>{if(e.key==="ArrowLeft"||e.key==="ArrowRight"){e.preventDefault();rotate(angle+(e.key==="ArrowRight"?5:-5));}});
if ("IntersectionObserver" in window) {
 const modelObserver = new IntersectionObserver(entries => {
  entries.forEach(entry => entry.target.classList.toggle("model-active", entry.isIntersecting));
 });
 modelObserver.observe(document.querySelector(".model-area"));
}
