using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using DokanNet;
using SevenZip;
using System.Threading;

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

        DokanNet.Logging.ILogger logger = new DokanNet.Logging.ConsoleLogger("[MyMirror]");

        public override NtStatus CreateFile(string fileName, DokanNet.FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, DokanFileInfo info)
        {
            var tid = Thread.CurrentThread.ManagedThreadId;
            /*
            logger.Debug(tid+" CreateFileProxy : {0}", fileName);
            logger.Debug(tid + " \tCreationDisposition\t{0}", (FileMode)mode);
            logger.Debug(tid + " \tFileAccess\t{0}", (DokanNet.FileAccess)access);
            logger.Debug(tid + " \tFileShare\t{0}", (FileShare)share);
            logger.Debug(tid + " \tFileOptions\t{0}", options);
            logger.Debug(tid + " \tFileAttributes\t{0}", attributes);
            logger.Debug(tid + " \tContext\t{0}", info);
            */
            if (IsBadName(fileName)) return NtStatus.ObjectNameInvalid;
            if ((access & ModificationAttributes) != 0) return NtStatus.DiskFull;

            var item = GetFile(fileName);
            if (item == null) return DokanResult.FileNotFound;
            if (item.Info.FullName != null && !isDirectory(item))
            {
                if ((access & DokanNet.FileAccess.ReadData) != 0)
                {
                    Console.WriteLine("MyMirror ReadData: " + fileName);

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
        
        private FsNode<FileInfo> GetFileInfo(string fileName, FileInfo prefileinfo = null)
        {
            logger.Debug("GetFile: " + fileName);

            if (!File.Exists(fileName) && !Directory.Exists(fileName) ||
                fileName.ToLower().EndsWith(".rar") ||
                fileName.ToLower().EndsWith(".zip")
                )
            {
                if (fileName.ToLower().Contains(".rar") || fileName.ToLower().Contains(".zip"))
                {
                    try
                    {
                        var info2 = new FileInfo(fileName);

                        var index = fileName.ToLower().IndexOf(".rar");
                        if (index == -1)
                            index = fileName.ToLower().IndexOf(".zip");

                        var file = fileName.Substring(0, index + 4);
                        var subpath = fileName.Substring(index + 4);

                        SevenZipFs fs;
                        cache.TryGetValue(file.ToLower(), out fs);
                        if (fs == null)
                        {
                            logger.Debug("SevenZipFs: get list " + file);
                            fs = new SevenZipFs(file);
                        }

                        cache[file.ToLower()] = fs;
                        var fsnodeinfo = fs.GetFile(subpath);

                        if (fsnodeinfo == null)
                            return null;


                        var answerarchive = new FsNode<FileInfo>()
                        {
                            Tag = fsnodeinfo,
                            Info = info2,
                            Name = info2.Name,
                            FullName = info2.FullName,
                        };

                        answerarchive.GetChildrenDelegate = () =>
                        {
                           

                            answerarchive.Tag = fsnodeinfo;

                            return fsnodeinfo?.Children?
                                .Select(x => GetFileInfo((
                                    file + Path.DirectorySeparatorChar + subpath +
                                    Path.DirectorySeparatorChar +
                                    x).Replace(""+Path.DirectorySeparatorChar + Path.DirectorySeparatorChar,
                                    ""+Path.DirectorySeparatorChar)))?.Where(a => a != null).ToList();
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

            FileInfo info;
            if (prefileinfo != null)
                info = prefileinfo;
            else
                info = new FileInfo(fileName);

            var answer = new FsNode<FileInfo>()
            {
                Info = info,
                Name = info.Name,
                FullName = info.FullName,
                GetChildrenDelegate = () =>
                {
                    DirectoryInfo dirinfo = new DirectoryInfo(info.FullName);

                    if (dirinfo.Exists)
                    {
                        var files = new ConcurrentBag<FsNode<FileInfo>>();
                        // get files
                        var fileInfos = dirinfo.GetFiles("*.rar", SearchOption.TopDirectoryOnly);
                        Parallel.ForEach(fileInfos, x =>
                        {
                            files.Add(GetFileInfo(x.FullName, x));
                        });
                        // filter files
                        var filefilter = files.Where(a =>
                            a != null &&
                            (a.Name.ToLower().Contains("part01.rar") || !a.Name.ToLower().Contains(".part")));
                        // get dirs
                        var dirs = dirinfo.GetDirectories("*", SearchOption.TopDirectoryOnly)
                            .Select(x => GetFileInfo(x.FullName));
                        // combine to one list
                        var combined = filefilter.Concat(dirs).Where(a => a != null);
                        // return result
                        return combined.ToList();
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
            if (File.Exists(info.FullName) && info.Name.ToLower().EndsWith(".7z"))
                return true;
            if (File.Exists(info.FullName) && info.Name.ToLower().EndsWith(".iso"))
                return true;
            if (info.Tag is FsNode<ArchiveFileInfo>)
            {
                //var children = info.GetChildrenDelegate();

                if (((((FsNode<ArchiveFileInfo>) info.Tag).Info.Attributes) & (uint) FileAttributes.Directory) > 0)
                    return true;
                return false;
            }
            return (info.Info.Attributes & FileAttributes.Directory) > 0;
        }

        FileInformation GetFileInformation(FsNode<FileInfo> item)
        {
            //logger.Debug("GetFileInformation: {0} {1}" ,item.FullName, item.Info.Attributes);
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

        public override NtStatus GetDiskFreeSpace(out long free, out long total, out long used, DokanFileInfo info)
        {
            free = 0;
            total = 1024l*1024*1024*1024;
            used = total;

            return NtStatus.Success;

            //return base.GetDiskFreeSpace(out free, out total, out used, info);
        }

        protected override IList<FileInformation> FindFilesHelper(string fileName, string searchPattern)
        {
            try
            {
                logger.Debug("FindFilesHelper: '{0}' '{1}'", fileName, searchPattern);
                
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
