param([Parameter(Mandatory=$true)][string]$Runtime, [Parameter(Mandatory=$true)][int]$Port)
$ErrorActionPreference = 'Stop'
$env:OLLAMA_HOST = '127.0.0.1:' + $Port
$env:OLLAMA_MODELS = Join-Path $Runtime 'models'
$env:OLLAMA_MAX_LOADED_MODELS = '1'
$env:OLLAMA_NUM_PARALLEL = '1'
$env:OLLAMA_CONTEXT_LENGTH = '4096'
$service = Start-Process -FilePath (Join-Path $Runtime 'ollama/ollama.exe') -ArgumentList 'serve' -WorkingDirectory $Runtime -WindowStyle Hidden -RedirectStandardOutput (Join-Path $Runtime 'ollama.log') -RedirectStandardError (Join-Path $Runtime 'ollama-errors.log') -PassThru
$service.Id | Set-Content -LiteralPath (Join-Path $Runtime 'ollama.pid')
