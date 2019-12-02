using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DokanNet;
using SharpCompress.Archives.Rar;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Readers.Rar;

namespace Shaman.Dokan
{
    public class SharpCompressFs : ReadOnlyFs
    {
        static DokanNet.Logging.ILogger logger = new DokanNet.Logging.ConsoleLogger("[SharpCompressFs]");

        private RarArchive _ext;
        public RarArchive extractor
        {
            get { if (_ext == null || _ext.disposed) _ext = RarArchive.Open(zipfile); return _ext; }
            set { _ext = value; }
        }
        private FsNode<RarArchiveEntry> root;

        ~SharpCompressFs()
        {
            _ext?.Dispose();
        }
        public SharpCompressFs(string path)
        {
            lock (this)
            {
                zipfile = path;

                root = CreateTree<RarArchiveEntry>(extractor.Entries, x => x.Key, x => x.IsDirectory);

                CheckDirectorys(root);

                extractor.Dispose();

                extractor = null;
            }
        }

        void CheckDirectorys(FsNode<RarArchiveEntry> myroot)
        {
            foreach (var item in myroot.Children)
            {
                if (item.Children != null && item.Children.Count > 0)
                {
                    var info = ((RarArchiveEntry) item.Info);
                    //info.Attributes = (int)FileAttributes.Directory;

                    item.Info = info;

                    CheckDirectorys(item);
                }
            }
        }

        private string zipfile;
        public override string SimpleMountName => "SharpCompressFs-" + zipfile;

        private object readerLock = new object();

        public override NtStatus CreateFile(string fileName, DokanNet.FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, IDokanFileInfo info)
       {
            if (IsBadName(fileName)) return NtStatus.ObjectNameInvalid;
            if ((access & ModificationAttributes) != 0) return NtStatus.DiskFull;

            var item = GetFile(fileName);
            if (item == null) return DokanResult.FileNotFound;
            if (item.Info.Key != null && !item.Info.IsDirectory)
            {
                if ((access & (DokanNet.FileAccess.ReadData | DokanNet.FileAccess.GenericRead)) != 0)
                {
                    Console.WriteLine("SharpCompressFs ReadData: " + fileName);

                    lock (this)
                    {
                        var entry = extractor.Entries.First(a => a.Key.EndsWith(fileName));

                        if (entry.RarParts.First().FileHeader.PackingMethod == 0x30)
                        {
                            // stored
                            info.Context = new RarStoreStream(entry);
                        }
                        else
                        {
                            //info.Context = new MemoryStream(entry.OpenEntryStream());
                            return NtStatus.AccessDenied;
                        }
                    }
                }
                return NtStatus.Success;
            }
            else
            {
                info.IsDirectory = true;
                return NtStatus.Success;
            }
        }

        public override NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, IDokanFileInfo info)
        {
            var item = GetFile(fileName);
            if (item == null)
            {
                fileInfo = default(FileInformation);
                return DokanResult.FileNotFound;
            }
            fileInfo = GetFileInformation(item);
            return NtStatus.Success;
        }

        public override NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features, out string fileSystemName, IDokanFileInfo info)
        {
            fileSystemName = volumeLabel = "SharpCompressFs";
            features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.ReadOnlyVolume | FileSystemFeatures.UnicodeOnDisk | FileSystemFeatures.VolumeIsCompressed;
            return NtStatus.Success;
        }

        public FsNode<RarArchiveEntry> GetFile(string fileName)
        {
            return GetNode(root, fileName);
        }

        protected override IList<FileInformation> FindFilesHelper(string fileName, string searchPattern)
        {
            var item = GetFile(fileName);
            if (item == null) return null;

            if (item == root || item.Info.IsDirectory)
            {
                if (item.Children == null) return new FileInformation[] { };
                var matcher = GetMatcher(searchPattern);
                return item.Children.Where(x => matcher(x.Name)).Select(x => GetFileInformation(x)).ToList();
            }
            return null;
        }

        private FileInformation GetFileInformation(FsNode<RarArchiveEntry> item)
        {
            return new FileInformation()
            {
                Attributes = item == root ? FileAttributes.Directory : FileAttributes.ReadOnly,
                CreationTime = item.Info.CreatedTime,
                FileName = item.Name,
                LastAccessTime = item.Info.LastAccessedTime,
                LastWriteTime = item.Info.LastModifiedTime,
                Length = (long)item.Info.Size
            };
        }

        public override void Cleanup(string fileName, IDokanFileInfo info)
        {
        }
    }
}
