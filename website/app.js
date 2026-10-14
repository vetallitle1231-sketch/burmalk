"use strict";
const shots=[
 {file:"dashboard",title:"Твой рабочий экран",description:"Свободное расположение виджетов, живые показатели и интерактивный 3D-ПК."},
 {file:"model",title:"Твоя собственная сборка",description:"Настрой компоненты, охлаждение и внешний вид. Выбирай детали, вращай модель и меняй ракурс."},
 {file:"rank",title:"Ранг по измеренным результатам",description:"Результаты CPU, GPU и RAM, прогресс и пять уровней производительности по методике Burmalk."},
 {file:"passport",title:"Паспорт твоего компьютера",description:"Сведения о комплектующих, модулях памяти и дисках. Оформление и экспорт PNG/PDF."},
 {file:"overlay",title:"Оверлей, который подходит тебе",description:"Выбор FPS, среднего FPS, времени кадра и датчиков. Цвет, размер и компактная строка с автосохранением."},
 {file:"games",title:"Твои игры в одной библиотеке",description:"Обнаруженные игры с иконками, поиск, добавление вручную и запуск через Burmalk."},
 {file:"health",title:"Понятное состояние ПК",description:"Цветная оценка доступных показателей и объяснение причин. Неполные данные отмечаются отдельно."},
 {file:"benchmark",title:"Проверь производительность",description:"CPU-тест всех потоков, проверка буфера RAM и отдельная аппаратная 3D-нагрузка GPU."},
 {file:"disks",title:"Скорость выбранного диска",description:"Измерение последовательного чтения и записи временного файла на выбранном локальном томе."}
];
let activeShot=0,dialogShot=0;
const image=document.getElementById("gallery-image"),lightbox=document.getElementById("lightbox");
const shotUrl=index=>`assets/screenshots/${shots[index].file}.png`;
function chooseShot(index){activeShot=(index+shots.length)%shots.length;const shot=shots[activeShot];image.src=shotUrl(activeShot);image.alt=shot.title;document.getElementById("gallery-title").textContent=shot.title;document.getElementById("gallery-description").textContent=shot.description;document.getElementById("gallery-open").setAttribute("aria-label",`Увеличить скриншот: ${shot.title}`);document.querySelectorAll("[data-shot]").forEach(b=>{const selected=Number(b.dataset.shot)===activeShot;b.classList.toggle("active",selected);b.setAttribute("aria-pressed",String(selected));});}
document.querySelectorAll("[data-shot]").forEach(b=>b.addEventListener("click",()=>chooseShot(Number(b.dataset.shot))));
function showShot(index){dialogShot=(index+shots.length)%shots.length;document.getElementById("lightbox-image").src=shotUrl(dialogShot);document.getElementById("lightbox-image").alt=shots[dialogShot].title;document.getElementById("lightbox-title").textContent=shots[dialogShot].title;document.getElementById("lightbox-count").textContent=`${dialogShot+1} / ${shots.length}`;}
document.getElementById("gallery-open").addEventListener("click",()=>{showShot(activeShot);lightbox.showModal();document.body.classList.add("modal-open");});
document.getElementById("close-lightbox").addEventListener("click",()=>lightbox.close());document.getElementById("previous-shot").addEventListener("click",()=>showShot(dialogShot-1));document.getElementById("next-shot").addEventListener("click",()=>showShot(dialogShot+1));
lightbox.addEventListener("close",()=>document.body.classList.remove("modal-open"));lightbox.addEventListener("keydown",e=>{if(e.key==="ArrowRight"||e.key==="ArrowLeft"){e.preventDefault();showShot(dialogShot+(e.key==="ArrowRight"?1:-1));}});
lightbox.addEventListener("click",e=>{if(e.target===lightbox){const r=lightbox.getBoundingClientRect();if(e.clientX<r.left||e.clientX>r.right||e.clientY<r.top||e.clientY>r.bottom)lightbox.close();}});
const menu=document.getElementById("menu-toggle"),nav=document.getElementById("navigation-links");menu.addEventListener("click",()=>{const open=nav.classList.toggle("open");menu.setAttribute("aria-expanded",String(open));menu.setAttribute("aria-label",open?"Закрыть меню":"Открыть меню");});nav.querySelectorAll("a").forEach(a=>a.addEventListener("click",()=>{nav.classList.remove("open");menu.setAttribute("aria-expanded","false");}));
let modelStarted=false;
document.querySelectorAll('[data-part],#model-reset').forEach(button=>button.disabled=true);
async function startModel(){if(modelStarted)return;modelStarted=true;document.getElementById("model-hint").textContent="Загрузка 3D-модели…";try{const model=await import("./model.js");await model.mount();}catch(error){document.getElementById("model-hint").textContent="3D-превью сборки Burmalk";document.getElementById("model-detail").textContent="Подробная модель этой сборки доступна в приложении. Здесь показан её предварительный вид.";document.getElementById("model-motion").disabled=true;document.getElementById("model-stage").dataset.status="poster";}}
if(location.protocol!=="file:"){if("IntersectionObserver" in window){const observer=new IntersectionObserver(entries=>{if(entries.some(e=>e.isIntersecting)){observer.disconnect();startModel();}},{rootMargin:"80px"});observer.observe(document.getElementById("model-card"));}else startModel();}else{document.getElementById("model-hint").textContent="Интерактивный 3D-вид доступен на сайте";document.getElementById("model-motion").disabled=true;}
