using OpenMcdf;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
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
            _managedFiles = new ConcurrentDictionary<string, CompoundFile>();
            _concurrentFileRetrievalLocks = new ConcurrentDictionary<string, object>();
        }

        #endregion

        #region Properties

        /// <inheritdoc />
        public bool AutoFlush { get; set; }

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public Task AddItemToPackageAsync(string packagePath, Stream data, string storagePath)
        {
            var cf = GetCompoundFile(packagePath);
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

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (var entry in _managedFiles)
            {
                entry.Value.Commit();
                entry.Value.Close();
            }
            _managedFiles.Clear();
        }

        /// <inheritdoc />
        public Task RemoveDataFromPackageAsync(string packagePath, string storagePath)
        {
            var cf = GetCompoundFile(packagePath);
            var storage = GetStorage(cf, storagePath);

            var filename = Path.GetFileName(storagePath);
            storage.Delete(filename);
            cf.Commit();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task<Stream> RetrieveDataFromPackageAsync(string packagePath, string storagePath)
        {
            var cf = GetCompoundFile(packagePath);
            var storage = GetStorage(cf, storagePath);

            var filename = Path.GetFileName(storagePath);
            var stream = storage.TryGetStream(filename);
            if (stream == null)
            {
                await Task.Delay(1);
                throw new KeyNotFoundException();
            }

            return new MemoryStream(stream.GetData());
        }

        /// <inheritdoc />
        public Task ScrubStorageAsync(string packagePath, List<string> storagePathsToKeep)
        {
            var cf = GetCompoundFile(packagePath);
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
            }

            return Task.CompletedTask;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Retrieve a compound file.
        /// This method will create the file if it does not exist, otherwise it will retrieve the file from the internal storage or simply open the current file
        /// </summary>
        /// <param name="packagePath">Path of the CF</param>
        /// <returns>Open compound file - Do not close!</returns>
        [SuppressMessage("ReSharper", "InconsistentlySynchronizedField", Justification = "This is done to prevent paying the lock cost on each access attempt - If the file is already in the collection there is no point in locking")]
        private CompoundFile GetCompoundFile(string packagePath)
        {
            if (!_managedFiles.ContainsKey(packagePath))
            {
                var padlock = _concurrentFileRetrievalLocks.GetOrAdd(packagePath, new object());
                lock (padlock)
                {
                    if (!_managedFiles.ContainsKey(packagePath))
                    {
                        if (!File.Exists(packagePath))
                        {
                            var ncf = new CompoundFile(CFSVersion.Ver_4, CFSConfiguration.Default);
                            ncf.Save(packagePath);
                            ncf.Close();
                        }

                        var stream = File.Open(packagePath, FileMode.Open, FileAccess.ReadWrite);
                        _managedFiles.TryAdd(packagePath, new CompoundFile(stream, CFSUpdateMode.Update, CFSConfiguration.Default));
                    }
                }
            }

            return _managedFiles[packagePath];
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
        /// Semaphores used to lock access when attempting to open/create compound files on a drive
        /// </summary>
        private readonly ConcurrentDictionary<string, object> _concurrentFileRetrievalLocks;

        /// <summary>
        /// Collection of compound files that we manage
        /// </summary>
        private readonly ConcurrentDictionary<string, CompoundFile> _managedFiles;

        #endregion
    }
}