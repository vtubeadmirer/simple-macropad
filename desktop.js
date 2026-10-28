// Native PC profile and input bridge. Onboard operations are added in hardware.js.
const hostSend=(type,data)=>window.chrome.webview.postMessage({type,data});
$('.badge').id='device-status';$('.badge').textContent='연결 확인 중';
$('.save-note').textContent='PC에 배치를 저장합니다. 기기 자체 설정은 변경하지 않습니다.';
$('.bottom > span').textContent='배치 관리 · 실제 입력 확인 가능 · 기기 자체 설정 변경 없음';
let desktopReady=false, saveSequence=0, lastSaved=0,pendingSave=null,failedSave=false;
const versionButton=makeButton('버전 확인 중','quiet');versionButton.id='app-version';$('.meta').prepend(versionButton);
const infoDialog=document.createElement('dialog');infoDialog.style.width='min(640px,94vw)';
infoDialog.innerHTML='<h2 id="app-version-title"></h2><p>실행 파일</p><pre id="app-exe-path" style="white-space:pre-wrap;overflow-wrap:anywhere;font-size:12px"></pre><p>배치 저장 파일</p><pre id="app-profile-path" style="white-space:pre-wrap;overflow-wrap:anywhere;font-size:12px"></pre><p>화면 위 버전과 이 경로로 최신 앱이 실행됐는지 확인할 수 있습니다.</p><div class="dialog-actions"><button id="app-info-close">닫기</button></div>';
document.body.append(infoDialog);versionButton.onclick=()=>infoDialog.showModal();$('#app-info-close').onclick=()=>infoDialog.close();
const signalCombos=new Map();
entries=Array.from({length:18},blank);draft=clone(entries[0]);
document.querySelectorAll('button,input').forEach(e=>e.disabled=true);
$('#help-dialog p').innerHTML='① 키 또는 노브 방향을 선택합니다.<br>② 아래 키보드에서 조합을 지정하거나 ‘기기 입력 확인’을 누릅니다.<br>③ 이름을 붙이고 배치를 저장합니다.';
$('#help-dialog p:nth-of-type(2)').textContent='이 앱은 배치를 PC에 기록합니다. 물리 키보드의 출력은 바꾸지 않습니다. 입력 확인 중 최대 2분간 캡처·작업 보기·데스크톱 전환 단축키를 보조 감지합니다. 보조 감지는 키보드 종류를 구분하지 못하므로 다른 키보드는 누르지 마세요. 스크롤 등 일반 입력은 SIDE-KEYBOARD만 감지합니다. 원래 동작은 차단하지 않습니다.';
persist=function(){if(!desktopReady||pendingSave)return false;pendingSave={sequence:++saveSequence};hostSend('save',{entries:clone(entries),sequence:saveSequence});return false;};
save=function(){if(pendingSave)return false;if(draft.mods.length&&!draft.key){toast('보조키와 함께 사용할 일반 키를 선택해주세요.');return false;}stopCapture();if(!failedSave)undo=clone(entries);entries[selected]=clone(draft);persist();renderPad();renderEditor();return true;};
$('#save').onclick=save;
// Native capture must survive Windows overlays and desktop switches.
window.removeEventListener('blur',stopCapture);
stopCapture=function(){capturing=false;hostSend('capture',false);$('#capture').textContent='⌨ 기기 입력 확인';$('#keyboard-help').textContent='Ctrl · Shift · Alt · Win은 켜고 끄기, 일반 키는 하나를 선택합니다.';$('#keyboard-help').classList.remove('capture-hint');};
$('#capture').onclick=()=>{if(capturing){stopCapture();return;}capturing=true;hostSend('capture',true);$('#capture').textContent='확인 취소 · Esc';$('#keyboard-help').textContent='최대 2분간 시스템 단축키도 감지합니다. 이때는 일반 키보드를 누르지 마세요. 캡처는 Esc로 닫고 돌아오세요.';$('#keyboard-help').classList.add('capture-hint');};
const systemPresets=[['캡처 (4번)',{name:'화면 캡처',mods:['Shift','Win'],key:'S'}],['작업 보기 (7번)',{name:'작업 보기',mods:['Win'],key:'Tab'}],['이전 데스크톱 (11번)',{name:'이전 데스크톱',mods:['Ctrl','Win'],key:'←'}],['다음 데스크톱 (12번)',{name:'다음 데스크톱',mods:['Ctrl','Win'],key:'→'}]];
document.querySelector('[data-preset="capture"]').remove();
for(const [text,value] of systemPresets){const b=makeButton(text,'chip');b.title='선택한 칸에 조합을 기록합니다. 물리 키보드는 변경하지 않습니다.';b.onclick=()=>{stopCapture();draft=clone(value);renderEditor();};$('.presets').append(b);}
// Browser keydown is intentionally not used for capture: it cannot identify devices.
document.addEventListener('keydown',e=>{if(capturing){if(e.key==='Escape')stopCapture();e.stopImmediatePropagation();}},true);
$('#export').onclick=()=>hostSend('export',entries);
$('#theme-toggle').addEventListener('click',()=>hostSend('theme',document.documentElement.dataset.theme));
window.Desktop={
 initialize(data){entries=data.entries;entries.forEach(e=>{if(e.signal)signalCombos.set(e.signal,combo(e));});draft=clone(entries[selected]);document.documentElement.dataset.theme=data.theme||document.documentElement.dataset.theme;desktopReady=true;document.querySelectorAll('button,input').forEach(e=>e.disabled=false);versionButton.textContent='v'+data.version;document.title='키패드 v'+data.version;$('#app-version-title').textContent='키패드 v'+data.version;$('#app-exe-path').textContent=data.exePath;$('#app-profile-path').textContent=data.profilePath;renderTheme();renderPad();renderEditor();if(data.migrated)toast('기존 앱의 배치를 가져왔어요. 원본은 그대로 유지됩니다.');},
 connection(connected){$('#device-status').textContent=connected?'● 키보드 연결됨':'○ 키보드 미연결';},
 input(event){if(!desktopReady||pendingSave)return;const e={mods:event.mods,key:event.key,signal:event.signal,source:event.source||'device'};signalCombos.set(e.signal,combo(e));if(wizard.open){wizardInput(e);return;}if(capturing){draft.mods=e.mods;draft.key=e.key;draft.signal=e.signal;draft.source=e.source;stopCapture();renderEditor();toast(e.source==='system'?'Windows 단축키 보조 감지 완료 · 장치 구분 없음':'기기 입력을 확인했어요. 이름을 붙이고 저장하세요.');}else{let matches=[];entries.forEach((entry,i)=>{if(entry.signal===e.signal||(!entry.signal&&entry.key===e.key&&mods.every(m=>entry.mods.includes(m)===e.mods.includes(m))))matches.push(i);});if(matches.length===1&&!dirty())move(matches[0]);toast('기기 입력: '+combo(e)+(matches.length>1?' · 같은 조합이 여러 칸에 있어요.':''));}},
  saved(sequence){lastSaved=Math.max(lastSaved,sequence);if(pendingSave&&pendingSave.sequence===sequence){pendingSave=null;failedSave=false;document.querySelectorAll('button,input').forEach(e=>e.disabled=false);renderEditor();}toast('PC에 배치를 저장했어요.');},
  saveFailed(result){if(pendingSave&&pendingSave.sequence===result.sequence){pendingSave=null;failedSave=true;document.querySelectorAll('button,input').forEach(e=>e.disabled=false);renderEditor();}toast('저장 실패: '+result.message+' · 저장 재시도를 눌러주세요.');},
  error(message){toast(message);},
  captureExpired(){stopCapture();scanListening=false;if(wizard.open&&scanIndex<18&&!scanCandidate){$('#scan-result').textContent='입력 대기 시간이 끝났어요.';$('#scan-warning').textContent='다시 감지를 눌러 계속해주세요.';}toast('2분이 지나 입력 확인을 종료했어요.');},
   canClose(){return !wizard.open&&!dirty()&&!pendingSave&&!failedSave&&lastSaved===saveSequence;}
};
// Avoid stale physical signal after an on-screen edit.
const originalEditor=renderEditor;
renderEditor=function(updateName=true){
 if(draft.signal&&signalCombos.has(draft.signal)&&signalCombos.get(draft.signal)!==combo(draft)){delete draft.signal;draft.source='manual';}
 originalEditor(updateName);const changed=dirty();
 $('#save').textContent=pendingSave?'저장 중…':failedSave?'저장 재시도':changed?'배치 저장':'저장됨';
 $('#save').disabled=!!pendingSave||(!failedSave&&!changed);
 $('.save-note').textContent=pendingSave?'PC 파일에 저장 중입니다.':failedSave?'저장 실패 · 변경 내용은 아직 메모리에만 있습니다.':changed?'저장 전 변경사항이 있어요. 기기 자체 설정은 바뀌지 않습니다.':'변경 없음 · 현재 배치가 PC에 저장되어 있습니다.';
 $('#save').title=pendingSave?'저장이 끝날 때까지 기다려주세요.':changed||failedSave?'현재 배치를 PC에 저장합니다.':'이름이나 조합을 바꾸면 저장할 수 있습니다.';
 if(pendingSave)document.querySelectorAll('button,input').forEach(e=>e.disabled=true);
};
const wizardButton=makeButton('전체 배치 확인','quiet');wizardButton.id='scan-all';wizardButton.disabled=true;$('.section-head').append(wizardButton);
const wizard=document.createElement('dialog');wizard.id='scan-dialog';wizard.style.width='min(640px,94vw)';
wizard.innerHTML=`<div class="section-head"><span class="eyebrow" id="scan-progress"></span><button class="quiet" id="scan-cancel">종료</button></div>
<h2 id="scan-title" style="margin-top:14px"></h2><p id="scan-guide"></p>
<div id="scan-map" style="display:grid;grid-template-columns:repeat(4,1fr);gap:6px;margin:14px 0"></div>
<div id="scan-result" class="combo-box" style="min-height:64px;margin:14px 0" aria-live="polite"></div>
<p id="scan-warning" style="font-size:12px"></p>
<div id="scan-review" style="max-height:300px;overflow:auto;margin-bottom:15px"></div>
<details style="margin:10px 0"><summary style="cursor:pointer;font-size:12px">감지가 안 되나요? 기능 직접 지정</summary><p style="font-size:11px">실제 감지가 아닌 수동 기록입니다. 해당 키의 기능을 알고 있을 때 선택하세요.</p><div id="scan-presets" class="presets"></div></details>
<p style="font-size:11px">시스템 단축키 보조 감지는 키보드 종류를 구분하지 못합니다. 확인 중에는 일반 키보드를 누르지 마세요. 최대 2분 후 자동 종료됩니다. 캡처는 Esc로 닫고 앱으로 돌아오세요.</p>
<div class="dialog-actions"><button id="scan-back">이전</button><button id="scan-skip">건너뛰기</button><button id="scan-retry">다시 감지</button><button class="primary" id="scan-next">다음</button></div>`;
document.body.append(wizard);
for(const [text,value] of systemPresets){const b=makeButton(text,'chip');b.onclick=()=>{if(scanIndex>=18)return;clearTimeout(scanTimer);scanCandidate=null;scanEvents=[];scanListening=true;const mask=(value.mods.includes('Ctrl')?2:0)|(value.mods.includes('Shift')?1:0)|(value.mods.includes('Win')?8:0);const vk={'S':83,'Tab':9,'←':37,'→':39}[value.key];wizardInput({...clone(value),source:'manual',signal:'K:'+mask+':'+vk});$('#scan-warning').textContent='직접 지정한 프리셋입니다. 실측 결과가 아닙니다.';};$('#scan-presets').append(b);}
let scanIndex=0,scanEntries=[],scanChanged=new Set(),scanCandidate=null,scanEvents=[],scanTimer=null,scanSettling=false,scanListening=false;
function beginScan(){if(dirty()){toast('현재 편집 내용을 먼저 저장하거나 되돌려주세요.');return;}if(failedSave){toast('저장 재시도로 배치를 저장한 뒤 다시 시작해주세요.');return;}if(lastSaved!==saveSequence){toast('저장이 끝난 뒤 다시 시작해주세요.');return;}stopCapture();scanIndex=0;scanEntries=clone(entries);scanChanged=new Set();wizard.showModal();scanStep();}
function scanStep(){clearTimeout(scanTimer);scanCandidate=null;scanEvents=[];scanSettling=false;const review=scanIndex===18;
 scanListening=!review;hostSend('capture',!review);
 $('#scan-progress').textContent=review?'확인 완료':`${scanIndex+1} / 18`;
 $('#scan-title').textContent=review?'감지한 배치를 확인하세요':title(scanIndex)+' 입력을 확인해요';
 $('#scan-guide').textContent=review?'저장을 누르기 전까지 기존 배치는 바뀌지 않습니다.':scanIndex<12?`왼쪽 위부터 ${Math.floor(scanIndex/4)+1}번째 줄, ${scanIndex%4+1}번째 키를 한 번 누르세요.`:`오른쪽 ${scanIndex<15?'위쪽':'아래쪽'} 노브를 ${['왼쪽으로 한 칸 돌려주세요.','한 번 눌러주세요.','오른쪽으로 한 칸 돌려주세요.'][(scanIndex-12)%3]}`;
  $('#scan-result').textContent=review?`${scanChanged.size}개 확인 · ${18-scanChanged.size}개 기존 배치 유지`:'입력 대기 중…';
 $('#scan-warning').textContent='';$('#scan-review').replaceChildren();$('#scan-map').replaceChildren();
 $('#scan-map').style.gridTemplateColumns='minmax(0,1fr) 100px';
 const keyMap=document.createElement('div');keyMap.style.cssText='display:grid;grid-template-columns:repeat(4,1fr);gap:6px';
 for(let i=0;i<12;i++){let cell=document.createElement('div');cell.textContent=String(i+1).padStart(2,'0');cell.style.cssText='text-align:center;padding:8px;border:1px solid var(--line);border-radius:6px;background:'+(scanIndex===i?'var(--accent)':'var(--soft)')+';color:'+(scanIndex===i?'var(--bg)':'var(--ink)');keyMap.append(cell);}
 const knobMap=document.createElement('div');knobMap.style.cssText='display:flex;flex-direction:column;justify-content:space-around;gap:8px';
 for(let n=0;n<2;n++){let cell=document.createElement('div');const active=scanIndex>=12+n*3&&scanIndex<15+n*3;cell.textContent='◉ '+(n===0?'위 노브 1':'아래 노브 2');cell.style.cssText='text-align:center;padding:10px 4px;border:1px solid var(--line);border-radius:20px;background:'+(active?'var(--accent)':'var(--soft)')+';color:'+(active?'var(--bg)':'var(--ink)');knobMap.append(cell);}
 $('#scan-map').append(keyMap,knobMap);
 $('#scan-back').disabled=scanIndex===0;$('#scan-skip').hidden=review;$('#scan-retry').hidden=review;$('#scan-next').textContent=review?'전체 배치 저장':'입력을 기다리는 중';$('#scan-next').disabled=!review||scanChanged.size===0;$('#scan-dialog details').hidden=review;
 if(review){for(let i=0;i<18;i++){let row=document.createElement('div');row.style.cssText='display:flex;justify-content:space-between;gap:12px;padding:7px 0;border-bottom:1px solid var(--line)';let label=document.createElement('span'),value=document.createElement('span');label.textContent=title(i);value.textContent=(combo(scanEntries[i])||'미등록')+(scanChanged.has(i)?(scanEntries[i].source==='manual'?' · 직접 지정':' · 새로 감지'):' · 유지');row.append(label,value);$('#scan-review').append(row);}}
}
function wizardInput(e){
 if(!scanListening||scanIndex>=18||scanCandidate)return;
 scanEvents.push(clone(e));scanSettling=true;$('#scan-result').textContent='입력 확인 중…';clearTimeout(scanTimer);
 scanTimer=setTimeout(()=>{
  scanSettling=false;scanListening=false;hostSend('capture',false);
  const distinct=[...new Map(scanEvents.map(x=>[x.signal,x])).values()];
  if(distinct.length!==1){$('#scan-result').textContent=distinct.map(combo).join(' → ');$('#scan-warning').textContent='여러 입력이 감지됐어요. 연속 매크로는 자동 등록하지 않습니다. 다시 감지하거나 건너뛰어주세요.';return;}
  scanCandidate=distinct[0];const manual=scanCandidate.source==='manual';
  $('#scan-result').textContent=combo(scanCandidate)+(manual?' · 직접 지정':'');
  const same=scanEntries.some((x,i)=>i!==scanIndex&&x.signal&&x.signal===scanCandidate.signal);
  $('#scan-warning').textContent=manual?'프리셋을 직접 지정했습니다. 실제 입력을 측정한 결과는 아닙니다.':scanCandidate.source==='system'?'Windows 단축키 보조 감지 결과입니다. 키보드 종류는 구분하지 못합니다.':same?'같은 입력이 다른 칸에도 있어요. 물리 키 위치는 안내 순서로 기록합니다.':'입력을 확인했다면 다음으로 이동하세요.';
  $('#scan-next').textContent='확인 후 다음';$('#scan-next').disabled=false;
 },250);
}
function cancelScan(){if(scanChanged.size||scanCandidate){if(!window.confirm('감지한 내용을 저장하지 않고 종료할까요?'))return;}clearTimeout(scanTimer);scanListening=false;hostSend('capture',false);wizard.close();}
wizardButton.onclick=beginScan;$('#scan-cancel').onclick=cancelScan;wizard.addEventListener('cancel',e=>{e.preventDefault();cancelScan();});
$('#scan-retry').onclick=scanStep;$('#scan-back').onclick=()=>{if(scanIndex>0){scanIndex--;scanStep();}};
$('#scan-skip').onclick=()=>{scanEntries[scanIndex]=clone(entries[scanIndex]);scanChanged.delete(scanIndex);scanIndex++;scanStep();};
$('#scan-next').onclick=()=>{if(scanIndex===18){undo=clone(entries);entries=clone(scanEntries);draft=clone(entries[selected]);persist();clearTimeout(scanTimer);wizard.close();renderPad();renderEditor();return;}if(!scanCandidate||scanSettling)return;let value=clone(scanCandidate);value.name=scanEntries[scanIndex].name||combo(value);scanEntries[scanIndex]=value;scanChanged.add(scanIndex);scanIndex++;scanStep();};
renderPad();renderEditor();
stopCapture();
hostSend('ready',null);
