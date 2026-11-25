using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProductApi.Service;
using ProductServiceApi.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
}).AddMvc();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddDbContext<ProductContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("ProductDb")));

string Required(string name) =>
    builder.Configuration[$"Jwt:{name}"] is string value && !string.IsNullOrWhiteSpace(value)
        ? value : throw new InvalidOperationException($"Configure Jwt:{name} before starting the API.");

var issuer = Required("Issuer");
var audience = Required("Audience");
var clientId = Required("ClientId");
var secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(Required("ClientSecret")));
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Required("SigningKey")));
if (key.KeySize < 256)
    throw new InvalidOperationException("Jwt:SigningKey requires at least 32 UTF-8 bytes.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = issuer,
        ValidateAudience = true, ValidAudience = audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = key,
        ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
        ValidateLifetime = true, ClockSkew = TimeSpan.Zero
    };
});
builder.Services.AddAuthorization(options =>
    options.AddPolicy("productApi", policy => policy.RequireAuthenticatedUser().RequireClaim("scope", "productApi")));

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ProductContext>();
    if (db.Database.IsRelational())
        db.Database.Migrate();
    else
        db.Database.EnsureCreated();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapPost("/auth/token", (ClientCredentials request, HttpResponse response) =>
{
    var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(request.ClientSecret ?? ""));
    if (!CryptographicOperations.FixedTimeEquals(secretHash, suppliedHash) || request.ClientId != clientId)
        return Results.Unauthorized();

    var now = DateTime.UtcNow;
    var token = new JwtSecurityToken(issuer, audience,
        new[] { new Claim("sub", clientId), new Claim("scope", "productApi") },
        now, now.AddHours(1), new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
    response.Headers.CacheControl = "no-store";
    return Results.Ok(new { access_token = new JwtSecurityTokenHandler().WriteToken(token), token_type = "Bearer", expires_in = 3600 });
}).AllowAnonymous();
app.MapControllers();
app.Run();

public record ClientCredentials(string ClientId, string ClientSecret);
public partial class Program { }
