$runtime=Join-Path $PSScriptRoot 'runtime'
foreach ($name in @('audio','chatterbox','ollama')) {
    $pidFile=Join-Path $runtime "$name.pid"
    if (!(Test-Path -LiteralPath $pidFile)) { continue }
    try {
        $service=Get-Process -Id ([int](Get-Content -LiteralPath $pidFile)) -ErrorAction SilentlyContinue
        $expected=if ($name -eq 'ollama') { Join-Path $runtime 'ollama\ollama.exe' } else { Join-Path $runtime 'python\python.exe' }
        # A reused PID must never stop an unrelated application's process.
        if ($service -and $service.Path -eq [IO.Path]::GetFullPath($expected)) { Stop-Process -Id $service.Id -ErrorAction Stop; $service.WaitForExit(5000) | Out-Null }
        Remove-Item -LiteralPath $pidFile -ErrorAction SilentlyContinue
    } catch { Write-Warning "Could not stop the $name service. Close it before updating Rune." }
}
