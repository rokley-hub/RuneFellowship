param([switch]$SmokeTest)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Service Ports.ps1')
$instanceMutex=$null;$ownsInstance=$false
try {
    $root = $PSScriptRoot
    $instanceMutex=New-Object Threading.Mutex $false,('Local\RuneFellowship-'+(Get-RuneInstanceKey $root))
    try {$ownsInstance=$instanceMutex.WaitOne(0)} catch [Threading.AbandonedMutexException] {$ownsInstance=$true}
    if(!$ownsInstance){throw 'Rune is already starting or open from this installation.'}
    $existing=Get-Process RuneVoice -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq (Join-Path $root 'app\RuneVoice.exe')}
    if($existing){$ownsInstance=$false;$instanceMutex.ReleaseMutex();throw 'Rune is already open from this installation.'}
    $runtime = Join-Path $root 'runtime'
    $python = Join-Path $runtime 'python\python.exe'
    $env:DOTNET_ROOT = Join-Path $runtime 'dotnet'
    $env:OLLAMA_MODELS = Join-Path $runtime 'models'
    # Release abandoned services recorded by this installation only. Never adopt
    # or stop an unknown listener belonging to another installation/application.
    & (Join-Path $PSScriptRoot 'Stop Rune services.ps1')
    $preferred=if($SmokeTest){12439}else{11439}
    $basePort=Select-RunePortBase $root $preferred
    @{basePort=$basePort;audioPort=$basePort+2;expressivePort=$basePort+3} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runtime 'service-ports.json')
    $env:RUNE_SERVICE_BASE_PORT=[string]$basePort
    $env:OLLAMA_HOST = '127.0.0.1:'+$basePort
    $brainUrl='http://127.0.0.1:'+$basePort
    $audioUrl='http://127.0.0.1:'+($basePort+2)
    $expressiveUrl='http://127.0.0.1:'+($basePort+3)
    $env:OLLAMA_MAX_LOADED_MODELS = '1'; $env:OLLAMA_NUM_PARALLEL = '1'; $env:OLLAMA_CONTEXT_LENGTH = '4096'
    $bridge = Join-Path $root 'bridge'
    New-Item -ItemType Directory -Force $bridge | Out-Null
    $mode = 'local'
    $prefs = Join-Path $bridge 'preferences.json'
    if (Test-Path -LiteralPath $prefs) { $mode = (Get-Content -LiteralPath $prefs -Raw | ConvertFrom-Json).aiMode }
    if (!(Test-Path -LiteralPath $python)) { throw 'The Rune runtime is missing. Run Verify Rune or reinstall the core package.' }
    function Start-LocalService($url, $exe, $arguments, $name) {
        $ready=$false
        try { Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 1 | Out-Null; $ready=$true } catch { }
        if ($ready) {
            $pidFile=Join-Path $runtime "$name.pid"
            $owned=$false
            if (Test-Path -LiteralPath $pidFile) {
                $service=Get-Process -Id ([int](Get-Content -LiteralPath $pidFile)) -ErrorAction SilentlyContinue
                $owned=$service -and $service.Path -eq [IO.Path]::GetFullPath($exe)
            }
            if (!$owned) { throw 'Another Rune installation or application is using a local service port. Close that installation and its services before opening this beta.' }
            return
        }
        $p = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $runtime -WindowStyle Hidden -RedirectStandardOutput (Join-Path $runtime "$name.log") -RedirectStandardError (Join-Path $runtime "$name-errors.log") -PassThru
        $p.Id | Set-Content (Join-Path $runtime "$name.pid")
    }
    if ($mode -ne 'chatgpt' -and (Test-Path -LiteralPath (Join-Path $runtime 'ollama\ollama.exe'))) {
        Start-LocalService ($brainUrl+'/api/tags') (Join-Path $runtime 'ollama\ollama.exe') @('serve') 'ollama'
    }
    $speechReady=Test-Path -LiteralPath (Join-Path $runtime 'whisper-small.en/model.bin')
    if ($speechReady) {
        Start-LocalService ($audioUrl+'/health') $python @('-I', ('"' + (Join-Path $runtime 'audio-service.py') + '"')) 'audio'
    } elseif (!(Test-Path -LiteralPath $prefs)) {
        @{microphoneMode=0;voiceReplies=$false} | ConvertTo-Json | Set-Content -LiteralPath $prefs
    }
    if (Test-Path -LiteralPath (Join-Path $runtime 'chatterbox-packages\chatterbox')) {
        Start-LocalService ($expressiveUrl+'/health') $python @('-I', ('"' + (Join-Path $runtime 'chatterbox-service.py') + '"')) 'chatterbox'
    }
    # Open the UI even if an optional model is missing; Settings explains the route.
    $appArgs=@('--bridge', ('"' + $bridge + '"'))
    if ($SmokeTest) {
        $checks=Join-Path $root 'startup-check';New-Item -ItemType Directory -Force $checks | Out-Null
        $audioReady=$false
        for ($attempt=0;$speechReady -and $attempt -lt 40;$attempt++) {
            try { Invoke-RestMethod ($audioUrl+'/health') -TimeoutSec 2 | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $checks 'audio-health.json');$audioReady=$true;break } catch { Start-Sleep -Milliseconds 500 }
        }
        if ($speechReady -and !$audioReady) { throw 'Audio did not become ready during startup test.' }
        if (!$speechReady) { @{ready=$false;optionalSpeechModelsMissing=$true} | ConvertTo-Json | Set-Content (Join-Path $checks 'audio-health.json') }
        $appArgs=@('--wpf-preview', ('"'+(Join-Path $checks 'welcome.png')+'"'),'Welcome','1280','800')
        if ($mode -ne 'chatgpt' -and (Test-Path -LiteralPath (Join-Path $runtime 'ollama\ollama.exe'))) {
            $body=@{model='qwen3.5:4b';prompt='Reply exactly: Rune ready.';think=$false;stream=$false;keep_alive=0;options=@{num_predict=64}} | ConvertTo-Json -Depth 3
            $reply=Invoke-RestMethod ($brainUrl+'/api/generate') -Method Post -Body $body -ContentType 'application/json' -TimeoutSec 180
            $reply | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $checks 'brain-response.json')
            if ([string]::IsNullOrWhiteSpace($reply.response)) { throw 'The local brain returned no reply text.' }
        }
    }
    $app=Start-Process -FilePath (Join-Path $root 'app\RuneVoice.exe') -ArgumentList $appArgs -PassThru
    $app.WaitForExit()
    if ($SmokeTest -and !(Test-Path -LiteralPath (Join-Path $checks 'welcome.png'))) { throw 'The desktop did not render during startup test.' }
} catch {
    if ($SmokeTest) { throw }
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show($_.Exception.Message, 'Rune could not start') | Out-Null
    exit 1
} finally {
    if($ownsInstance){& (Join-Path $PSScriptRoot 'Stop Rune services.ps1');$instanceMutex.ReleaseMutex()}
    if($instanceMutex){$instanceMutex.Dispose()}
}
