# TOKAI2 Unity MCP

MCP for Unity 10.3.0 is embedded in `Packages/com.coplaydev.unity-mcp`.
The Codex MCP server named `unity` launches `start-server.ps1` over stdio.
Unity's editor bridge listens locally; no public endpoint is required.

Keep TOKAI2 open in Unity. After adding the MCP server, restart Codex to
load its tools. The editor setup script selects stdio once per Unity session.
The default target is TOKAI2. Telemetry is disabled for the server and editor.

Local runtime binaries in `Tools/uv` and `Tools/python` are ignored by Git.
The Python package cache is under `Library/uv-cache`.

Upstream: https://github.com/CoplayDev/unity-mcp/tree/v10.3.0
