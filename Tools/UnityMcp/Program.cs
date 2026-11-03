using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UnityMcp;

// stdio MCP server (Claude Code) <-> TCP bridge inside the Unity editor. See README.md.
// stdout carries MCP JSON-RPC only; every log line goes to stderr.
string host = "127.0.0.1";
int port = int.TryParse(Environment.GetEnvironmentVariable("UNITY_MCP_PORT"), out int envPort) ? envPort : 7801;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--port") port = int.Parse(args[i + 1]);
    else if (args[i] == "--host") host = args[i + 1];
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddSingleton(_ =>
{
    var bridge = new BridgeClient(host, port);
    bridge.Start();
    return bridge;
});
builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "unity-editor", Version = "1.0.0" })
    .WithStdioServerTransport()
    .WithTools<UnityTools>();

var app = builder.Build();
// Connect eagerly so the first tool call usually finds the editor already attached.
app.Services.GetRequiredService<BridgeClient>();
await app.RunAsync();
