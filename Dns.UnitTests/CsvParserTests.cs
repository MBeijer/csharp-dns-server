using System;
using System.IO;
using System.Linq;
using Dns.Utility;
using Xunit;

namespace Dns.UnitTests;

public sealed class CsvParserTests
{
	[Fact]
	public void Create_ThrowsOnInvalidArguments()
	{
		Assert.Throws<ArgumentNullException>(() => CsvParser.Create(null));
		Assert.Throws<FileNotFoundException>(() => CsvParser.Create(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv")));
	}

	[Fact]
	public void Rows_ParsesFieldDeclarationsCommentsAndRows()
	{
		var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
		File.WriteAllLines(path,
		[
			"#Fields: host,addr",
			"example,192.0.2.1",
			"; comment",
			"",
			"api,192.0.2.2",
		]);

		try
		{
			var parser = CsvParser.Create(path);
			var rows = parser.Rows.ToList();

			Assert.Equal(["host", "addr"], parser.Fields);
			Assert.Equal("example", rows[0][0]);
			Assert.Equal("192.0.2.1", rows[0]["addr"]);
			Assert.Equal("api", rows[1]["host"]);
		}
		finally
		{
			File.Delete(path);
		}
	}
}
