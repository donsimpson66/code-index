using CodeIndex.Cli;
using CodeIndex.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton(new CliRuntime());
builder.Services.AddSingleton<CodeIndexBuildService>();
builder.Services.AddSingleton<CodeIndexQueryService>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<CodeIndexMcpTools>();

await builder.Build().RunAsync();