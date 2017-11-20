using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DokanNet;
using SevenZip;

namespace Shaman.Dokan
{
    public class MyMirror : ReadOnlyFs
    {
        private string path = "";

        FsNode<FileInfo> root;

        public MyMirror(string path)
        {
            this.path = path.TrimEnd('\\');

            root = GetFileInfo(this.path);
        }
        
        public override string SimpleMountName => "MyMirror-" + path;

        private object readerLock = new object();

        public override NtStatus CreateFile(string fileName, DokanNet.FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, DokanFileInfo info)
        {
            if (IsBadName(fileName)) return NtStatus.ObjectNameInvalid;
            if ((access & ModificationAttributes) != 0) return NtStatus.DiskFull;

            var item = GetFile(fileName);
            if (item == null) return DokanResult.FileNotFound;
            if (item.Info.FullName != null && !isDirectory(item))
            {
                if ((access & DokanNet.FileAccess.ReadData) != 0)
                {
                    Console.WriteLine("ReadData: " + fileName);

                    var archive = item.Tag as FsNode<ArchiveFileInfo>;

                    if (archive != null)
                    {
                        var idx = fileName.ToLower().IndexOf(archive.FullName.ToLower());
                        var file = fileName.Substring(0, idx-1);

                        try
                        {
                            return cache[path + file.ToLower()].CreateFile(archive.FullName, access, share, mode, options,
                                attributes,
                                info);
                        }
                        catch (Exception ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine(ex);
                            Console.ForegroundColor = ConsoleColor.White;
                            return NtStatus.AccessDenied;
                        }
                    }
                    else
                    {
                        info.Context =
                            File.Open(path +
                                          fileName, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read);
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

        Dictionary<string,SevenZipFs> cache = new Dictionary<string, SevenZipFs>();

        public FsNode<FileInfo> GetFile(string fileName)
        {
            return GetNode(root, fileName);
        }
        
        private FsNode<FileInfo> GetFileInfo(string fileName)
        {
            FsNode<FileInfo> answer;

            Console.WriteLine("GetFile: " + fileName);

            if (!File.Exists(fileName) && !Directory.Exists(fileName) || fileName.ToLower().EndsWith(".rar") || fileName.ToLower().EndsWith(".zip"))
            {
                if (fileName.ToLower().Contains(".rar")|| fileName.ToLower().Contains(".zip"))
                {
                    var index = fileName.ToLower().IndexOf(".rar");

                    if (index == -1)
                        index = fileName.ToLower().IndexOf(".zip");

                    var file = fileName.Substring(0, index + 4);
                    var subpath = fileName.Substring(index+4);

                    try
                    {
                        SevenZipFs fs;

                        cache.TryGetValue(file.ToLower(), out fs);

                        if(fs == null)
                            fs = new SevenZipFs(file);

                        cache[file.ToLower()] = fs;

                        var fsnodeinfo = fs.GetFile(subpath);

                        if (fsnodeinfo == null)
                            return null;

                        var info2 = new FileInfo(fileName);

                        //if((fsnodeinfo.Info.Attributes & (uint)FileAttributes.Directory) != 0)
                        //info2.Attributes = info2.Attributes | FileAttributes.Directory;

                        var answerarchive = new FsNode<FileInfo>()
                        {
                            Tag = fsnodeinfo,
                            Info = info2,
                            Name = info2.Name,
                            FullName = info2.FullName,
                            GetChildrenDelegate = () =>
                            {
                                return fsnodeinfo?.Children?
                                    .Select(x => GetFileInfo(
                                            file + Path.DirectorySeparatorChar + subpath + Path.DirectorySeparatorChar + x))?
                                            .ToList();
                            }
                        };

                        return answerarchive;
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(ex);
                        Console.ForegroundColor = ConsoleColor.White;
                        return null;
                    }
                }

                return null;
            }

            var info = new FileInfo(fileName);
            answer = new FsNode<FileInfo>()
            {
                Info = info,
                Name = info.Name,
                FullName = info.FullName,
                GetChildrenDelegate = () =>
                {
                    if (Directory.Exists(info.FullName))
                    {
                        var files = Directory.GetFiles(info.FullName, "*", SearchOption.TopDirectoryOnly).Select(x => GetFileInfo(x)).ToList();
                        var dirs = Directory.GetDirectories(info.FullName, "*", SearchOption.TopDirectoryOnly).Select(x => GetFileInfo(x));
                        return files.Concat(dirs).ToList();
                    }
                    return null;
                }
            };

            return answer;
        }

        public override NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, DokanFileInfo info)
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

        bool isDirectory(FsNode<FileInfo> info)
        {
            if (File.Exists(info.FullName) && info.Name.ToLower().EndsWith(".rar"))
                return true;
            if (File.Exists(info.FullName) && info.Name.ToLower().EndsWith(".zip"))
                return true;
            if (info.Tag != null)
            {
                if (((((FsNode<ArchiveFileInfo>) info.Tag).Info.Attributes) & (uint) FileAttributes.Directory) > 0)
                    return true;
                return false;
            }
            return (info.Info.Attributes & FileAttributes.Directory) > 0;
        }

        FileInformation GetFileInformation(FsNode<FileInfo> item)
        {
            if (item == null)
                return new FileInformation();

            var isdir = isDirectory(item);
            long length = isdir ? 0 : item.Info.Exists ? item.Info.Length : 0;
            FileAttributes attrib = item.Info.Attributes;

            var archive = item.Tag as FsNode<ArchiveFileInfo>;

            if (archive != null)
            {
                length = (long) archive.Info.Size;
                attrib = (FileAttributes)archive.Info.Attributes;
                return new FileInformation()
                {
                    FileName = item.Info.Name,
                    Length = length,
                    Attributes = isdir ? (FileAttributes.Directory) : attrib,
                    CreationTime = archive.Info.CreationTime,
                    LastAccessTime = archive.Info.LastAccessTime,
                    LastWriteTime = archive.Info.LastWriteTime,
                };
            }

            return new FileInformation()
            {
                FileName = item.Info.Name,
                Length = length,
                Attributes = isdir ? (FileAttributes.Directory) : attrib,
                CreationTime = item.Info.CreationTime,
                LastAccessTime = item.Info.LastAccessTime,
                LastWriteTime = item.Info.LastWriteTime,
            };
        }

        public override NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features, out string fileSystemName, DokanFileInfo info)
        {
            fileSystemName = volumeLabel = "MyMirror";
            features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.ReadOnlyVolume | FileSystemFeatures.UnicodeOnDisk | FileSystemFeatures.VolumeIsCompressed;
            return NtStatus.Success;
        }

        protected override IList<FileInformation> FindFilesHelper(string fileName, string searchPattern)
        {
            try
            {
                var item = GetFile(fileName);
                if (item == null) return null;

                if (isDirectory(item))
                {
                    if (item.Children == null) return new FileInformation[] { };
                    var matcher = GetMatcher(searchPattern);
                    var where = item.Children.Where(x => x != null && matcher(x.Name));
                    var cnt1 = where.Count();
                    var select = where.Select(x => GetFileInformation(x));
                    var cnt2 = select.Count();
                    var list = select.ToList();

                    return list;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex);
                Console.ForegroundColor = ConsoleColor.White;
            }

            return new List<FileInformation>();
        }

        public override void Cleanup(string fileName, DokanFileInfo info)
        {
        }
    }
}
