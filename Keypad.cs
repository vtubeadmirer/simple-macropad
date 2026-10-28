using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Xml.Linq;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
[assembly:System.Reflection.AssemblyVersion("0.10.0.0")]
[assembly:System.Reflection.AssemblyFileVersion("0.10.0.0")]

namespace KeypadDesktop {
 public class Entry { public string name=""; public string key=""; public string[] mods=new string[0]; public string signal=""; public string source=""; public int[] raw;public string rawCombo=""; }
 public class State { public Entry[] entries=Enumerable.Range(0,18).Select(i=>new Entry()).ToArray(); public string theme=""; public bool migrated; public int[] macroDraft; public int? backupProfile; }
 static class Program {
  [STAThread]static void Main(string[] args){
   if(args.Length==2&&(args[0]=="--device-backup"||args[0]=="--verify-unchanged-write")){try{using(var pad=new PadHardware()){var s=args[0]=="--device-backup"?pad.Read():pad.VerifyUnchangedWrite(args[1]);if(args[0]=="--device-backup")PadHardware.Backup(s,args[1]);File.WriteAllText(Path.Combine(args[1],"result.json"),new JavaScriptSerializer().Serialize(s));}}catch(Exception ex){Directory.CreateDirectory(args[1]);File.WriteAllText(Path.Combine(args[1],"error.txt"),ex.ToString());Environment.ExitCode=1;}return;}
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new App(args.Contains("--self-test")));}
 }
 class App:Form {
  readonly JavaScriptSerializer json=new JavaScriptSerializer();readonly WebView2 web=new WebView2();
  readonly string dataDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SimpleMacroPad","Desktop");
  readonly Dictionary<IntPtr,HashSet<int>> held=new Dictionary<IntPtr,HashSet<int>>();
  State state=new State();Timer scan;bool ready,closing,test;RawWindow raw;DateTime captureUntil=DateTime.MinValue;
  IntPtr keyboardHook=IntPtr.Zero;HookProc hookProc;readonly HashSet<int> hookHeld=new HashSet<int>();
  const string AppVersion="0.10.0";bool failNextSave,hardwareBusy;
  string Profile {get{return Path.Combine(dataDir,"profile.json");}}
  public App(bool testing){test=testing;if(test)dataDir=Path.Combine(Path.GetTempPath(),"opencode","KeypadTest-"+Guid.NewGuid().ToString("N"));Text="키패드 v"+AppVersion;ClientSize=new Size(1180,940);MinimumSize=new Size(900,720);StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(245,245,243);web.Dock=DockStyle.Fill;Controls.Add(web);Shown+=async delegate{await Start();};Deactivate+=delegate{if(!CaptureArmed)held.Clear();};FormClosing+=OnClosing;}
  async System.Threading.Tasks.Task Start(){try{
   Directory.CreateDirectory(dataDir);LoadProfile();
   var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(dataDir,test?"TestWebView":"WebView"));
   await web.EnsureCoreWebView2Async(environment);
   web.CoreWebView2.Settings.AreDevToolsEnabled=false;web.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;web.CoreWebView2.Settings.IsStatusBarEnabled=false;
   web.CoreWebView2.SetVirtualHostNameToFolderMapping("keypad.local",AppDomain.CurrentDomain.BaseDirectory,CoreWebView2HostResourceAccessKind.DenyCors);
   web.CoreWebView2.NavigationStarting+=(s,e)=>{if(!e.Uri.StartsWith("https://keypad.local/",StringComparison.OrdinalIgnoreCase))e.Cancel=true;};
   web.CoreWebView2.NewWindowRequested+=(s,e)=>e.Handled=true;
   web.CoreWebView2.PermissionRequested+=(s,e)=>e.State=CoreWebView2PermissionState.Deny;
   web.CoreWebView2.WebMessageReceived+=Message;
   raw=new RawWindow(this);Register(raw.Handle);
   scan=new Timer{Interval=1000};scan.Tick+=delegate{Connection();if(captureUntil!=DateTime.MinValue&&!CaptureArmed){ArmCapture(false);Send("captureExpired",null);}};scan.Start();
   web.Source=new Uri("https://keypad.local/index.html");
  }catch(Exception ex){MessageBox.Show("앱을 시작하지 못했습니다. WebView2 Runtime 설치 상태를 확인해주세요.\n"+ex.Message);closing=true;Close();}}
  void LoadProfile(){if(File.Exists(Profile)){state=json.Deserialize<State>(File.ReadAllText(Profile));Validate(state.entries);return;}
   string old=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SimpleMacroPad","profile.xml");if(!File.Exists(old))return;
   var entries=XDocument.Load(old).Descendants("PadEntry").Take(18).ToArray();for(int i=0;i<entries.Length;i++){var x=entries[i];var signal=(string)x.Element("Signal")??"";var e=state.entries[i];e.name=(string)x.Element("Name")??"";e.signal=signal;if(signal.StartsWith("K:")){string[] p=signal.Split(':');int m,v;if(p.Length==3&&int.TryParse(p[1],out m)&&int.TryParse(p[2],out v)){e.mods=Modifiers(m);e.key=KeyName(v);}}else if(signal.Length>0)e.key=(string)x.Element("Shortcut")??signal;}state.migrated=true;
  }
  static void Validate(Entry[] entries){if(entries==null||entries.Length!=18)throw new Exception("배치 개수가 올바르지 않습니다.");foreach(var e in entries){if(e==null||e.name==null||e.name.Length>80||e.key==null||e.key.Length>512||e.signal==null||e.signal.Length>2048||e.mods==null||e.mods.Length>4||e.mods.Any(m=>!new[]{"Ctrl","Shift","Alt","Win"}.Contains(m)))throw new Exception("배치 형식이 올바르지 않습니다.");}}
  void Save(State next){Validate(next.entries);if(test&&failNextSave){failNextSave=false;throw new IOException("Simulated save failure");}string tmp=Profile+".tmp";File.WriteAllText(tmp,json.Serialize(next),Encoding.UTF8);if(File.Exists(Profile))File.Replace(tmp,Profile,Profile+".bak");else File.Move(tmp,Profile);}
  async void Message(object sender,CoreWebView2WebMessageReceivedEventArgs e){try{if(!e.Source.StartsWith("https://keypad.local/",StringComparison.OrdinalIgnoreCase))return;var m=json.Deserialize<Dictionary<string,object>>(e.WebMessageAsJson);string type=(string)m["type"];object data=m.ContainsKey("data")?m["data"]:null;
   if(type=="ready"){ready=true;await web.ExecuteScriptAsync("Desktop.initialize("+json.Serialize(new{entries=state.entries,macroDraft=state.macroDraft,backupProfile=state.backupProfile,selfTest=test,theme=state.theme,migrated=state.migrated,version=AppVersion,exePath=Application.ExecutablePath,profilePath=Profile})+")");Connection();if(test)await SelfTest();}
   else if(type=="hardwareRead"||type=="hardwareApply"){
    if(test)throw new Exception("Self-test must never access hardware");if(hardwareBusy)return;hardwareBusy=true;ArmCapture(false);
    try{var request=type=="hardwareApply"?json.Deserialize<PadApply>(json.Serialize(data)):null;
     var snapshot=await System.Threading.Tasks.Task.Run(()=>{using(var pad=new PadHardware()){if(request!=null)return pad.Apply(request,Path.Combine(dataDir,"DeviceBackups"));var s=pad.Read();PadHardware.Backup(s,Path.Combine(dataDir,"DeviceBackups"));return s;}});
     Send(type=="hardwareRead"?"hardwareLoaded":"hardwareApplied",snapshot);
    }catch(Exception ex){Send("hardwareFailed",ex.Message);}finally{hardwareBusy=false;}
   }
   else if(type=="hardwareBackupOpen"){
    if(hardwareBusy)return;using(var dialog=new OpenFileDialog{Filter="키보드 백업 (*.json)|*.json",InitialDirectory=Path.Combine(dataDir,"DeviceBackups")}){if(dialog.ShowDialog(this)==DialogResult.OK){var file=new FileInfo(dialog.FileName);if(file.Length>1000000)throw new Exception("백업 파일이 너무 큽니다.");var s=json.Deserialize<PadSnapshot>(File.ReadAllText(dialog.FileName));if(s==null||s.device!="0816:2473"||s.keys==null||s.keys.Length!=88||s.macros==null||s.macros.Length!=4096||s.keys.Concat(s.macros).Any(x=>x<0||x>255)||s.profile<0||s.profile>=6||s.layer!=0)throw new Exception("이 키보드의 백업 파일이 아닙니다.");Send("hardwareBackupLoaded",s);}}
   }
    else if(type=="update"){
     if(test||hardwareBusy)return;
     if(await web.ExecuteScriptAsync("Desktop.canClose()&&!dirty()&&!macroDirty&&easyChanges()<=0")!="true")return;
     string script=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Update.ps1");
     System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -File \""+script+"\" -CurrentExe \""+Application.ExecutablePath+"\" -AppProcessId "+System.Diagnostics.Process.GetCurrentProcess().Id){UseShellExecute=false,CreateNoWindow=true});closing=true;Close();
    }
    else if(type=="capture"){ArmCapture(data is bool&&(bool)data);}
   else if(type=="save"){var d=json.Deserialize<Dictionary<string,object>>(json.Serialize(data));int sequence=Convert.ToInt32(d["sequence"]);try{var entries=json.Deserialize<Entry[]>(json.Serialize(d["entries"]));Validate(entries);var macro=d.ContainsKey("macroDraft")?json.Deserialize<int[]>(json.Serialize(d["macroDraft"])):state.macroDraft;if(macro!=null&&(macro.Length!=4096||macro.Any(x=>x<0||x>255)))throw new Exception("매크로 데이터 형식 오류");int? backupProfile=d.ContainsKey("backupProfile")&&d["backupProfile"]!=null?(int?)Convert.ToInt32(d["backupProfile"]):null;var next=new State{entries=entries,macroDraft=macro,backupProfile=backupProfile,theme=state.theme,migrated=false};Save(next);state=next;Send("saved",sequence);}catch(Exception ex){Send("saveFailed",new{sequence=sequence,message=ex.Message});}}
   else if(type=="theme"){string theme=data as string;if(theme=="dark"||theme=="light"){var next=new State{entries=state.entries,macroDraft=state.macroDraft,backupProfile=state.backupProfile,theme=theme,migrated=state.migrated};Save(next);state=next;}}
   else if(type=="export"){var entries=json.Deserialize<Entry[]>(json.Serialize(data));Validate(entries);using(var dialog=new SaveFileDialog{Filter="키패드 배치 (*.json)|*.json",FileName="내 키패드 배치.json"}){if(dialog.ShowDialog(this)==DialogResult.OK)File.WriteAllText(dialog.FileName,json.Serialize(new State{entries=entries,macroDraft=state.macroDraft,theme=state.theme}),Encoding.UTF8);}}
  }catch(Exception ex){Send("error","저장 또는 처리 실패: "+ex.Message);if(test){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"desktop-test.json"),json.Serialize(new{error=ex.ToString()}));closing=true;Close();}}}
  void Send(string method,object value){if(ready&&!IsDisposed)web.CoreWebView2.ExecuteScriptAsync("Desktop."+method+"("+json.Serialize(value)+")");}
   async void OnClosing(object sender,FormClosingEventArgs e){if(closing||!ready)return;e.Cancel=true;if(hardwareBusy){MessageBox.Show(this,"키보드 통신이 끝난 뒤 종료해주세요.");return;}try{string safe=await web.ExecuteScriptAsync("Desktop.canClose()");if(safe!="true"&&MessageBox.Show(this,"저장하지 않은 변경 또는 저장 중인 내용이 있어요. 종료할까요?","종료 확인",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;closing=true;Close();}catch{closing=true;Close();}}
  async System.Threading.Tasks.Task SelfTest(){
   await System.Threading.Tasks.Task.Delay(500);
   if(AcceptInput(false))throw new Exception("Background input enabled without capture");
   await web.ExecuteScriptAsync("document.querySelector('#capture').click();window.dispatchEvent(new Event('blur'));");await System.Threading.Tasks.Task.Delay(100);
   if(!AcceptInput(false)||await web.ExecuteScriptAsync("capturing")!="true")throw new Exception("Background capture/blur policy failed");
   await web.ExecuteScriptAsync("Desktop.input({mods:['Ctrl','Shift'],key:'S',signal:'K:3:83'});document.querySelector('#name').value='Test';document.querySelector('#name').dispatchEvent(new Event('input'));document.querySelector('#save').click();");
   await System.Threading.Tasks.Task.Delay(500);
   await web.ExecuteScriptAsync("if(document.documentElement.dataset.theme!=='dark')document.querySelector('#theme-toggle').click()");await System.Threading.Tasks.Task.Delay(150);
   if(AcceptInput(false))throw new Exception("Capture did not disarm after input");
   var disk=json.Deserialize<State>(File.ReadAllText(Profile));if(disk.entries[0].key!="S"||disk.entries[0].signal!="K:3:83"||disk.theme!="dark")throw new Exception("Persistence self-test failed");
   await web.ExecuteScriptAsync("document.querySelector('#capture').click()");await System.Threading.Tasks.Task.Delay(100);captureUntil=DateTime.UtcNow.AddSeconds(-1);if(AcceptInput(false))throw new Exception("Capture expiry policy failed");await web.ExecuteScriptAsync("Desktop.captureExpired(null)");await System.Threading.Tasks.Task.Delay(100);
   if(await web.ExecuteScriptAsync("capturing")!="false")throw new Exception("Capture expiry UI failed");
   foreach(var sample in new[]{new[]{91,160,83},new[]{91,9},new[]{162,91,37},new[]{162,91,39},new[]{44}}){
    await web.ExecuteScriptAsync("document.querySelector('#capture').click()");await System.Threading.Tasks.Task.Delay(100);
    if(keyboardHook==IntPtr.Zero)throw new Exception("System keyboard hook installation failed");
    foreach(int key in sample)HookKey(key,false);
    await System.Threading.Tasks.Task.Delay(150);
    if(await web.ExecuteScriptAsync("capturing")!="false")throw new Exception("System combination capture failed");
    if(keyboardHook!=IntPtr.Zero)throw new Exception("System keyboard hook not removed");
    await web.ExecuteScriptAsync("move(0)");
   }
   await web.ExecuteScriptAsync("document.querySelector('#capture').click()");await System.Threading.Tasks.Task.Delay(100);Wheel(0x400,120);await System.Threading.Tasks.Task.Delay(100);
   if(await web.ExecuteScriptAsync("draft.signal==='M:V:+'&&!capturing")!="true")throw new Exception("Vertical wheel capture failed");
   await web.ExecuteScriptAsync("move(0)");
   await web.ExecuteScriptAsync("beginScan();Desktop.input({mods:[],key:'A',signal:'K:0:65'});Desktop.input({mods:[],key:'B',signal:'K:0:66'});");await System.Threading.Tasks.Task.Delay(350);
   if(await web.ExecuteScriptAsync("scanCandidate===null&&document.querySelector('#scan-next').disabled")!="true")throw new Exception("Multiple-input guard failed");
   await web.ExecuteScriptAsync("document.querySelector('#scan-retry').click();");
   for(int i=0;i<18;i++){
    if(i==1){await web.ExecuteScriptAsync("document.querySelector('#scan-skip').click();");continue;}
     if(i==3)await web.ExecuteScriptAsync("document.querySelector('#scan-presets .chip').click()");else if(i==12)Wheel(0x400,120);else if(i==14)Wheel(0x400,-120);else if(i==15)Wheel(0x800,-120);else if(i==17)Wheel(0x800,120);else await web.ExecuteScriptAsync("Desktop.input({mods:[],key:'A',signal:'K:0:65'})");await System.Threading.Tasks.Task.Delay(350);
    if(await web.ExecuteScriptAsync("scanCandidate!==null")!="true")throw new Exception("Wizard input failed at "+i);
    await web.ExecuteScriptAsync("document.querySelector('#scan-next').click()");
   }
   if(json.Deserialize<State>(File.ReadAllText(Profile)).entries[0].key!="S")throw new Exception("Wizard changed profile before confirmation");
   if(await web.ExecuteScriptAsync("scanIndex===18&&scanChanged.size===17")!="true")throw new Exception("Wizard review failed");
   await web.ExecuteScriptAsync("document.querySelector('#scan-next').click()");await System.Threading.Tasks.Task.Delay(500);
   disk=json.Deserialize<State>(File.ReadAllText(Profile));if(disk.entries[0].key!="A"||disk.entries[3].source!="manual"||disk.entries[12].signal!="M:V:+"||disk.entries[14].signal!="M:V:-"||disk.entries[15].signal!="M:H:-"||disk.entries[17].signal!="M:H:+")throw new Exception("Wizard/wheel/manual save failed");
   string beforeFailure=File.ReadAllText(Profile);failNextSave=true;
   await web.ExecuteScriptAsync("document.querySelector('#name').value='retry-test';document.querySelector('#name').dispatchEvent(new Event('input'));document.querySelector('#save').click()");await System.Threading.Tasks.Task.Delay(500);
   if(File.ReadAllText(Profile)!=beforeFailure||state.entries[0].name=="retry-test")throw new Exception("Failed save changed confirmed state");
   if(await web.ExecuteScriptAsync("failedSave&&!pendingSave&&!document.querySelector('#save').disabled&&!Desktop.canClose()")!="true")throw new Exception("Failed save retry UI failed");
   await web.ExecuteScriptAsync("document.querySelector('#save').click()");await System.Threading.Tasks.Task.Delay(500);
   if(json.Deserialize<State>(File.ReadAllText(Profile)).entries[0].name!="retry-test")throw new Exception("Retry persistence failed");
   await web.ExecuteScriptAsync("draft={source:entries[selected].source,key:entries[selected].key,mods:[...entries[selected].mods].reverse(),name:entries[selected].name,signal:entries[selected].signal};renderEditor()");
     if(await web.ExecuteScriptAsync("!dirty()&&document.querySelector('#save').disabled&&document.querySelector('#app-version').textContent==='v0.10.0'")!="true")throw new Exception("Version or semantic equality test failed");
   await web.ExecuteScriptAsync("macroLists=Array.from({length:32},()=>[]);macroLists[2]=[{type:2,code:4,down:true,delay:10},{type:2,code:4,down:false,delay:20}];macroChanged();draft.raw=[32,0,4,0];draft.rawCombo=PC.signature(draft);document.querySelector('#macro-save').click()");await System.Threading.Tasks.Task.Delay(500);
   disk=json.Deserialize<State>(File.ReadAllText(Profile));if(disk.macroDraft==null||disk.macroDraft.Length!=4096||disk.macroDraft[4]!=64||disk.macroDraft[66]!=66||disk.macroDraft[70]!=130||disk.entries[0].raw==null||disk.entries[0].raw[0]!=32)throw new Exception("Macro/raw persistence failed");
   string result=await web.ExecuteScriptAsync("({version:document.querySelector('#app-version').textContent,keys:document.querySelectorAll('.pad-key').length,knobs:document.querySelectorAll('.knob-action').length,ready:desktopReady,connected:document.querySelector('#device-status').textContent,theme:document.documentElement.dataset.theme,saved:lastSaved===saveSequence,wizardSteps:scanIndex,detected:scanChanged.size,closed:!wizard.open,saveRetryTest:true,manualPresetTest:true})");
   File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"desktop-test.json"),result);closing=true;Close();
  }
  static string[] Modifiers(int m){var a=new List<string>();if((m&2)!=0)a.Add("Ctrl");if((m&1)!=0)a.Add("Shift");if((m&4)!=0)a.Add("Alt");if((m&8)!=0)a.Add("Win");return a.ToArray();}
  static string KeyName(int v){if(v>=65&&v<=90)return ((char)v).ToString();if(v>=48&&v<=57)return ((char)v).ToString();var map=new Dictionary<int,string>{{27,"Esc"},{32,"Space"},{8,"Backspace"},{9,"Tab"},{13,"Enter"},{20,"CapsLock"},{37,"←"},{38,"↑"},{39,"→"},{40,"↓"},{44,"PrtSc"},{46,"Delete"},{192,"`"},{189,"-"},{187,"="},{219,"["},{221,"]"},{220,"\\"},{186,";"},{222,"'"},{188,","},{190,"."},{191,"/"}};return map.ContainsKey(v)?map[v]:((Keys)v).ToString();}
  string DeviceName(IntPtr d){uint n=0;GetRawInputDeviceInfo(d,0x20000007,null,ref n);if(n==0||n>4096)return "";var b=new StringBuilder((int)n+1);return GetRawInputDeviceInfo(d,0x20000007,b,ref n)==uint.MaxValue?"":b.ToString();}
  bool IsPad(IntPtr d){return DeviceName(d).IndexOf("VID_0816&PID_2473",StringComparison.OrdinalIgnoreCase)>=0;}
  void Connection(){uint count=0,size=(uint)Marshal.SizeOf(typeof(Device));if(GetRawInputDeviceList(null,ref count,size)==uint.MaxValue)return;var devices=new Device[count];bool found=false;if(GetRawInputDeviceList(devices,ref count,size)!=uint.MaxValue)foreach(var d in devices)if(IsPad(d.handle))found=true;Send("connection",found);}
  void Register(IntPtr h){var list=new[]{new Registration{page=1,usage=6,flags=0x100,target=h},new Registration{page=1,usage=2,flags=0x100,target=h},new Registration{page=12,usage=1,flags=0x100,target=h},new Registration{page=1,usage=128,flags=0x100,target=h}};if(!RegisterRawInputDevices(list,(uint)list.Length,(uint)Marshal.SizeOf(typeof(Registration))))throw new Exception("입력 감지 등록 실패");}
  bool CaptureArmed {get{return DateTime.UtcNow<captureUntil;}}
  void ArmCapture(bool enable){captureUntil=enable?DateTime.UtcNow.AddMinutes(2):DateTime.MinValue;if(enable){Register(raw.Handle);if(keyboardHook==IntPtr.Zero){hookHeld.Clear();hookProc=Hook;keyboardHook=SetWindowsHookEx(13,hookProc,GetModuleHandle(null),0);if(keyboardHook==IntPtr.Zero)Send("error","Windows 단축키 보조 감지를 켜지 못했어요. 기능 직접 지정을 이용하세요.");}}else{if(keyboardHook!=IntPtr.Zero){UnhookWindowsHookEx(keyboardHook);keyboardHook=IntPtr.Zero;}hookHeld.Clear();held.Clear();}}
  static bool SystemCombo(int m,int key){return key==44||(key==83&&(m&9)==9)||(key==9&&(m&8)!=0)||((key==37||key==39)&&(m&10)==10);}
  void HookKey(int key,bool up){if(up){hookHeld.Remove(key);return;}if(!hookHeld.Add(key))return;int m=(Has(hookHeld,16,160,161)?1:0)|(Has(hookHeld,17,162,163)?2:0)|(Has(hookHeld,18,164,165)?4:0)|(Has(hookHeld,91,92)?8:0);if(!CaptureArmed||!SystemCombo(m,key))return;var input=new{mods=Modifiers(m),key=KeyName(key),signal="K:"+m+":"+key,source="system"};BeginInvoke(new Action(delegate{if(CaptureArmed)Send("input",input);}));}
  IntPtr Hook(int code,IntPtr message,IntPtr data){try{if(code>=0&&CaptureArmed){int msg=message.ToInt32();if(msg==0x100||msg==0x104||msg==0x101||msg==0x105){int key=Marshal.ReadInt32(data);HookKey(key,msg==0x101||msg==0x105);}}}catch{}return CallNextHookEx(keyboardHook,code,message,data);}
  bool AcceptInput(bool foreground){return ready&&(foreground||CaptureArmed);}
  void Input(IntPtr handle){if(!AcceptInput(GetForegroundWindow()==Handle))return;uint n=0;int hs=Marshal.SizeOf(typeof(Header));GetRawInputData(handle,0x10000003,IntPtr.Zero,ref n,(uint)hs);if(n<hs||n>65536)return;IntPtr ptr=Marshal.AllocHGlobal((int)n);try{if(GetRawInputData(handle,0x10000003,ptr,ref n,(uint)hs)!=n)return;var h=(Header)Marshal.PtrToStructure(ptr,typeof(Header));if(!IsPad(h.device))return;var b=new byte[n];Marshal.Copy(ptr,b,0,(int)n);
   if(h.type==1&&n>=hs+16){int flags=BitConverter.ToUInt16(b,hs+2),v=BitConverter.ToUInt16(b,hs+6);HashSet<int> down;if(!held.TryGetValue(h.device,out down)){down=new HashSet<int>();held[h.device]=down;}if((flags&1)!=0){down.Remove(v);return;}if(!down.Add(v)||v==255||v==16||v==17||v==18||v==91||v==92||(v>=160&&v<=165))return;int m=(Has(down,16,160,161)?1:0)|(Has(down,17,162,163)?2:0)|(Has(down,18,164,165)?4:0)|(Has(down,91,92)?8:0);if(CaptureArmed&&keyboardHook!=IntPtr.Zero&&SystemCombo(m,v))return;Send("input",new{mods=Modifiers(m),key=KeyName(v),signal="K:"+m+":"+v});}
   else if(h.type==0&&n>=hs+24){int flags=BitConverter.ToUInt16(b,hs+4),delta=BitConverter.ToInt16(b,hs+6);Wheel(flags,delta);}
   else if(h.type==2&&n>=hs+8){int len=BitConverter.ToInt32(b,hs),count=BitConverter.ToInt32(b,hs+4);if(len<=0||count<=0||(long)len*count>n-hs-8)return;for(int i=0;i<count;i++){int offset=hs+8+i*len;bool nonzero=false;for(int j=1;j<len;j++)if(b[offset+j]!=0)nonzero=true;if(!nonzero)continue;string signal=BitConverter.ToString(b,offset,len);Send("input",new{mods=new string[0],key="미디어 "+signal,signal="H:"+signal});}}
  }catch(Exception ex){Send("error","입력 해석 실패: "+ex.Message);}finally{Marshal.FreeHGlobal(ptr);}}
  void Wheel(int flags,int delta){if(delta==0)return;foreach(int axis in new[]{0x400,0x800})if((flags&axis)!=0){bool vertical=axis==0x400;string key=vertical?(delta>0?"스크롤 위":"스크롤 아래"):(delta>0?"가로 스크롤 오른쪽":"가로 스크롤 왼쪽");Send("input",new{mods=new string[0],key=key,signal="M:"+(vertical?"V:":"H:")+(delta>0?"+":"-")});}}
  static bool Has(HashSet<int> set,params int[] keys){return keys.Any(set.Contains);}
  protected override void Dispose(bool disposing){if(disposing){ArmCapture(false);if(scan!=null)scan.Dispose();if(raw!=null)raw.DestroyHandle();}base.Dispose(disposing);}
  class RawWindow:NativeWindow{App app;public RawWindow(App a){app=a;CreateHandle(new CreateParams{Caption="KeypadRawInput"});}protected override void WndProc(ref Message m){if(m.Msg==0xff)app.Input(m.LParam);base.WndProc(ref m);}}
  [StructLayout(LayoutKind.Sequential)]struct Registration{public ushort page,usage;public uint flags;public IntPtr target;}
  [StructLayout(LayoutKind.Sequential)]struct Header{public uint type,size;public IntPtr device,param;}
  [StructLayout(LayoutKind.Sequential)]struct Device{public IntPtr handle;public uint type;}
  [DllImport("user32.dll",SetLastError=true)]static extern bool RegisterRawInputDevices(Registration[] d,uint n,uint size);
  [DllImport("user32.dll")]static extern uint GetRawInputData(IntPtr input,uint command,IntPtr data,ref uint n,uint size);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint GetRawInputDeviceInfo(IntPtr device,uint command,StringBuilder data,ref uint n);
  [DllImport("user32.dll")]static extern uint GetRawInputDeviceList([In,Out] Device[] d,ref uint n,uint size);
  [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
  delegate IntPtr HookProc(int code,IntPtr message,IntPtr data);
  [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetWindowsHookEx(int kind,HookProc proc,IntPtr module,uint thread);
  [DllImport("user32.dll")]static extern bool UnhookWindowsHookEx(IntPtr hook);
  [DllImport("user32.dll")]static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);
 }
}
