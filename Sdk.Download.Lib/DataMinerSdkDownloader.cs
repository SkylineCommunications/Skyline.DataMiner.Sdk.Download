namespace Skyline.DataMiner.Sdk.Download.Lib
{
	using System;
	using System.Linq;
	using System.Threading;
	using System.Threading.Tasks;

	using Microsoft.Extensions.Logging;

	using NuGet.Common;
	using NuGet.Configuration;
	using NuGet.Packaging.Core;
	using NuGet.Packaging.Signing;
	using NuGet.Protocol;
	using NuGet.Protocol.Core.Types;

	using Skyline.DataMiner.CICD.FileSystem;

	/// <summary>
	/// Provides functionality to download and install the DataMiner SDK package from NuGet.
	/// </summary>
	public class DataMinerSdkDownloader
	{
		private readonly ClientPolicyContext clientPolicyContext;
		private readonly Microsoft.Extensions.Logging.ILogger logger;
		private readonly NuGet.Common.ILogger nuGetLogger;
		private readonly PackageSource nugetPackageSource;
		private readonly SourceRepository nugetRepository;
		private readonly SourceRepository rootRepository;
		private readonly ISettings settings;

		/// <summary>
		/// Initializes a new instance of the <see cref="DataMinerSdkDownloader"/> class.
		/// </summary>
		/// <param name="logger">An instance of <see cref="Microsoft.Extensions.Logging.ILogger"/> used for logging.</param>
		public DataMinerSdkDownloader(Microsoft.Extensions.Logging.ILogger logger)
		{
			this.logger = logger;
			nuGetLogger = NullLogger.Instance;

			// Load default NuGet settings. This will automatically detect NuGet.config files on the default locations.
			settings = Settings.LoadDefaultSettings(null);

			// Create a client policy context based on the loaded settings.
			clientPolicyContext = ClientPolicyContext.GetClientPolicy(settings, nuGetLogger);

			// Retrieve the path to the global NuGet packages folder.
			NuGetRootPath = SettingsUtility.GetGlobalPackagesFolder(settings);

			// Create a repository for the global packages folder.
			rootRepository = new SourceRepository(new PackageSource(NuGetRootPath), Repository.Provider.GetCoreV3());

			// Define the NuGet package source (e.g., NuGet v3 feed).
			nugetPackageSource = new PackageSource("https://api.nuget.org/v3/index.json");

			// Create a repository for the specified NuGet package source.
			nugetRepository = Repository.Factory.GetCoreV3(nugetPackageSource);
		}

		/// <summary>
		/// Gets the path to the NuGet global packages folder.
		/// </summary>
		public string NuGetRootPath { get; private set; }

		/// <summary>
		/// Adds or updates the DataMiner SDK package by downloading the latest stable version from NuGet.
		/// </summary>
		/// <param name="cancel">A <see cref="CancellationToken"/> to observe while waiting for the task to complete.</param>
		/// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
		public async Task AddOrUpdateDataMinerSdk(CancellationToken cancel)
		{
			// Get the package metadata resource.
			var metadataResource = await nugetRepository.GetResourceAsync<PackageMetadataResource>(cancel);

			// Retrieve all package metadata for Skyline.DataMiner.Sdk, filtering out unlisted and prerelease versions.
			var metadata = await metadataResource.GetMetadataAsync(
				"Skyline.DataMiner.Sdk",
				includePrerelease: false,
				includeUnlisted: false,
				new SourceCacheContext(),
				NullLogger.Instance,
				cancel);

			// Select the highest (latest) stable version.
			var latestVersion = metadata
				.Select(m => m.Identity.Version)
				.Max();

			// Create a PackageIdentity for the latest stable version.
			var sdkId = new PackageIdentity("Skyline.DataMiner.Sdk", latestVersion);

			using (var cacheContext = new SourceCacheContext())
			{
				cacheContext.MaxAge = DateTimeOffset.UtcNow;
				await InstallPackageIfNotFound(sdkId, cacheContext, cancel);
			}
		}

		/// <summary>
		/// Installs the specified package if it is not already installed in the global packages folder.
		/// </summary>
		/// <param name="packageToInstall">The package identity to install.</param>
		/// <param name="cacheContext">The <see cref="SourceCacheContext"/> to use during installation.</param>
		/// <param name="cancelToken">A <see cref="CancellationToken"/> to observe while waiting for the task to complete.</param>
		/// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
		private async Task InstallPackageIfNotFound(PackageIdentity packageToInstall, SourceCacheContext cacheContext, CancellationToken cancelToken)
		{
			var existsResource = await rootRepository.GetResourceAsync<FindLocalPackagesResource>(cancelToken);
			if (existsResource.Exists(packageToInstall, nuGetLogger, cancelToken))
			{
				// Package is already installed.
				logger.LogInformation($"OK - {packageToInstall} was already present in local cache");
				return;
			}

			var resource = await nugetRepository.GetResourceAsync<DownloadResource>(cancelToken);

			try
			{
				using (DownloadResourceResult downloadResourceResult = await resource.GetDownloadResourceResultAsync(
					packageToInstall,
					new PackageDownloadContext(cacheContext),
					SettingsUtility.GetGlobalPackagesFolder(settings),
					nuGetLogger,
					cancelToken))
				{
					// Add the package to the global packages folder.
					using (DownloadResourceResult result = await GlobalPackagesFolderUtility.AddPackageAsync(
						nugetPackageSource.Source,
						packageToInstall,
						downloadResourceResult.PackageStream,
						NuGetRootPath,
						Guid.Empty,
						clientPolicyContext,
						nuGetLogger,
						CancellationToken.None))
					{
						logger.LogDebug($"InstallPackageIfNotFound|Finished installing package {packageToInstall.Id} - {packageToInstall.Version} with status: " + result?.Status);
					}
				}
			}
			catch
			{
				logger.LogDebug("Retrying to add package without caching");
				string tempDir = FileSystem.Instance.Directory.CreateTemporaryDirectory();

				try
				{
					// Retry without caching.
					using (DownloadResourceResult downloadResourceResult = await resource.GetDownloadResourceResultAsync(
						packageToInstall,
						new PackageDownloadContext(cacheContext, tempDir, true),
						SettingsUtility.GetGlobalPackagesFolder(settings),
						nuGetLogger,
						cancelToken))
					{
						// Add the package to the global packages folder.
						using (DownloadResourceResult result = await GlobalPackagesFolderUtility.AddPackageAsync(
							nugetPackageSource.Source,
							packageToInstall,
							downloadResourceResult.PackageStream,
							NuGetRootPath,
							Guid.Empty,
							clientPolicyContext,
							nuGetLogger,
							CancellationToken.None))
						{
							logger.LogDebug($"InstallPackageIfNotFound|Finished installing package {packageToInstall.Id} - {packageToInstall.Version} with status: " + result?.Status);
						}
					}
				}
				finally
				{
					FileSystem.Instance.Directory.DeleteDirectory(tempDir);
				}
			}

			logger.LogInformation($"OK - {packageToInstall} was installed to local cache.");
		}
	}
}