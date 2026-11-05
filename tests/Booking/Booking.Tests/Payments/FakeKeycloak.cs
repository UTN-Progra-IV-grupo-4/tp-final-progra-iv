using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Booking.Tests.Payments;

// A local stand-in for the Keycloak realm: discovery document, signing keys and client credentials
// token endpoint. Tokens are signed with a throwaway RSA key generated per instance.
internal sealed class FakeKeycloak : IAsyncDisposable
{
    public const string Audience = "payment-service";

    private readonly WebApplication app;
    private readonly RSA rsa;
    private readonly RsaSecurityKey signingKey;
    private int tokenRequestCount;

    public string Authority { get; }

    public int TokenRequestCount => Volatile.Read(ref tokenRequestCount);

    private FakeKeycloak(WebApplication app, RSA rsa, RsaSecurityKey signingKey, string authority)
    {
        this.app = app;
        this.rsa = rsa;
        this.signingKey = signingKey;
        Authority = authority;
    }

    public static async Task<FakeKeycloak> StartAsync()
    {
        var portPicker = new TcpListener(IPAddress.Loopback, 0);
        portPicker.Start();
        var port = ((IPEndPoint)portPicker.LocalEndpoint).Port;
        portPicker.Stop();
        var authority = $"http://127.0.0.1:{port}/realms/seat-reservation";

        var rsa = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        var builder = WebApplication.CreateBuilder();
        // Explicit HTTP/1.1: the test output folder carries Payment's appsettings, which default to HTTP/2 only.
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, port, endpoint => endpoint.Protocols = HttpProtocols.Http1));
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var instance = new FakeKeycloak(app, rsa, signingKey, authority);

        var publicParameters = rsa.ExportParameters(false);
        var keySet = new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = SecurityAlgorithms.RsaSha256,
                    kid = signingKey.KeyId,
                    n = Base64UrlEncoder.Encode(publicParameters.Modulus),
                    e = Base64UrlEncoder.Encode(publicParameters.Exponent)
                }
            }
        };

        app.MapGet("/realms/seat-reservation/.well-known/openid-configuration", () => Results.Json(new
        {
            issuer = authority,
            jwks_uri = $"{authority}/protocol/openid-connect/certs"
        }));
        app.MapGet("/realms/seat-reservation/protocol/openid-connect/certs", () => Results.Json(keySet));
        app.MapPost("/realms/seat-reservation/protocol/openid-connect/token", () =>
        {
            Interlocked.Increment(ref instance.tokenRequestCount);
            return Results.Json(new
            {
                access_token = instance.CreateToken("booking-service"),
                expires_in = 300,
                token_type = "Bearer"
            });
        });

        await app.StartAsync();
        return instance;
    }

    public string CreateToken(string authorizedParty)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Authority,
            Audience = Audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["azp"] = authorizedParty },
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public async ValueTask DisposeAsync()
    {
        await app.DisposeAsync();
        rsa.Dispose();
    }
}
