using Microsoft.Extensions.Configuration.Json;
using Xunit;

namespace Dns.UnitTests;

public class ConfigTests
{
    [Fact]
    public void LoadConfig()
    {
        var jsonSource = new JsonConfigurationSource
        {
            Path = "./Data/appsettings.json",
        };
        var jsonConfig = new JsonConfigurationProvider(jsonSource);
    }
}