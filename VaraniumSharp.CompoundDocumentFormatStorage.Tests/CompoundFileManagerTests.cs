using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace VaraniumSharp.CompoundDocumentFormatStorage.Tests
{
    public class CompoundFileManagerTests
    {
        #region Public Methods

        [Fact]
        public async Task PackageIsClosedCorrectly()
        {
            // arrange
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var packagePath = Path.Combine(appPath, Guid.NewGuid().ToString());
            var filePath = Path.Combine(appPath, ResourceDirectory, "File1.txt");
            const string storagePath = "docs/File1.txt";

            File.Exists(packagePath).Should().BeFalse();

            var sut = new CompoundFileManager();
            using (var fileStream = File.Open(filePath, FileMode.Open))
            {
                await sut.AddItemToPackageAsync(packagePath, fileStream, storagePath);
            }

            // act
            sut.ClosePackage(packagePath);

            // assert
            var act = new Action(() => File.Delete(packagePath));
            act.Should().NotThrow<IOException>("If the file was closed we can delete it, otherwise an IO Exception is thrown");
        }

        [Fact]
        public async Task AddingDataWithTheSameInternalStoragePathAsExistingDataOverwritesTheExistingData()
        {
            // arrange
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var packagePath = Path.Combine(appPath, Guid.NewGuid().ToString());
            var filePath = Path.Combine(appPath, ResourceDirectory, "File1.txt");
            var filePath2 = Path.Combine(appPath, ResourceDirectory, "File2.txt");
            const string storagePath = "docs/File1.txt";

            File.Exists(packagePath).Should().BeFalse();

            var sut = new CompoundFileManager();
            using (var fileStream = File.Open(filePath, FileMode.Open))
            {
                await sut.AddItemToPackageAsync(packagePath, fileStream, storagePath);
            }

            // act
            using (var fileStream2 = File.Open(filePath2, FileMode.Open))
            {
                await sut.AddItemToPackageAsync(packagePath, fileStream2, storagePath);
            }

            // assert
            var stream = await sut.RetrieveDataFromPackageAsync(packagePath, storagePath);
            using (var reader = new StreamReader(stream))
            {
                reader.ReadLine().Should().Be("Overwriting data");
            }

            sut.Dispose();
            File.Delete(packagePath);
        }

        [Fact]
        public async Task AutomaticFlushingCorrectlyReattachesTheArchive()
        {
            // arrange
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var packagePath = Path.Combine(appPath, Guid.NewGuid().ToString());
            var filePath = Path.Combine(appPath, ResourceDirectory, "File1.txt");
            const string storagePath = "docs/File1.txt";

            File.Exists(packagePath).Should().BeFalse();

            var sut = new CompoundFileManager
            {
                AutoFlush = true
            };
            using (var fileStream = File.Open(filePath, FileMode.Open))
            {
                await sut.AddItemToPackageAsync(packagePath, fileStream, storagePath);
            }

            // act
            var result = await sut.RetrieveDataFromPackageAsync(packagePath, storagePath);

            // assert
            using (var fileData = File.OpenRead(filePath))
            using (var streamReader = new StreamReader(fileData))
            using (var resultReader = new StreamReader(result))
            {
                var expectedData = streamReader.ReadToEnd();
                var resultData = resultReader.ReadToEnd();

                expectedData.Should().Be(resultData);
            }

            sut.Dispose();
            File.Delete(packagePath);
        }

        [Fact]
        public async Task ItemIsAddedToThePackageCorrectly()
        {
            // arrange
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var packagePath = Path.Combine(appPath, Guid.NewGuid().ToString());
            var filePath = Path.Combine(appPath, ResourceDirectory, "File1.txt");
            const string storagePath = "docs/File1.txt";

            File.Exists(packagePath).Should().BeFalse();

            var sut = new CompoundFileManager();

            // act
            using (var fileStream = File.Open(filePath, FileMode.Open))
            {
                await sut.AddItemToPackageAsync(packagePath, fileStream, storagePath);
            }

            // assert
            var entry = await sut.RetrieveDataFromPackageAsync(packagePath, storagePath);
            using (var streamReader = new StreamReader(entry))
            {
                var data = await streamReader.ReadLineAsync();
                data.Should().Be("Test File 1");
            }
            sut.Dispose();
            File.Delete(packagePath);
        }

        [Fact]
        public async Task RemovingDataFromThePackageCorrectlyRemoveIt()
        {
            // arrange
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var packagePath = Path.Combine(appPath, Guid.NewGuid().ToString());
            var filePath = Path.Combine(appPath, ResourceDirectory, "File1.txt");
            const string storagePath = "docs/File1.txt";

            File.Exists(packagePath).Should().BeFalse();

            var sut = new CompoundFileManager();
            using (var fileStream = File.Open(filePath, FileMode.Open))
            {
                await sut.AddItemToPackageAsync(packagePath, fileStream, storagePath);
            }

            // act
            await sut.RemoveDataFromPackageAsync(packagePath, storagePath);

            // assert
            var act = new Action(() => sut.RetrieveDataFromPackageAsync(packagePath, storagePath).Wait());
            act.Should().Throw<KeyNotFoundException>();

            sut.Dispose();
            File.Delete(packagePath);
        }

        [Fact]
        public async Task ScrubbingPackageCorrectlyRemovesUnwantedDataFromThePackage()
        {
            // arrange
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var packagePath = Path.Combine(appPath, Guid.NewGuid().ToString());
            var filePath = Path.Combine(appPath, ResourceDirectory, "File1.txt");
            var filePath2 = Path.Combine(appPath, ResourceDirectory, "File2.txt");
            const string storagePath1 = "docs/File1.txt";
            const string storagePath2 = "docs/File2.txt";
            var listToKeep = new List<string> { storagePath1 };

            File.Exists(packagePath).Should().BeFalse();

            var sut = new CompoundFileManager();
            using (var fileStream1 = File.Open(filePath, FileMode.Open))
            using (var fileStream2 = File.Open(filePath2, FileMode.Open))
            {
                await sut.AddItemToPackageAsync(packagePath, fileStream1, storagePath1);
                await sut.AddItemToPackageAsync(packagePath, fileStream2, storagePath2);
            }

            // act
            await sut.ScrubStorageAsync(packagePath, listToKeep);

            // assert
            var act = new Action(() => sut.RetrieveDataFromPackageAsync(packagePath, storagePath2).Wait());
            act.Should().Throw<KeyNotFoundException>();


            sut.Dispose();
            File.Delete(packagePath);
        }
        
        [Fact]
        public async Task ScrubbingPackageDoesNotRemoveDataForPathsThatWereRequestedToBeLeft()
        {
            // arrange
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var packagePath = Path.Combine(appPath, Guid.NewGuid().ToString());
            var filePath = Path.Combine(appPath, ResourceDirectory, "File1.txt");
            var filePath2 = Path.Combine(appPath, ResourceDirectory, "File2.txt");
            const string storagePath1 = "docs/File1.txt";
            const string storagePath2 = "docs/File2.txt";
            var listToKeep = new List<string> { storagePath1 };

            File.Exists(packagePath).Should().BeFalse();

            var sut = new CompoundFileManager();
            using (var fileStream1 = File.Open(filePath, FileMode.Open))
            using (var fileStream2 = File.Open(filePath2, FileMode.Open))
            {
                await sut.AddItemToPackageAsync(packagePath, fileStream1, storagePath1);
                await sut.AddItemToPackageAsync(packagePath, fileStream2, storagePath2);
            }

            // act
            await sut.ScrubStorageAsync(packagePath, listToKeep);

            // assert
            var entry = await sut.RetrieveDataFromPackageAsync(packagePath, storagePath1);
            using (var streamReader = new StreamReader(entry))
            {
                var data = await streamReader.ReadLineAsync();
                data.Should().Be("Test File 1");
            }

            sut.Dispose();
            File.Delete(packagePath);
        }

        [Fact]
        public async Task StoredDataIsCorrectlyRetrievedFromThePackage()
        {
            // arrange
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var packagePath = Path.Combine(appPath, Guid.NewGuid().ToString());
            var filePath = Path.Combine(appPath, ResourceDirectory, "File1.txt");
            const string storagePath = "docs/File1.txt";

            File.Exists(packagePath).Should().BeFalse();

            var sut = new CompoundFileManager();
            using (var fileStream = File.Open(filePath, FileMode.Open))
            {
                await sut.AddItemToPackageAsync(packagePath, fileStream, storagePath);
            }

            // act
            var stream = await sut.RetrieveDataFromPackageAsync(packagePath, storagePath);

            // assert
            using (var reader = new StreamReader(stream))
            {
                reader.ReadLine().Should().Be("Test File 1");
            }

            sut.Dispose();
            File.Delete(packagePath);
        }

        [Fact]
        public async Task WhenAnItemIsAddedToAPackageThatDoesNotExistThePackageIsCreated()
        {
            // arrange
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var packagePath = Path.Combine(appPath, Guid.NewGuid().ToString());
            var filePath = Path.Combine(appPath, ResourceDirectory, "File1.txt");
            const string storagePath = "docs/File1.txt";

            File.Exists(packagePath).Should().BeFalse();

            var sut = new CompoundFileManager();

            // act
            using (var fileStream = File.Open(filePath, FileMode.Open))
            {
                await sut.AddItemToPackageAsync(packagePath, fileStream, storagePath);
            }

            // assert
            File.Exists(packagePath).Should().BeTrue();

            sut.Dispose();
            File.Delete(packagePath);
        }

        [Fact]
        public void WhenAPackageDoesNotContainTheDesiredItemAnExceptionIsThrown()
        {
            // arrange
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var packagePath = Path.Combine(appPath, Guid.NewGuid().ToString());
            const string storagePath = "docs/File1.txt";

            var sut = new CompoundFileManager();
            var act = new Action(() => sut.RetrieveDataFromPackageAsync(packagePath, storagePath).Wait());

            // act
            // assert
            act.Should().Throw<KeyNotFoundException>();
        }

        #endregion

        #region Variables

        private const string ResourceDirectory = "Resources";

        #endregion
    }
}