using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VaraniumSharp.Attributes;
using VaraniumSharp.Enumerations;
using VaraniumSharp.Interfaces.Collections;

namespace VaraniumSharp.CompoundDocumentFormatStorage
{
    /// <inheritdoc />
    [AutomaticContainerRegistration(typeof(IPackageManager), ServiceReuse.Singleton, Priority = 1)]
    public class CompoundFileManager : IPackageManager
    {
        #region Constructor

        /// <summary>
        /// DI Constructor
        /// </summary>
        public CompoundFileManager()
        {
            _containerLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
            _openCompoundFiles = new ConcurrentDictionary<string, CompoundFileContainer>();
        }

        #endregion

        #region Properties

        /// <inheritdoc />
        public bool AutoFlush { get; set; }

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public async Task AddItemToPackageAsync(string packagePath, Stream data, string storagePath)
        {
            var semaphore = _containerLocks.GetOrAdd(packagePath, new SemaphoreSlim(1));

            try
            {
                await semaphore.WaitAsync();
                var cf = GetCompoundFile(packagePath);
                var (directory, filename) = SplitFilepath(storagePath);
                var storage = cf.GetStorageForPath(directory);
                storage.TryGetStream(filename, out var currentStream);
                if (currentStream != null)
                {
                    currentStream.CopyFrom(data);
                }
                else
                {
                    var fileStream = storage.AddStream(filename);
                    fileStream.CopyFrom(data);
                }

                cf.Commit();
            }
            finally
            {
                semaphore.Release(1);
            }
        }

        /// <inheritdoc />
        public void ClosePackage(string packagePath)
        {
            var semaphore = _containerLocks.GetOrAdd(packagePath, new SemaphoreSlim(1));

            try
            {
                semaphore.Wait();
                if (_openCompoundFiles.TryRemove(packagePath, out var fileToClose))
                {
                    fileToClose.Close();
                }
            }
            finally
            {
                semaphore.Release(1);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (var cf in _openCompoundFiles)
            {
                cf.Value.Close();
            }
        }

        /// <inheritdoc />
        public async Task RemoveDataFromPackageAsync(string packagePath, string storagePath)
        {
            var semaphore = _containerLocks.GetOrAdd(packagePath, new SemaphoreSlim(1));

            try
            {
                await semaphore.WaitAsync();
                var cf = GetCompoundFile(packagePath);
                var (directory, filename) = SplitFilepath(storagePath);
                var storage = cf.GetStorageForPath(directory);
                storage.Delete(filename);
                cf.Commit();
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <inheritdoc />
        public async Task<Stream> RetrieveDataFromPackageAsync(string packagePath, string storagePath)
        {
            var semaphore = _containerLocks.GetOrAdd(packagePath, new SemaphoreSlim(1));
            try
            {
                await semaphore.WaitAsync();
                var (directory, filename) = SplitFilepath(storagePath);
                var cf = GetCompoundFile(packagePath);
                var storage = cf.GetStorageForPath(directory);
                storage.TryGetStream(filename, out var stream);
                if (stream == null)
                {
                    await Task.Delay(1);
                    throw new KeyNotFoundException();
                }

                var data = stream.GetData();
                return new MemoryStream(data);
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <inheritdoc />
        public async Task ScrubStorageAsync(string packagePath, List<string> storagePathsToKeep)
        {
            var semaphore = _containerLocks.GetOrAdd(packagePath, new SemaphoreSlim(1));

            try
            {
                await semaphore.WaitAsync();

                var splitFiles = storagePathsToKeep
                    .Select(x => new { Directory = Path.GetDirectoryName(x), FileName = Path.GetFileName(x) })
                    .GroupBy(arg => arg.Directory);

                var cf = GetCompoundFile(packagePath);
                foreach (var group in splitFiles)
                {
                    var files = group.Select(x => x.FileName).ToList();
                    var storage = cf.GetStorageForPath(group.Key);
                    storage.VisitEntries(item =>
                    {
                        if (!files.Contains(item.Name))
                        {
                            storage.Delete(item.Name);
                        }
                    }, false);

                    cf.Commit();
                }
            }
            finally
            {
                semaphore.Release();
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Retrieve a Compound File Container
        /// Note that this method does not lock the container lock in <see cref="_containerLocks"/>, this is the caller's responsibility
        /// </summary>
        /// <param name="packagePath">Path of the Compound File</param>
        /// <returns>CompoundFileContainer with the opened compound file</returns>
        private CompoundFileContainer GetCompoundFile(string packagePath)
        {
            if (!_openCompoundFiles.ContainsKey(packagePath))
            {
                _openCompoundFiles.TryAdd(packagePath, new CompoundFileContainer(packagePath));
            }

            return _openCompoundFiles[packagePath];
        }

        /// <summary>
        /// Split a filepath into a Directory and Filename
        /// </summary>
        /// <param name="filePath">Path to split</param>
        /// <returns>Tuple containing directory and filename</returns>
        private (string directory, string filename) SplitFilepath(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            var filename = Path.GetFileName(filePath);
            return new ValueTuple<string, string>(directory, filename);
        }

        #endregion

        #region Variables

        /// <summary>
        /// Collection of compound files that we manage
        /// </summary>
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _containerLocks;

        /// <summary>
        /// Collection of Compound File Containers that has already been opened
        /// </summary>
        private readonly ConcurrentDictionary<string, CompoundFileContainer> _openCompoundFiles;

        #endregion
    }
}