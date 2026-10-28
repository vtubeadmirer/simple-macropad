const assert=require('assert/strict'),fs=require('fs'),C=require('./hardware-codec');
const empty=Array.from({length:32},()=>[]);
const lists=structuredClone(empty);lists[0]=[{type:2,code:224,down:true,delay:10},{type:2,code:6,down:true,delay:20},{type:2,code:6,down:false,delay:0},{type:2,code:224,down:false,delay:60000}];lists[15]=[{type:4,code:255,down:false,delay:8}];lists[31]=[{type:3,code:1,down:false,delay:0}];
const bytes=C.writeMacros(lists);assert.deepEqual(bytes.slice(64,80),[10,0,66,224,20,0,66,6,0,0,2,6,96,234,130,224]);assert.deepEqual(C.readMacros(bytes),lists);
assert.throws(()=>C.readMacros(Array(4096).fill(1)),/주소/);
let malformed=Array(4096).fill(2);malformed[0]=64;malformed[1]=0;assert.throws(()=>C.readMacros(malformed),/종료/);
let huge=structuredClone(empty);huge[0]=Array(1009).fill({type:2,code:4,down:false,delay:0});assert.throws(()=>C.writeMacros(huge),/용량/);
let invalid=structuredClone(empty);invalid[0]=[{type:2,code:999,down:false,delay:-1}];assert.throws(()=>C.writeMacros(invalid),/오류/);
assert.deepEqual(C.encode({key:'M15',mods:[]}),[96,15,1,0]);assert.deepEqual(C.encode({key:'←',mods:['Ctrl','Win']}),[32,9,80,0]);assert.equal(C.encode({key:'',mods:[]}),null);
assert.throws(()=>C.encode({key:'가로 스크롤 오른쪽',mods:[]}),/지원/);
for(const raw of [[32,64,0,0],[96,0,7,2],[255,3,2,1],[16,0,0,255],[32,9,80,0]])assert.deepEqual(C.encode(C.decode(raw)),raw);
let edit=C.decode([32,9,80,0]);edit.key='S';assert.deepEqual(C.encode(edit),[32,9,22,0]);
if(process.argv[2]){const s=JSON.parse(fs.readFileSync(process.argv[2],'utf8').replace(/^\uFEFF/,''));assert.equal(s.keys.length,88);for(let i of C.slots){let raw=s.keys.slice(i*4,i*4+4);assert.deepEqual(C.encode(C.decode(raw)),raw);}const decoded=C.readMacros(s.macros);assert.deepEqual(C.readMacros(C.writeMacros(decoded)),decoded);console.log('Hardware fixture:',C.slots.map(i=>C.decode(s.keys.slice(i*4,i*4+4)).key).join(', '));console.log('Macro lengths:',decoded.map(x=>x.length).join(','));}
console.log('PASS: wire fixtures, raw preservation, modifiers, macro flags/delays, bounds, capacity, end markers, upper slots.');
