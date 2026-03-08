using Dns.Services;
using Xunit;

namespace Dns.UnitTests;

public sealed class BindZoneImportBatchResultTests
{
	[Fact]
	public void Defaults_AreInitialized()
	{
		var result = new BindZoneImportBatchResult();
		var item = new BindZoneImportBatchItem();
		Assert.NotNull(result.Items);
		Assert.Empty(result.Items);
		Assert.Equal(string.Empty, item.ZoneSuffix);
		Assert.Equal(string.Empty, item.FileName);
		Assert.Equal(string.Empty, item.Error);
	}
}
