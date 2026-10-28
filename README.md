# Simple MacroPad · 키패드

Windows용 12키 + 2노브 키패드 설정 앱입니다. SDINNOVATION SIDE-KEYBOARD (`0816:2473`)의 현재 활성 프로필, 기본 레이어를 편집합니다.

## 사용

1. [Releases](https://github.com/vtubeadmirer/simple-macropad/releases/latest)에서 `SimpleMacroPad-win-x64.zip`을 받아 압축을 풉니다.
2. `Keypad.exe`를 실행합니다. Windows x64와 Microsoft Edge WebView2 Runtime이 필요합니다.
3. 위의 키패드 버튼을 선택하고 아래 키보드를 누르면 바로 지정됩니다. **키보드에 저장**으로 적용합니다.

- 글씨 **작게 / 기본 / 크게**, 밝은 / 어두운 테마
- 연결 시 설정 자동 읽기, 키·노브·미디어·마우스·스크롤 지정
- M0–M15 매크로 기록, 편집, 가져오기/내보내기
- 쓰기 전 백업, 외부 변경 감지, 전송 후 전체 읽기 검증
- **업데이트** 버튼: 최신 GitHub Release 다운로드, SHA-256 확인, 새 버전 실행. 변경한 장치 설정은 먼저 저장하세요.

업데이트 시 앱은 자동으로 재시작됩니다. 다운로드 실패 시 기존 버전을 다시 실행합니다. USB 설정 기능은 오프라인에서 작동하며 업데이트에만 인터넷이 필요합니다.

사용자 배치와 백업은 `%LOCALAPPDATA%\SimpleMacroPad\Desktop`에 보관합니다. 저장소 및 배포 파일에는 포함되지 않습니다.

## 개발 및 버전 관리

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
node test-codec.cjs
dist\Keypad.exe --self-test
```

Windows의 .NET Framework C# 컴파일러를 사용합니다. 빌드 시 NuGet에서 WebView2 1.0.2903.40, HidLibrary 3.3.40을 복원합니다. 각 라이브러리의 라이선스는 해당 NuGet 패키지를 따릅니다.

`ui.html`은 기본 화면, `direct.js`는 직접 편집/글씨 크기, `hardware.js`와 `hardware-codec.js`는 장치 UI 및 직렬화, `Hardware.cs`는 HID 통신, `Keypad.cs`는 Windows 호스트입니다.

`main`에 push하면 GitHub Actions가 빌드합니다. `Keypad.cs`의 버전과 네이티브 테스트 버전 문자열을 함께 바꾼 후 `v0.10.0` 같은 태그를 push하면 ZIP과 체크섬을 Release로 게시합니다.

```sh
git add .
git commit -m "Describe the change"
git push
git tag v0.10.0
git push origin v0.10.0
```

프로필 전환 및 조명 편집은 아직 지원하지 않습니다. 다른 기종에는 적용하지 마세요.
