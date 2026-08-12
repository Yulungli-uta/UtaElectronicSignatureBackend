using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using UtaElectronicSignature.API;
using UtaElectronicSignature.Application;
using UtaElectronicSignature.FirmaEc;
using UtaElectronicSignature.Infrastructure;

var builder=WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
builder.Host.UseSerilog((c,l)=>l.ReadFrom.Configuration(c.Configuration).Enrich.FromLogContext().WriteTo.Console());
if(args.Contains("--provision-institutional-dependencies",StringComparer.Ordinal))
{
 Environment.ExitCode=await InstitutionalProvisioner.RunAsync(builder.Configuration);
 return;
}
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();builder.Services.AddHttpContextAccessor();builder.Services.AddSignalR();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddDbContext<SignatureDbContext>(o=>o.UseSqlServer(builder.Configuration.GetConnectionString("SignatureDatabase")));
builder.Services.AddScoped<ICurrentUserService,CurrentUserService>();
builder.Services.AddScoped<ISigningProcessService,SigningProcessService>();
builder.Services.AddScoped<ICallbackEndpointService,CallbackEndpointService>();
builder.Services.AddScoped<IPdfSignatureValidator,PdfSignatureValidationService>();
builder.Services.AddScoped<ICertificateValidator,CertificateValidationService>();
builder.Services.Configure<FirmaEcOptions>(builder.Configuration.GetSection(FirmaEcOptions.SectionName));
builder.Services.AddHttpClient<IFirmaEcClient,FirmaEcClient>((services,client)=>{
 var options=services.GetRequiredService<Microsoft.Extensions.Options.IOptions<FirmaEcOptions>>().Value;
 client.Timeout=TimeSpan.FromSeconds(options.TimeoutSeconds);
});
// Unico lugar que arma un BaseAddress de HttpClient a partir de una URL de config.
// BaseAddress DEBE terminar en "/" y las rutas relativas usadas con ese cliente NO
// deben empezar con "/": si el host expone la API bajo un subpath (ej. /WsSeguUta),
// una ruta relativa con "/" inicial descarta ese subpath en vez de anexarse (regla
// de combinacion de URIs de HttpClient). Todo cliente institucional nuevo debe
// construir su BaseAddress con este helper, nunca con "new Uri(valorCrudo)".
static Uri NormalizeBaseAddress(string value)=>new(value.TrimEnd('/')+"/");

var authBase=builder.Configuration["RepositoryUta:BaseUrl"]??throw new InvalidOperationException("RepositoryUta:BaseUrl es obligatorio.");
var hrBackendBase=builder.Configuration["HrBackend:BaseUrl"]??throw new InvalidOperationException("HrBackend:BaseUrl es obligatorio.");
builder.Services.AddHttpClient("RepositoryUta",c=>c.BaseAddress=NormalizeBaseAddress(authBase)).AddStandardResilienceHandler();
builder.Services.AddHttpClient("HrBackend",c=>c.BaseAddress=NormalizeBaseAddress(hrBackendBase)).AddStandardResilienceHandler();
builder.Services.AddHttpClient("Callbacks",c=>c.Timeout=TimeSpan.FromSeconds(builder.Configuration.GetValue("Callbacks:TimeoutSeconds",15))).AddStandardResilienceHandler();
builder.Services.AddSingleton<ServiceTokenProvider>();builder.Services.AddScoped<HrBackendClient>();builder.Services.AddScoped<DocumentAccessService>();builder.Services.AddHostedService<OutboxWorker>();builder.Services.AddHostedService<SignatureMaintenanceWorker>();builder.Services.AddHostedService<CallbackOutboxWorker>();
var jwksPath=builder.Configuration["RepositoryUta:JwksUrl"]??"/.well-known/jwks.json";
var jwksUrl=$"{authBase.TrimEnd('/')}/{jwksPath.TrimStart('/')}";
// ConfigurationManager refresca solo (AutomaticRefreshInterval) en vez del fetch
// unico de antes al arrancar: si RepositoryUta rota su llave (reinicio en dev con
// clave efimera, o rotacion real en produccion), esto se recupera solo sin
// necesitar recrear el contenedor.
var jwksManager=new Microsoft.IdentityModel.Protocols.ConfigurationManager<JsonWebKeySet>(
    jwksUrl,new JsonWebKeySetRetriever(),new HttpDocumentRetriever{RequireHttps=new Uri(jwksUrl).Scheme==Uri.UriSchemeHttps})
{
    AutomaticRefreshInterval=TimeSpan.FromMinutes(5),
    RefreshInterval=TimeSpan.FromSeconds(30)
};
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>{
 o.TokenValidationParameters=new(){ValidateIssuerSigningKey=true,ValidateIssuer=true,ValidIssuer=builder.Configuration["RepositoryUta:Issuer"],
 ValidateAudience=true,ValidAudience=builder.Configuration["RepositoryUta:Audience"],ValidateLifetime=true,ClockSkew=TimeSpan.FromMinutes(2),ValidAlgorithms=[SecurityAlgorithms.RsaSha256],
 IssuerSigningKeyResolver=(token,securityToken,kid,parameters)=>jwksManager.GetConfigurationAsync(CancellationToken.None).GetAwaiter().GetResult().GetSigningKeys()};
 o.Events=new(){OnMessageReceived=c=>{if(c.HttpContext.Request.Path.StartsWithSegments("/signatureHub"))c.Token=c.Request.Query["access_token"];return Task.CompletedTask;},
  OnAuthenticationFailed=c=>{
   jwksManager.RequestRefresh();
   c.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JwtBearer")
    .LogWarning(c.Exception,"Fallo de autenticacion JWT en {Path}: {Message}",c.HttpContext.Request.Path,c.Exception.Message);
   return Task.CompletedTask;
  }};
});
builder.Services.AddAuthorization(o=>{foreach(var p in SignaturePermissions.All)o.AddPolicy(p,x=>x.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(p)));});
builder.Services.AddRateLimiter(o=>{
 o.AddFixedWindowLimiter("external-signing",x=>{x.PermitLimit=20;x.Window=TimeSpan.FromMinutes(1);x.QueueLimit=0;});
 o.RejectionStatusCode=429;
});
builder.Services.AddSingleton<IAuthorizationHandler,PermissionHandler>();
builder.Services.AddCors(o=>o.AddPolicy("Frontend",p=>p.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()??[]).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddHealthChecks().AddCheck<SqlHealthCheck>("sql-server",tags:["ready"]);

var app=builder.Build();
var forwardedHeaders=new ForwardedHeadersOptions
{
 ForwardedHeaders=ForwardedHeaders.XForwardedFor|ForwardedHeaders.XForwardedProto
};
forwardedHeaders.KnownNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);
app.Use(async(ctx,next)=>{ctx.Response.Headers["X-Correlation-ID"]=ctx.TraceIdentifier;await next();});
app.UseExceptionHandler();if(app.Environment.IsDevelopment()){app.UseSwagger();app.UseSwaggerUI();}
app.UseHttpsRedirection();app.UseCors("Frontend");app.UseRateLimiter();app.UseAuthentication();app.UseAuthorization();
app.MapControllers();app.MapHub<SignatureHub>("/signatureHub").RequireAuthorization();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live",new(){Predicate=_=>false});
app.MapHealthChecks("/health/ready",new(){Predicate=x=>x.Tags.Contains("ready")});
app.Run();
public partial class Program;
