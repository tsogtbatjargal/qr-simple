using ModelContextProtocol.Server;
using QrSimple.Mcp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
    })
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapMcp("/mcp");

var port = int.TryParse(Environment.GetEnvironmentVariable("QR_SIMPLE_MCP_PORT"), out var parsedPort)
    ? parsedPort
    : 8932;

app.Run($"http://127.0.0.1:{port}");
