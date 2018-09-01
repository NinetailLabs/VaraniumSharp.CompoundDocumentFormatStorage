using OpenMcdf;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VaraniumSharp.CompoundDocumentFormatStorage
{
    /// <summary>
    /// Class used to manage open Compound Files
    /// </summary>
    internal class CompoundFileContainer
    {
        #region Constructor

        /// <summary>
        /// Construct with a file path.
        /// This will open the CompoundFile and store it
        /// </summary>
        /// <param name="filePath"></param>
        public CompoundFileContainer(string filePath)
        {
            FilePath = filePath;
            OpenCompoundFile();
            _storageCollection = new ConcurrentDictionary<string, CFStorage>();
        }

        #endregion Constructor

        #region Properties

        /// <summary>
        /// Path to the Compound File managed by this container
        /// </summary>
        public string FilePath { get; }

        #endregion Properties

        #region Public Methods

        /// <summary>
        /// Close the Compound File
        /// </summary>
        public void Close()
        {
            _compoundFile.Close();
        }

        /// <summary>
        /// Commit in memory data to the backing stream
        /// </summary>
        public void Commit()
        {
            _compoundFile.Commit();
        }

        /// <summary>
        /// Retrieve the correct Storage in the Compound File.
        /// If the storage does not yet exist it will be created
        /// </summary>
        /// <param name="directoryPath">Storage directory path</param>
        /// <returns>Storage instance</returns>
        public CFStorage GetStorageForPath(string directoryPath)
        {
            if (!_storageCollection.ContainsKey(directoryPath))
            {
                _storageCollection.TryAdd(directoryPath, GetStorage(_compoundFile, directoryPath));
            }

            return _storageCollection[directoryPath];
        }

        #endregion Public Methods

        #region Private Methods

        /// <summary>
        /// Retrieve the CFStorage where the file in the path is stored
        /// </summary>
        /// <param name="compoundFile"></param>
        /// <param name="directory">Directory path for the storage</param>
        /// <returns>CFStorage of appropriate level</returns>
        private static CFStorage GetStorage(CompoundFile compoundFile, string directory)
        {
            var storage = compoundFile.RootStorage;
            foreach (var dir in SubFolders(directory))
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
        /// Open a Compound File or, if it does not exist yet create it
        /// </summary>
        private void OpenCompoundFile()
        {
            if (!File.Exists(FilePath))
            {
                var ncf = new CompoundFile(CFSVersion.Ver_4, CFSConfiguration.Default);
                ncf.Save(FilePath);
                ncf.Close();
            }

            var stream = File.Open(FilePath, FileMode.Open, FileAccess.ReadWrite);
            _compoundFile = new CompoundFile(stream, CFSUpdateMode.Update, CFSConfiguration.Default);
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
                return new[] { fullPath };
            }

            if (!fullPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                fullPath = $"{fullPath}{Path.DirectorySeparatorChar}";
            }

            var split = Path.GetDirectoryName(fullPath)?.Split(Path.DirectorySeparatorChar);
            return split
                ?.ToList()
                ?? new List<string>();
        }

        #endregion Private Methods

        #region Variables

        /// <summary>
        /// Collection of opened CFStorage
        /// </summary>
        private readonly ConcurrentDictionary<string, CFStorage> _storageCollection;

        /// <summary>
        /// CompoundFile instance for this container
        /// </summary>
        private CompoundFile _compoundFile;

        #endregion Variables
    }
}