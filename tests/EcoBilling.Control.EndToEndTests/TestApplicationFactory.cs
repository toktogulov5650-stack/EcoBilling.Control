using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EcoBilling.Control.EndToEndTests;

internal static class TestApplicationFactory
{
    public static WebApplicationFactory<Program> Create()
    {
        using var rsa = RSA.Create(2048);
        var signingKeyPem = rsa.ExportPkcs8PrivateKeyPem();

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ServiceAuth:Jwt:SigningKeyPem", signingKeyPem);
            builder.UseSetting("ServiceAuth:Jwt:KeyId", "test-service-auth");
        });
    }
}
