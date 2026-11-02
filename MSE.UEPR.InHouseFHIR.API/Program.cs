using System;

using Asp.Versioning;

using AutoMapper;

using Hl7.Fhir.Rest;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.AzureAppServices;
using Microsoft.Identity.Web;
using Microsoft.OpenApi.Models;

using Moq;

using MSE.UEPR.FHIR.CORE.Config;
using MSE.UEPR.FHIR.CORE.Mappings;
using MSE.UEPR.FHIR.CORE.Redis;
using MSE.UEPR.FHIR.CORE.Security;
using MSE.UEPR.FHIR.Services;
using MSE.UEPR.FHIR.Services.Interfaces;
using MSE.UEPR.FHIR.Services.Services;

using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var bypassAuth = builder.Configuration.GetSection("AzureAd:Enabled").Value != null
    && bool.TryParse(builder.Configuration.GetSection("AzureAd:Enabled").Value, out bool res) && !res;
bool isRedisEnabled = false;

builder.Logging.AddAzureWebAppDiagnostics();
// Configure specific file logging options
builder.Services.Configure<AzureFileLoggerOptions>(options =>
{
    options.FileName = "azure-diagnostics-inhouse-FHIR";
    options.FileSizeLimit = 50 * 1024;
    options.RetainedFileCountLimit = 15;
});

// Add services to the container.

builder.Services.AddControllers();
//Access configuration
var configuration = builder.Configuration;

// Register services that need IConfiguration
builder.Services.AddSingleton<IConfiguration>(configuration);

if (configuration.GetSection("Redis:Enabled").Value != null
    && bool.TryParse(configuration.GetSection("Redis:Enabled").Value, out bool redis) && redis)
{
    isRedisEnabled = true;
}

builder.Services.AddSingleton<LocalRedisCache>();
builder.Services.AddSingleton<ManagedRedis>();

//IF Redis is enabled in configuration, register the Redis services.
//This allows us to use Redis for caching and other purposes if it's enabled, while still allowing the application to run without Redis if it's not needed or not available
//in certain environments (like local development).
if (isRedisEnabled)
{
    if (builder.Environment.IsDevelopment())
    {
        //fRedis Caching -via Docker: or the purpose of testing, we will register a mock Redis connection if Redis is disabled in configuration. This allows us to test the application without needing a real Redis instance.
        builder.Services.AddSingleton<IConnectionMultiplexer>(x => ConnectionMultiplexer.Connect(configuration.GetSection("Redis:LocalRedisUrl").Value!));
        builder.Services.AddSingleton<IRedisCache, LocalRedisCache>();
    }
    else
    { //Azure Managed Redis: for production, we will register the actual Redis service that connects to Azure Redis Cache using the provided endpoint and credentials. The Redis class will handle the connection logic and provide a database instance for caching.
        builder.Services.AddSingleton<IRedisCache>(provider => provider.GetRequiredService<ManagedRedis>());
    }
}
// Mock the DB caching
else
{
    builder.Services.AddSingleton<IRedisCache, LocalRedisCache>();
    var mockMultiplexer = new Mock<IConnectionMultiplexer>();
    var mockDatabase = new Mock<IDatabase>();

    // 2. Configure the multiplexer to return the dummy database
    mockMultiplexer
        .Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
        .Returns(mockDatabase.Object);

    // 3. Register as a singleton in your C# builder
    builder.Services.AddSingleton<IConnectionMultiplexer>(mockMultiplexer.Object);
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient<PatientService>();
builder.Services.AddScoped<IPatientService, PatientService>();
builder.Services.AddScoped<IAllergyIntoleranceService, AllergyIntoleranceService>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
builder.Services.AddSingleton<AppSettings>();
builder.Services.AddHttpClient<IHttpClientToCerner, HttpClientToCerner>();

// Register a standard pool for handling underlying sockets efficiently
// 1. Socket pooling for outgoing connections (Singleton under the hood)
builder.Services.AddHttpClient("FhirClientPool", client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
});

builder.Services.AddHttpClient("IdentityClientPool", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

// Register your primary dependency layers
// 2. Authentication Helper (Singleton so the cache token is shared across all concurrent users)
builder.Services.AddSingleton<Authentication>();

// Register the FhirClient factory explicitly by passing only the 'provider'
// Scoped: Once that web request finishes and returns a response to the user, that specific client instance is safely disposed.
// This is perfectly safe for token management because web requests usually execute in under a few seconds—well before your 1-hour token expires.
// 3. The FHIR SDK Client factory mapping (Scoped per API request)
builder.Services.AddScoped<FhirClient>(provider =>
{
    var authService = provider.GetRequiredService<Authentication>();

    // Using .GetAwaiter().GetResult() is completely safe here because GetAccessToken()
    // uses a fast, local memory cache check 99.9% of the time.
    return authService.GetFhirClientAsync().GetAwaiter().GetResult();
});

//Refactor this to use a custom authorization policy that checks if AzureAd:Enabled is true, and if not, allows anonymous access.
//This will allow us to keep the authentication and authorization configuration cleaner and more maintainable.
if (bypassAuth)
{
    builder.Services.AddMvc(options => { options.Filters.Add(new AllowAnonymousFilter()); options.EnableEndpointRouting = false; });
}
else
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));
    builder.Services.AddMvc(options => { options.EnableEndpointRouting = false; });
}

var mappingConfig = new MapperConfiguration(mc =>
{
    mc.AddProfile(new PatientMapper());
    mc.AddProfile(new PatientInHouseMapper());
    mc.AddProfile(new AddressMapper());
    mc.AddProfile(new AllergyInhouseMapper());
    mc.AddProfile(new OrganizationMapper());
    // mc.DisableConstructorMapping();
});
//mappingConfig.AssertConfigurationIsValid();

IMapper mapper = mappingConfig.CreateMapper();
builder.Services.AddSingleton(mapper);
builder.Services.AddSwaggerGen(
c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "MSE.UEPR.InHouseFHIR.API", Version = "v1" });
    c.EnableAnnotations();
});
builder.Services.AddApiVersioning(
options =>
{
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.ReportApiVersions = true;
});


var app = builder.Build();

app.UseDeveloperExceptionPage();
// 1. CRITICAL: This allows the app to serve your custom-swagger.css file
app.UseStaticFiles();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MSE.UEPR.InHouseFHIR.API");
    c.ConfigObject.AdditionalItems.Add("useUnsafeMarkdown", true);
    c.InjectStylesheet("/custom-swagger.css");
});
}

if (app.Environment.IsProduction() && isRedisEnabled)
{
    // Initialize Redis connection
    var redisservice = app.Services.GetRequiredService<ManagedRedis>();

    try
    {
        await redisservice.ConnectAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to connect to REDIS: {ex.Message}");
        throw;
    }
}

var rewriteOptions = new RewriteOptions()
     .AddRewrite(@"(.+)//+(.*)", "$1/$2", skipRemainingRules: false);
app.UseRewriter(rewriteOptions);

app.UseHttpsRedirection();
if (bypassAuth)
{
    app.UseMvc();
}
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();