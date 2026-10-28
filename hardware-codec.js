(function(root){
 'use strict';
 const slots=[0,1,2,3,4,5,6,7,8,9,10,11,18,16,17,21,19,20];
 const hid={Enter:40,Esc:41,Backspace:42,Tab:43,Space:44,'-':45,'=':46,'[':47,']':48,'\\':49,';':51,"'":52,'`':53,',':54,'.':55,'/':56,CapsLock:57,PrtSc:70,ScrollLock:71,Pause:72,Insert:73,Home:74,PageUp:75,Delete:76,End:77,PageDown:78,'→':79,'←':80,'↓':81,'↑':82,NumLock:83,Divide:84,Multiply:85,Subtract:86,Add:87,NumEnter:88,Decimal:99,Menu:101,LCtrl:224,LShift:225,LAlt:226,LWin:227,RCtrl:228,RShift:229,RAlt:230,RWin:231};
 for(let i=0;i<26;i++)hid[String.fromCharCode(65+i)]=4+i;
 for(let i=1;i<=9;i++){hid[String(i)]=29+i;hid['NumPad'+i]=88+i;}hid['0']=39;hid.NumPad0=98;
 for(let i=1;i<=24;i++)hid['F'+i]=i<=12?57+i:91+i;
 const inverse=Object.fromEntries(Object.entries(hid).map(([k,v])=>[v,k]));
 const media={'음소거':226,'볼륨 +':233,'볼륨 -':234,'재생/일시정지':205,'다음 트랙':181,'이전 트랙':182,'정지':183};
 const mouse={'마우스 왼쪽':[16,1,0,0],'마우스 오른쪽':[16,2,0,0],'마우스 가운데':[16,4,0,0],'스크롤 위':[16,0,0,1],'스크롤 아래':[16,0,0,255]};
 const signature=e=>JSON.stringify([['Ctrl','Shift','Alt','Win'].filter(m=>(e.mods||[]).includes(m)),e.key||'']);
 function decode(raw){let [t,a,b,c]=raw,e={name:'',key:'',mods:[],signal:'',source:'device-memory',raw:[...raw],rawCombo:''};
  if(t===32){for(const [m,mask] of [['Ctrl',1],['Shift',2],['Alt',4],['Win',8]])if(a&mask)e.mods.push(m);
   if(a&240){e.mods=[];e.key=[...['LCtrl','LShift','LAlt','LWin','RCtrl','RShift','RAlt','RWin'].filter((_,i)=>a&(1<<i)),...(b?[inverse[b]||'HID '+b]:[])].join(' + ');}
   else e.key=inverse[b]||(b?'HID '+b:(a?'보조키만':''));
  }else if(t===96)e.key='M'+a;
  else if(t===48)e.key=Object.keys(media).find(k=>media[k]===(a|(b<<8)))||'미디어 '+(a|(b<<8));
  else if(t===19)e.key='사용 안 함';
  else if(t===16)e.key=Object.keys(mouse).find(k=>mouse[k].every((v,i)=>v===raw[i]))||'마우스 '+raw.slice(1).join('/');
  else e.key='원본 '+raw.map(v=>v.toString(16).padStart(2,'0')).join(' ');
  e.rawCombo=signature(e);return e;
 }
 function encode(e){if(e.raw&&e.raw.length===4&&signature(e)===e.rawCombo)return [...e.raw];
  const mask=(e.mods||[]).reduce((a,m)=>a|({Ctrl:1,Shift:2,Alt:4,Win:8}[m]||0),0);
  if(!e.key)return mask?[32,mask,0,0]:null; // blank preserves; a modifier on its own is a valid key
  if(e.key==='사용 안 함')return [19,0,0,0];
  if(/^M(?:[0-9]|1[0-5])$/.test(e.key)){if(mask)throw Error('매크로 지정에는 보조키를 함께 선택할 수 없습니다.');return [96,Number(e.key.slice(1)),1,0];}
  if(media[e.key]!==undefined){if(mask)throw Error('미디어 키에는 보조키를 함께 선택할 수 없습니다.');let c=media[e.key];return [48,c&255,c>>8,0];}
  if(mouse[e.key]){if(mask)throw Error('마우스 조합은 매크로로 작성해주세요.');return [...mouse[e.key]];}
  if(hid[e.key]!==undefined){const code=hid[e.key];return code>=224?[32,mask|(1<<(code-224)),0,0]:[32,mask,code,0];}
  throw Error('온보드 변환을 지원하지 않는 입력: '+e.key+' · 키를 다시 선택하거나 원본을 유지해주세요.');
 }
 function readMacros(bytes){if(!bytes||bytes.length!==4096)throw Error('매크로 데이터 길이 오류');let lists=[];
  for(let slot=0;slot<32;slot++){let p=bytes[slot*2]|bytes[slot*2+1]<<8,list=[];if(p===65535||p===0){lists.push(list);continue;}
   if(p<64||p>4092||p%4)throw Error('M'+slot+' 매크로 주소 오류');let ended=false;
   for(;p<=4092;p+=4){const flags=bytes[p+2],type=flags&63;if(flags===0){ended=true;break;}if(![2,3,4,5].includes(type))throw Error('M'+slot+' 알 수 없는 매크로 이벤트');list.push({type,code:bytes[p+3],down:!!(flags&64),delay:bytes[p]|bytes[p+1]<<8});if(flags&128){ended=true;break;}}
   if(!ended)throw Error('M'+slot+' 종료 표시 없음');lists.push(list);
  }return lists;
 }
 function writeMacros(lists){if(lists.length!==32)throw Error('매크로 슬롯 개수 오류');const b=Array(64).fill(255).concat(Array(4032).fill(0));let pos=64;
  lists.forEach((list,slot)=>{if(!list.length)return;if(pos+list.length*4>4096)throw Error('매크로 용량 초과: 전체 최대 1008개 동작');b[slot*2]=pos&255;b[slot*2+1]=pos>>8;
   list.forEach((s,i)=>{if(!s||typeof s.down!=='boolean'||![2,3,4,5].includes(s.type)||!Number.isInteger(s.code)||s.code<0||s.code>255||!Number.isInteger(s.delay)||s.delay<0||s.delay>60000)throw Error('매크로 코드 또는 지연 값 오류');b[pos++]=s.delay&255;b[pos++]=s.delay>>8;b[pos++]=s.type|(s.down?64:0)|(i===list.length-1?128:0);b[pos++]=s.code;});});return b;
 }
 function eventName(e){return e.type===2?(inverse[e.code]||'HID '+e.code):e.type===3?({1:'왼쪽 클릭',2:'오른쪽 클릭',4:'가운데 클릭',8:'앞으로',16:'뒤로'}[e.code]||'마우스 '+e.code):e.type===4?(e.code===255?'스크롤 아래':'스크롤 위'):(e.code===255?'가로 왼쪽':'가로 오른쪽');}
 const api={slots,hid,inverse,media,mouse,signature,decode,encode,readMacros,writeMacros,eventName};root.PadCodec=api;if(typeof module!=='undefined')module.exports=api;
})(typeof window!=='undefined'?window:globalThis);
