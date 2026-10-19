$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$env:UV_PYTHON_INSTALL_DIR = Join-Path $projectRoot 'Tools/python'
$env:UV_CACHE_DIR = Join-Path $projectRoot 'Library/uv-cache'
$env:DISABLE_TELEMETRY = 'true'
& (Join-Path $projectRoot 'Tools/uv/uvx.exe') --system-certs --python 3.12 --from mcpforunityserver==10.3.0 mcp-for-unity --transport stdio --default-instance TOKAI2
exit $LASTEXITCODE
