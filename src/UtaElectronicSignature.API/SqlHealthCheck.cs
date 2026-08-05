using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using UtaElectronicSignature.Infrastructure;
namespace UtaElectronicSignature.API;
public sealed class SqlHealthCheck(SignatureDbContext db):IHealthCheck
{
 public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,CancellationToken ct=default)
  =>await db.Database.CanConnectAsync(ct)?HealthCheckResult.Healthy():HealthCheckResult.Unhealthy("SQL Server no disponible.");
}
