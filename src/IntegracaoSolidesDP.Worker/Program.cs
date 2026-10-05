using IntegracaoSolidesDP.Worker.Commands;
using IntegracaoSolidesDP.Worker.Infrastructure;
using IntegracaoSolidesDP.Worker.Pipeline;
using IntegracaoSolidesDP.Worker.Scheduling;
using Microsoft.Extensions.Options;
using Serilog;

var command = CliCommand.Parse(args);

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = command.HostArgs,
    // Como Windows Service o diretório corrente é System32: appsettings e logs ficam junto do executável.
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddWindowsService(options => options.ServiceName = "IntegracaoSolidesDP");
builder.Services.AddSystemd();
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext());

builder.Services.AddIntegracaoSolidesDP(builder.Configuration);
if (command.Mode == CliMode.Service)
{
    builder.Services.AddHostedService<SyncWorker>();
}

IHost host;
try
{
    host = builder.Build();
    // Valida as options agora (ValidateOnStart só roda no StartAsync, que os comandos não chamam).
    _ = host.Services.GetRequiredService<IOptions<IntegracaoSolidesDP.Worker.Options.SolidesDpOptions>>().Value;
    _ = host.Services.GetRequiredService<IOptions<IntegracaoSolidesDP.Worker.Options.ExecutionOptions>>().Value;
    _ = host.Services.GetRequiredService<IOptions<IntegracaoSolidesDP.Worker.Options.SyncOptions>>().Value;
}
catch (OptionsValidationException ex)
{
    await Console.Error.WriteLineAsync("Configuração inválida:");
    foreach (var failure in ex.Failures)
    {
        await Console.Error.WriteLineAsync($"  - {failure}");
    }

    return 2;
}

if (command.Mode == CliMode.Service)
{
    await host.RunAsync();
    return 0;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

await using var scope = host.Services.CreateAsyncScope();
var services = scope.ServiceProvider;
var ct = cancellation.Token;

switch (command.Mode)
{
    case CliMode.CheckConfig:
        return await services.GetRequiredService<OperatorCommands>().CheckConfigAsync(ct);
    case CliMode.Discover:
        return await services.GetRequiredService<OperatorCommands>().DiscoverAsync(ct);
    case CliMode.Reconcile:
        return await services.GetRequiredService<OperatorCommands>().ReconcileAsync(command.Repair, ct);
    default:
        var summary = await services.GetRequiredService<SyncPipeline>().RunAsync(
            command.Mode == CliMode.DryRun ? "cli-dry-run" : "cli", command.Mode == CliMode.DryRun ? true : null, ct);
        Console.WriteLine($"{summary.Status} — relatório: {summary.ReportPath}");
        return summary.Status is "completed" ? 0 : 1;
}
