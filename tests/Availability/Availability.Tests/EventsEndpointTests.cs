using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Availability.Tests;

// Comprueba el pipeline real: registrar autenticación para #15 no debe cerrar
// las consultas públicas del #13 ni requerir PostgreSQL para la demo.
public sealed class EventsEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/eventos")]
    [InlineData("/eventos/10000000-0000-0000-0000-000000000001/asientos")]
    public async Task EventPages_WithoutCredentials_Return200(string path)
    {
        using var demoFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["EventQueries:Source"] = "Demo"
                }));
        });
        using var client = demoFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
