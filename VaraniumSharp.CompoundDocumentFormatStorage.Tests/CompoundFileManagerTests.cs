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
        private const string ResourceDirectory = "Resources";

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
            await using (var fileStream = File.Open(filePath, FileMode.Open))
            {
                await sut.AddItemToPackageAsync(packagePath, fileStream, storagePath);
            }

            // act
            await using (var fileStream2 = File.Open(filePath2, FileMode.Open))
            {
                await sut.AddItemToPackageAsync(packagePath, fileStream2, storagePath);
            }

            // assert
            var stream = await sut.RetrieveDataFromPackageAsync(packagePath, storagePath);
            using (var reader = new StreamReader(stream))
            {
                (await reader.ReadLineAsync()).Should().Be("Overwriting data");
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
            await using (var fileStream = File.Open(filePath, FileMode.Open))
            {
                await sut.AddItemToPackageAsync(packagePath, fileStream, storagePath);
            }

            // act
            var result = await sut.RetrieveDataFromPackageAsync(packagePath, storagePath);

            // assert
            await using (var fileData = File.OpenRead(filePath))
            using (var streamReader = new StreamReader(fileData))
            using (var resultReader = new StreamReader(result))
            {
                var expectedData = await streamReader.ReadToEndAsync();
                var resultData = await resultReader.ReadToEndAsync();

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
            await using (var fileStream = File.Open(filePath, FileMode.Open))
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
        public async Task PackageIsClosedCorrectly()
        {
            // arrange
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var packagePath = Path.Combine(appPath, Guid.NewGuid().ToString());
            var filePath = Path.Combine(appPath, ResourceDirectory, "File1.txt");
            const string storagePath = "docs/File1.txt";

            File.Exists(packagePath).Should().BeFalse();

            var sut = new CompoundFileManager();
            await using (var fileStream = File.Open(filePath, FileMode.Open))
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
        public async Task PackageReturnsAllContent()
        {
            // arrange
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var packagePath = Path.Combine(appPath, Guid.NewGuid().ToString());
            var filePath = Path.Combine(appPath, ResourceDirectory, "File1.txt");
            var filePath2 = Path.Combine(appPath, ResourceDirectory, "File2.txt");
            const string storagePath1 = "docs/File1.txt";
            const string storagePath2 = "docs/File2.txt";
            var listToKeep = new List<string> { "docs" };

            File.Exists(packagePath).Should().BeFalse();

            var sut = new CompoundFileManager();
            await using (var fileStream1 = File.Open(filePath, FileMode.Open))
            await using (var fileStream2 = File.Open(filePath2, FileMode.Open))
            {
                await sut.AddItemToPackageAsync(packagePath, fileStream1, storagePath1);
                await sut.AddItemToPackageAsync(packagePath, fileStream2, storagePath2);
            }

            // act
            var feedback = await sut.GetPackageContentAsync(packagePath, listToKeep);

            // assert
            feedback.Count.Should().Be(2);
            feedback[0].Path.Should().Be(storagePath1);
            feedback[0].Size.Should().Be(14);
            feedback[1].Path.Should().Be(storagePath2);
            feedback[1].Size.Should().Be(19);

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
            await using (var fileStream = File.Open(filePath, FileMode.Open))
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
            await using (var fileStream1 = File.Open(filePath, FileMode.Open))
            await using (var fileStream2 = File.Open(filePath2, FileMode.Open))
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
            await using (var fileStream1 = File.Open(filePath, FileMode.Open))
            await using (var fileStream2 = File.Open(filePath2, FileMode.Open))
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
        public async Task ScrubbingPackageWithFeedbackReturnsScrubbedDetails()
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
            await using (var fileStream1 = File.Open(filePath, FileMode.Open))
            await using (var fileStream2 = File.Open(filePath2, FileMode.Open))
            {
                await sut.AddItemToPackageAsync(packagePath, fileStream1, storagePath1);
                await sut.AddItemToPackageAsync(packagePath, fileStream2, storagePath2);
            }

            // act
            var feedback = await sut.ScrubStorageWithFeedbackAsync(packagePath, listToKeep);

            // assert
            feedback.Count.Should().Be(1);
            feedback[0].Path.Should().Be(storagePath2);
            feedback[0].Size.Should().Be(19);
            var act = new Action(() => sut.RetrieveDataFromPackageAsync(packagePath, storagePath2).Wait());
            act.Should().Throw<KeyNotFoundException>();
            
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
            await using (var fileStream = File.Open(filePath, FileMode.Open))
            {
                await sut.AddItemToPackageAsync(packagePath, fileStream, storagePath);
            }

            // act
            var stream = await sut.RetrieveDataFromPackageAsync(packagePath, storagePath);

            // assert
            using (var reader = new StreamReader(stream))
            {
                (await reader.ReadLineAsync()).Should().Be("Test File 1");
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
            await using (var fileStream = File.Open(filePath, FileMode.Open))
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
    }
}