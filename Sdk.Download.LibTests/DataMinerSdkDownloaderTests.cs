using Microsoft.Extensions.Logging;

using Serilog;

namespace Skyline.DataMiner.Sdk.Download.Lib.Tests
{
	[TestClass()]
	public class DataMinerSdkDownloaderTests
	{
		[TestMethod()]
		public async Task AddOrUpdateDataMinerSdkTest()
		{
			var logConfig = new LoggerConfiguration().WriteTo.Console();
			logConfig.MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug);
			var seriLog = logConfig.CreateLogger();

			using var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog(seriLog));
			var logger = loggerFactory.CreateLogger("Skyline.DataMiner.Sdk.Download");

			DataMinerSdkDownloader downloader = new DataMinerSdkDownloader(logger);

			CancellationTokenSource tokenSource = new CancellationTokenSource();

			await downloader.AddOrUpdateDataMinerSdk(tokenSource.Token);
		}
	}
}