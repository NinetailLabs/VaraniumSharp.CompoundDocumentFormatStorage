using System;
using OpenMcdf;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VaraniumSharp.Attributes;
using VaraniumSharp.Enumerations;
using VaraniumSharp.Interfaces.Collections;
using VaraniumSharp.Logging;

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
            _managedFileLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
            _openFiles = new List<CompoundFile>();
            _logger = StaticLogger.GetLogger<CompoundFileManager>();
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
            var semaphore = _managedFileLocks.GetOrAdd(packagePath, new SemaphoreSlim(1));

            try
            {
                await semaphore.WaitAsync();

                using (var cf = GetCompoundFile(packagePath))
                {
                    var storage = GetStorage(cf, storagePath);
                    var filename = Path.GetFileName(storagePath);

                    var currentStream = storage.TryGetStream(filename);
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
                    _openFiles.Remove(cf);
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
            foreach (var file in _openFiles)
            {
                try
                {
                    file.Close();
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Error occured trying to close CompoundFile during disposal");
                }
            }
        }

        /// <inheritdoc />
        public async Task RemoveDataFromPackageAsync(string packagePath, string storagePath)
        {
            var semaphore = _managedFileLocks.GetOrAdd(packagePath, new SemaphoreSlim(1));

            try
            {
                await semaphore.WaitAsync();
                using (var cf = GetCompoundFile(packagePath))
                {
                    var storage = GetStorage(cf, storagePath);

                    var filename = Path.GetFileName(storagePath);
                    storage.Delete(filename);
                    cf.Commit();
                    _openFiles.Remove(cf);
                }
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <inheritdoc />
        public async Task<Stream> RetrieveDataFromPackageAsync(string packagePath, string storagePath)
        {
            var semaphore = _managedFileLocks.GetOrAdd(packagePath, new SemaphoreSlim(1));
            try
            {
                await semaphore.WaitAsync();
                using (var cf = GetCompoundFile(packagePath))
                {
                    var storage = GetStorage(cf, storagePath);

                    var filename = Path.GetFileName(storagePath);
                    var stream = storage.TryGetStream(filename);
                    if (stream == null)
                    {
                        await Task.Delay(1);
                        throw new KeyNotFoundException();
                    }

                    var data = stream.GetData();
                    _openFiles.Remove(cf);
                    return new MemoryStream(data);
                }
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <inheritdoc />
        public async Task ScrubStorageAsync(string packagePath, List<string> storagePathsToKeep)
        {
            var semaphore = _managedFileLocks.GetOrAdd(packagePath, new SemaphoreSlim(1));

            try
            {
                await semaphore.WaitAsync();
                using (var cf = GetCompoundFile(packagePath))
                { 
                    var splitFiles = storagePathsToKeep
                        .Select(x => new { Directory = Path.GetDirectoryName(x), FileName = Path.GetFileName(x) })
                        .GroupBy(arg => arg.Directory);

                    foreach (var group in splitFiles)
                    {
                        var files = group.Select(x => x.FileName).ToList();
                        var storage = GetStorage(cf, group.Key);
                        storage.VisitEntries(item =>
                        {
                            if (!files.Contains(item.Name))
                            {
                                storage.Delete(item.Name);
                            }
                        }, false);

                        cf.Commit();
                        _openFiles.Remove(cf);
                    }
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
        /// Retrieve a compound file.
        /// Note that this method does not lock the file lock in <see cref="_managedFileLocks"/>, this is the caller's responsibility
        /// </summary>
        /// <param name="packagePath">Path of the CF</param>
        /// <returns>Open compound file</returns>
        [SuppressMessage("ReSharper", "InconsistentlySynchronizedField", Justification = "This is done to prevent paying the lock cost on each access attempt - If the file is already in the collection there is no point in locking")]
        private CompoundFile GetCompoundFile(string packagePath)
        {
            if (!File.Exists(packagePath))
            {
                var ncf = new CompoundFile(CFSVersion.Ver_4, CFSConfiguration.Default);
                ncf.Save(packagePath);
                ncf.Close();
            }

            var stream = File.Open(packagePath, FileMode.Open, FileAccess.ReadWrite);
            var file = new CompoundFile(stream, CFSUpdateMode.Update, CFSConfiguration.Default);
            _openFiles.Add(file);

            return file;
        }

        /// <summary>
        /// Retrieve the CFStorage where the file in the path is stored
        /// </summary>
        /// <param name="compoundFile"></param>
        /// <param name="filePath">FilePath where file is stored (including filename)</param>
        /// <returns>CFStorage of appropriate level</returns>
        private static CFStorage GetStorage(CompoundFile compoundFile, string filePath)
        {
            var storage = compoundFile.RootStorage;
            foreach (var dir in SubFolders(filePath))
            {
                var subStorage = storage.TryGetStorage(dir);
                if (subStorage == null)
                {
                    subStorage = storage.AddStorage(dir);
                }

                storage = subStorage;
            }

            return storage;
        }

        /// <summary>
        /// Split a FilePath into the sub-directories
        /// </summary>
        /// <param name="fullPath">Full path to split</param>
        /// <returns>Sub-directories</returns>
        private static IEnumerable<string> SubFolders(string fullPath)
        {
            if (!fullPath.Contains(Path.DirectorySeparatorChar) && !fullPath.Contains(Path.AltDirectorySeparatorChar))
            {
                return new []{ fullPath };
            }

            var split = Path.GetDirectoryName(fullPath)?.Split(Path.DirectorySeparatorChar);
            return split
                ?.ToList()
                ?? new List<string>();
        }

        #endregion

        #region Variables

        /// <summary>
        /// Collection of compound files that we manage
        /// </summary>
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _managedFileLocks;

        /// <summary>
        /// Dictionary containing all currently open files
        /// </summary>
        private readonly List<CompoundFile> _openFiles;

        /// <summary>
        /// Logger instance
        /// </summary>
        private readonly ILogger _logger;

        #endregion
    }
}