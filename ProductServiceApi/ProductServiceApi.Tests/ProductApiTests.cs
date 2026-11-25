using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ProductServiceApi.Data;
using Xunit;

public sealed class ProductFactory : WebApplicationFactory<Program>
{
    public string Secret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly string database = Guid.NewGuid().ToString();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string>
        {
            ["Jwt:Issuer"] = "test-issuer", ["Jwt:Audience"] = "test-audience",
            ["Jwt:ClientId"] = "test-client", ["Jwt:ClientSecret"] = Secret,
            ["Jwt:SigningKey"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
        }));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ProductContext>>();
            services.AddDbContext<ProductContext>(options => options.UseInMemoryDatabase(database));
        });
}

public class ProductApiTests(ProductFactory factory) : IClassFixture<ProductFactory>
{
    [Theory]
    [InlineData(1, 2, new[] { 1, 2 })]
    [InlineData(2, 2, new[] { 3 })]
    [InlineData(3, 2, new int[0])]
    [InlineData(0, 0, new[] { 1, 2, 3 })]
    public async Task V1_returns_requested_page(int page, int size, int[] expected)
    {
        using var client = factory.CreateClient();
        var products = await client.GetFromJsonAsync<JsonElement>($"/api/v1/products?pageNumber={page}&pageSize={size}");
        Assert.Equal(expected, products.EnumerateArray().Select(p => p.GetProperty("id").GetInt32()));
    }

    [Fact]
    public async Task V2_challenges_anonymous_requests()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v2/products");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
    }

    [Fact]
    public async Task Token_endpoint_rejects_wrong_secret()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/auth/token", new { clientId = "test-client", clientSecret = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Issued_token_opens_v2_and_v2_wraps_the_same_product_array_with_total_count()
    {
        using var client = factory.CreateClient();
        var v1 = await client.GetFromJsonAsync<JsonElement>("/api/v1/products?pageSize=1");
        Assert.Equal(JsonValueKind.Array, v1.ValueKind);
        var product = Assert.Single(v1.EnumerateArray());
        Assert.Equal(new[] { "description", "id", "name", "price", "quantity" },
            product.EnumerateObject().Select(p => p.Name).OrderBy(name => name));

        using var tokenResponse = await client.PostAsJsonAsync("/auth/token", new { clientId = "test-client", clientSecret = factory.Secret });
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("access_token").GetString());
        using var response = await client.GetAsync("/api/v2/products?pageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var v2 = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "products", "totalCount" }, v2.EnumerateObject().Select(p => p.Name).OrderBy(name => name));
        Assert.Equal(3, v2.GetProperty("totalCount").GetInt32());
        Assert.Equal(v1.GetRawText(), v2.GetProperty("products").GetRawText());
    }
}
