"""Read-only MCP handshake and Unity discovery check."""
import asyncio
import json
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

async def main():
    server = StdioServerParameters(
        command=r"C:\Users\S\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe",
        args=["-NoLogo", "-NoProfile", "-File", r"C:\DevGame\TOKAI2\Tools\UnityMcp\start-server.ps1"],
    )
    async with stdio_client(server) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            tools = await session.list_tools()
            print(json.dumps({"tool_count": len(tools.tools), "tools": [t.name for t in tools.tools]}))
            resources = await session.list_resources()
            print(json.dumps({"resources": [str(r.uri) for r in resources.resources]}))
            instances = await session.read_resource("mcpforunity://instances")
            print(instances.model_dump_json())
            project = await session.read_resource("mcpforunity://project/info")
            print(project.model_dump_json())
            scene = await session.call_tool("manage_scene", {"action": "get_active"})
            print(scene.model_dump_json())
            assets = await session.call_tool("manage_asset", {"action": "search", "path": "Assets", "page_size": 1, "page_number": 1})
            print(assets.model_dump_json())

asyncio.run(main())
