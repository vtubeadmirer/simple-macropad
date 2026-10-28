// Direct editing: the visible keyboard assigns to the selected physical button.
easyStyle.textContent+=`
html:not([data-advanced="true"]) .keyboard-panel{display:block!important;margin-top:18px}
.easy-progress,.easy-connect{display:none!important}
.easy-welcome{margin:0 0 14px}.easy-welcome h1{font-size:26px}.easy-welcome p{font-size:16px}
html:not([data-advanced="true"]) .device{margin:12px auto}
html:not([data-advanced="true"]) .device .pad-key{min-height:76px}
.easy-picker{padding:18px}.easy-action{min-height:80px;padding:12px}.easy-action strong{font-size:17px}
.easy-action[aria-pressed="true"]{padding:11px}.easy-action span{font-size:14px}
.easy-feedback:empty{display:none}.easy-save{margin:16px 0}.keyboard-heading h2{font-size:21px}
`;
easyWelcome.querySelector('h1').textContent='누르고, 원하는 키로 바꾸세요.';
easyWelcome.querySelector('p').textContent='위의 키패드 버튼을 선택하고 아래 키보드를 누르면 바로 지정돼요.';
easyDeviceTitle.querySelector('h2').textContent='내 키패드';
easyDeviceTitle.querySelector('p').textContent='바꿀 버튼을 누르세요.';
$('.keyboard-panel').after(easySave);
easySave.after(easyHelp);
$('#easy-custom').remove();
easyHelp.querySelector('details').innerHTML='<summary>사용 도움말</summary><p>위에서 바꿀 버튼을 선택하고 아래 키보드에서 원하는 키를 누르세요. Ctrl·Shift·Alt·Win을 함께 선택하면 단축키가 돼요. 다른 버튼도 자유롭게 바꾼 뒤 <strong>키보드에 저장</strong>을 누르면 적용돼요.</p>';
let directSelfTest=false,directReadStarted=false;
const directInit=Desktop.initialize;Desktop.initialize=function(data){directSelfTest=!!data.selfTest;directInit(data);};
function directRead(){if(directSelfTest||directReadStarted||!desktopReady||hwBusy||pendingSave)return;directReadStarted=true;hwIntent='auto';setHwBusy(true);hwStatus('키보드 설정을 자동으로 읽는 중…');hostSend('hardwareRead');}
const directConnection=Desktop.connection;Desktop.connection=function(value){directConnection(value);if(value)directRead();};
// Keep an explicit retry for a failed initial connection, without discarding edits automatically.
const retry=makeButton('연결 다시 확인','easy-button');retry.id='direct-retry';easySave.append(retry);retry.onclick=()=>{directReadStarted=false;directRead();};
const directRefreshBase=easyRefresh;easyRefresh=function(){
 // The previous easy UI expects this control; keep a detached placeholder for its state updates.
 directRefreshBase();
 const ready=!!hwSnapshot,n=easyChanges();
 $('#easy-picker-copy').textContent='자주 쓰는 기능도 바로 지정할 수 있어요.';
 $('#easy-save').textContent=hwBusy?'키보드와 통신 중…':'키보드에 저장';
 $('#easy-save-title').textContent=hwBusy?'키보드를 확인하고 있어요.':!ready?'키보드 연결을 확인해주세요.':n!==0?'저장하지 않은 변경이 있어요.':'키보드와 같은 설정이에요.';
 $('#easy-save-copy').textContent=hwBusy?'잠시 기다려주세요.':!ready?'USB 연결 후 연결 다시 확인을 눌러주세요.':n!==0?'원하는 만큼 바꾸고 한 번에 저장하세요.':'버튼을 선택하고 아래 키보드를 눌러 바꿀 수 있어요.';
 $('.keyboard-heading h2').textContent=easyTitle(selected)+'에 넣을 키';
 $('#keyboard-help').textContent='누르면 바로 지정 · Ctrl / Shift / Alt / Win은 함께 누를 키';
 $('.keyboard-panel').querySelectorAll('button').forEach(b=>b.disabled=!ready||hwBusy||!!pendingSave);
 retry.hidden=ready||hwBusy;retry.disabled=hwBusy||!!pendingSave;
};
// Retain the old selector privately for the shared refresh routine.
const customPlaceholder=makeButton('','');customPlaceholder.id='easy-custom';customPlaceholder.hidden=true;easyPicker.append(customPlaceholder);
function directCommit(){
 draft.name='';draft.signal='';draft.source='manual';
 undo=clone(entries);entries[selected]=clone(draft);persist();renderPad();renderEditor();
}
$('.keyboard-panel').addEventListener('click',e=>{
 if(easyIsAdvanced||!hwSnapshot||hwBusy||pendingSave)return;
 if(e.target.closest('.kb-key,.function-grid button'))directCommit();
});
// Selecting the same ordinary key again keeps the assignment rather than producing a blank slot.
document.querySelectorAll('.kb-key').forEach(b=>{const original=b.onclick;b.onclick=()=>{if(!easyIsAdvanced&&!mods.includes(b.dataset.key)){stopCapture();draft.key=b.dataset.key;renderEditor(false);}else original();};});
$('#easy-save').onclick=()=>{
 if(!hwSnapshot||hwBusy||pendingSave)return;
 try{stopRecording();const keys=currentKeys(),macros=macrosForApply();if(JSON.stringify(macros)!==JSON.stringify(hwSnapshot.macros))validateMacroHolds();setHwBusy(true);hwStatus('키보드에 저장하고 다시 읽어 확인하는 중…');hostSend('hardwareApply',{baseline:clone(hwSnapshot),keys,macros});}catch(e){hwStatus(e.message);}
};
easyRefresh();
// Three explicit sizes, persisted independently from the legacy large-text toggle.
const smallText=makeButton('글씨 작게','quiet');smallText.id='easy-small-text';largeText.before(smallText);
const normalText=makeButton('기본 크기','quiet');normalText.id='easy-normal-text';largeText.before(normalText);
function setTextSize(size){setLargeText(size==='large');document.documentElement.dataset.textSize=size;largeText.textContent='글씨 크게';smallText.setAttribute('aria-pressed',size==='small');normalText.setAttribute('aria-pressed',size==='normal');try{localStorage.setItem('keypad-text-size',size);}catch{}}
smallText.onclick=()=>setTextSize('small');normalText.onclick=()=>setTextSize('normal');largeText.onclick=()=>setTextSize(largeEnabled?'normal':'large');
let textSize=largeEnabled?'large':'normal';try{textSize=localStorage.getItem('keypad-text-size')||textSize;}catch{}setTextSize(textSize);
easyStyle.textContent+=`html[data-text-size="small"] body{font-size:14px}html[data-text-size="small"] .easy-action strong,html[data-text-size="small"] .key-name{font-size:14px!important}html[data-text-size="small"] .easy-selection,html[data-text-size="small"] .easy-selection small,html[data-text-size="small"] .easy-picker>p,html[data-text-size="small"] .easy-help,html[data-text-size="small"] .easy-save p,html[data-text-size="small"] .easy-welcome p{font-size:14px}html[data-text-size="small"] .easy-action span{font-size:12px}html[data-text-size="small"] .easy-button{font-size:15px}html[data-text-size="small"] .easy-device-title,html[data-text-size="small"] .easy-picker h2,html[data-text-size="small"] .keyboard-heading h2{font-size:18px}.meta{flex-wrap:wrap;justify-content:flex-end}`;
const updateButton=makeButton('업데이트','quiet');updateButton.id='app-update';$('.meta').append(updateButton);
updateButton.onclick=()=>{if(hwBusy||pendingSave||recording||dirty()||macroDirty||easyChanges()>0){toast('바꾼 설정을 키보드에 저장한 뒤 업데이트해주세요.');return;}hostSend('update');};
