using DbUp.Engine.Output;
using Microsoft.Extensions.Logging;

namespace DataHub.Settlement.Infrastructure.Database;

/// <summary>
/// Adapts DbUp's IUpgradeLog to Microsoft.Extensions.Logging.ILogger.
/// </summary>
internal sealed class DbUpLogger : IUpgradeLog
{
    private readonly ILogger _logger;

    public DbUpLogger(ILogger logger)
    {
        _logger = logger;
    }

    public void WriteInformation(string format, params object[] args)
        => _logger.LogInformation(format, args);

    public void WriteWarning(string format, params object[] args)
        => _logger.LogWarning(format, args);

    public void WriteError(string format, params object[] args)
        => _logger.LogError(format, args);
}
