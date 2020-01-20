set PATH=C:\Users\media\Desktop\Collector;C:\Program Files (x86)\Microsoft Visual Studio\2019\Preview\Team Tools\DiagnosticsHub\Collector;%PATH%


for /F "tokens=1,2" %%i in ('tasklist /fi "IMAGENAME eq Shaman.Dokan.Archive.exe" /fo table /NH') do set pid=%%j

VSDiagnostics.exe start 11 /attach:%pid% /loadConfig:"C:\Users\media\Desktop\Collector\AgentConfigs\CpuUsageBase.json"
pause
VSDiagnostics.exe stop 11 /output:profile

pause


VSDiagnostics.exe