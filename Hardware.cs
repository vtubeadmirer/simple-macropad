using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;
using HidLibrary;

namespace KeypadDesktop {
 public class PadSnapshot {
  public string device="0816:2473",serial="",captured="",backup="";
  public int profile,layer,profiles,layers,firmware;
  public int[] info,keys,macros;
 }
 public class PadApply { public PadSnapshot baseline;public int[] keys,macros; }
 // SDTech Options 1.0.3 wire format, independently implemented from static analysis.
 // Only this model's configuration interface is opened. No bootloader/reset commands.
 public sealed class PadHardware:IDisposable {
  SafeFileHandle handle;string serial;Mutex deviceLock;bool ownsLock;
  [StructLayout(LayoutKind.Sequential)]struct Overlap{public IntPtr a,b;public uint offset,offsetHigh;public IntPtr evt;}
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr sa,uint creation,uint flags,IntPtr template);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool ReadFile(SafeFileHandle h,IntPtr data,uint count,IntPtr done,IntPtr ov);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool WriteFile(SafeFileHandle h,IntPtr data,uint count,IntPtr done,IntPtr ov);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetOverlappedResult(SafeFileHandle h,IntPtr ov,out uint count,bool wait);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool CancelIoEx(SafeFileHandle h,IntPtr ov);
  [DllImport("hid.dll",SetLastError=true)]static extern bool HidD_FlushQueue(SafeFileHandle h);
  public PadHardware(){
   deviceLock=new Mutex(false,"Local\\SimpleMacroPad-0816-2473");try{ownsLock=deviceLock.WaitOne(0);}catch(AbandonedMutexException){ownsLock=true;}
   if(!ownsLock){deviceLock.Dispose();deviceLock=null;throw new IOException("다른 키패드 앱 창에서 통신 중입니다.");}
   try{
   var devices=HidDevices.Enumerate(0x0816,0x2473).Where(d=>d.DevicePath.IndexOf("mi_02",StringComparison.OrdinalIgnoreCase)>=0&&unchecked((ushort)d.Capabilities.UsagePage)==0xff00&&d.Capabilities.Usage==2&&d.Capabilities.InputReportByteLength==65&&d.Capabilities.OutputReportByteLength==65).ToArray();
   if(devices.Length!=1)throw new IOException("SIDE-KEYBOARD 설정 인터페이스를 하나만 연결해주세요.");
   byte[] s;serial=devices[0].ReadSerialNumber(out s)?System.Text.Encoding.Unicode.GetString(s).Trim('\0'):"";
   handle=CreateFile(devices[0].DevicePath,0xc0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
   if(handle.IsInvalid)throw new IOException("키보드를 열 수 없습니다. WEB HUB와 다른 설정 앱을 닫아주세요.");
   }catch{Dispose();throw;}
  }
  byte[] Transfer(byte[] bytes,bool write,int timeout){
   using(var ev=new EventWaitHandle(false,EventResetMode.ManualReset)){
    IntPtr ov=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Overlap))),buf=Marshal.AllocHGlobal(65);
    try{
     Marshal.StructureToPtr(new Overlap{evt=ev.SafeWaitHandle.DangerousGetHandle()},ov,false);
     if(write)Marshal.Copy(bytes,0,buf,65);
     bool ok=write?WriteFile(handle,buf,65,IntPtr.Zero,ov):ReadFile(handle,buf,65,IntPtr.Zero,ov);
     if(!ok&&Marshal.GetLastWin32Error()!=997)throw new IOException("USB 입출력 실패: "+Marshal.GetLastWin32Error());
     if(!ok&&!ev.WaitOne(timeout)){CancelIoEx(handle,ov);uint ignored;GetOverlappedResult(handle,ov,out ignored,true);throw new TimeoutException("키보드 응답 시간 초과. WEB HUB를 닫고 USB를 다시 연결해주세요.");}
     uint count;if(!GetOverlappedResult(handle,ov,out count,true)||count!=65)throw new IOException("USB 보고서 길이 또는 통신 오류");
     var result=new byte[65];Marshal.Copy(buf,result,0,65);return result;
    }finally{Marshal.FreeHGlobal(buf);Marshal.FreeHGlobal(ov);}
   }
  }
  public byte[] Exchange(params byte[] payload){
   if(payload.Length>64||payload.Length<2||payload[0]!=6||!new byte[]{5,8,12,13,16}.Contains(payload[1]))throw new ArgumentException("허용되지 않은 명령");
   var report=new byte[65];Array.Copy(payload,0,report,1,payload.Length);HidD_FlushQueue(handle);Transfer(report,true,1500);
   var deadline=DateTime.UtcNow.AddSeconds(3);string last="";
   while(DateTime.UtcNow<deadline){byte[] response;try{response=Transfer(null,false,1500).Skip(1).ToArray();}catch(Exception ex){throw new IOException(ex.Message+" REQUEST="+BitConverter.ToString(payload)+" LAST="+last,ex);}last=BitConverter.ToString(response);
    if(response[0]!=0xaa||response[1]!=(payload[1]==8?7:payload[1]))continue;
    // Address echo disambiguates key/macro block replies from stale replies.
    if(payload.Length>=5&&(response[3]!=payload[3]||response[4]!=payload[4]))continue;
    return response;
   }throw new IOException("요청과 일치하는 키보드 응답이 없습니다.");
  }
  byte[] ReadRegion(byte command,int length,int layer){
   var result=new byte[length];for(int offset=0;offset<length;offset+=56){int count=Math.Min(56,length-offset);
    var reply=command==8?Exchange(6,8,58,(byte)offset,(byte)(offset>>8),0,(byte)layer):Exchange(6,12,(byte)count,(byte)offset,(byte)(offset>>8));
    Array.Copy(reply,8,result,offset,count);
   }return result;
  }
  public PadSnapshot Read(){
   var info=Exchange(6,5);if(info[2]<14)throw new IOException("장치 정보 응답이 너무 짧습니다.");
   // Vendor UI always requests base layer 0; info[18] is not a zero-based layer selector.
   var s=new PadSnapshot{serial=serial,captured=DateTime.UtcNow.ToString("o"),info=info.Select(x=>(int)x).ToArray(),firmware=info[9]|info[10]<<8,profiles=info[15],profile=info[16],layers=info[17],layer=0};
   if(s.profiles<1||s.profile>=s.profiles||s.layers<1||s.layer>=s.layers)throw new IOException("프로필/레이어 정보를 해석할 수 없습니다. INFO="+BitConverter.ToString(info));
   s.keys=ReadRegion(8,88,s.layer).Select(x=>(int)x).ToArray();s.macros=ReadRegion(12,4096,s.layer).Select(x=>(int)x).ToArray();return s;
  }
  public static string Backup(PadSnapshot s,string dir){Directory.CreateDirectory(dir);string path=Path.Combine(dir,"keyboard-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".json");s.backup=path;File.WriteAllText(path,new JavaScriptSerializer().Serialize(s),System.Text.Encoding.UTF8);return path;}
  public PadSnapshot VerifyUnchangedWrite(string backups){
   var before=Read();Backup(before,backups);
   Exchange(6,16,7,0,0,0,0,0,(byte)before.keys[0],(byte)before.keys[1],(byte)before.keys[2],(byte)before.keys[3]);
   foreach(int offset in new[]{0,59,4071}){int count=Math.Min(59,4096-offset);var macro=new byte[5+count];macro[0]=6;macro[1]=13;macro[2]=(byte)count;macro[3]=(byte)offset;macro[4]=(byte)(offset>>8);for(int i=0;i<count;i++)macro[5+i]=(byte)before.macros[offset+i];Exchange(macro);}
   var after=Read();if(!before.keys.SequenceEqual(after.keys)||!before.macros.SequenceEqual(after.macros))throw new IOException("동일 값 재전송 검증 불일치. 백업: "+before.backup);after.backup=before.backup;return after;
  }
  static void Bytes(int[] a,int count){if(a==null||a.Length!=count||a.Any(x=>x<0||x>255))throw new ArgumentException("장치 데이터 형식 오류");}
  public PadSnapshot Apply(PadApply request,string backups){
   if(request==null||request.baseline==null)throw new ArgumentException("먼저 키보드에서 불러오세요.");
   Bytes(request.keys,88);Bytes(request.macros,4096);Bytes(request.baseline.keys,88);Bytes(request.baseline.macros,4096);
   // The four unused slots must survive an upload byte-for-byte.
   for(int i=48;i<64;i++)if(request.keys[i]!=request.baseline.keys[i])throw new ArgumentException("예약된 키 슬롯은 변경할 수 없습니다.");
   var before=Read();var expected=request.baseline;
   if(before.serial!=expected.serial||before.profile!=expected.profile||before.layer!=expected.layer||!before.keys.SequenceEqual(expected.keys)||!before.macros.SequenceEqual(expected.macros))throw new IOException("키보드 설정이 다른 곳에서 변경됐습니다. 다시 불러온 뒤 적용해주세요.");
   string backup=Backup(before,backups);
   try{
    if(!before.macros.SequenceEqual(request.macros))for(int offset=0;offset<4096;offset+=59){int count=Math.Min(59,4096-offset);var p=new byte[5+count];p[0]=6;p[1]=13;p[2]=(byte)count;p[3]=(byte)offset;p[4]=(byte)(offset>>8);for(int i=0;i<count;i++)p[5+i]=(byte)request.macros[offset+i];Exchange(p);}
    for(int index=0;index<22;index++){if(index>=12&&index<16)continue;int off=index*4;if(before.keys.Skip(off).Take(4).SequenceEqual(request.keys.Skip(off).Take(4)))continue;
     Exchange(6,16,7,(byte)off,0,0,(byte)before.layer,0,(byte)request.keys[off],(byte)request.keys[off+1],(byte)request.keys[off+2],(byte)request.keys[off+3]);
    }
    var after=Read();if(after.profile!=before.profile||after.layer!=before.layer||!after.keys.SequenceEqual(request.keys)||!after.macros.SequenceEqual(request.macros))throw new IOException("전송 후 다시 읽은 값이 일치하지 않습니다.");after.backup=backup;return after;
   }catch(Exception ex){throw new IOException("일부 설정이 적용되었을 수 있습니다. 다시 불러와 확인해주세요. 적용 전 백업: "+backup+"\n"+ex.Message,ex);}
  }
  public void Dispose(){if(handle!=null)handle.Dispose();if(deviceLock!=null){if(ownsLock){deviceLock.ReleaseMutex();ownsLock=false;}deviceLock.Dispose();deviceLock=null;}}
 }
}
