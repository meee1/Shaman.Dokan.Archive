using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using SevenZip;
using System.IO;
using System.Threading;
using DokanNet;

namespace Shaman.Dokan
{
    class SevenZipProgram
    {
        static bool cancel = false;

        static int Main(string[] args)
        {
            SevenZipExtractor.SetLibraryPath(
                Path.Combine(Path.GetDirectoryName(typeof(SevenZipProgram).Assembly.Location), "7z.dll"));
            var filedir = args.FirstOrDefault(x => !x.StartsWith("-"));
            if (filedir == null)
            {
                Console.WriteLine("Must specify a file.");
                return 1;
            }

            if (Directory.Exists(filedir))
                filedir = filedir.TrimEnd('\\') + Path.DirectorySeparatorChar;

            Console.CancelKeyPress += Console_CancelKeyPress;

            var myfs = new MyMirror(filedir);
            myfs.Mount("X:", DokanOptions.NetworkDrive, 4);

            return 0;

            var rars = Directory.GetFiles(Path.GetDirectoryName(filedir), "*.rar", SearchOption.AllDirectories);

            List<String> mounts = new List<string>();

            Parallel.ForEach(rars, (rar) =>
                    //foreach (var rar in rars)
                {
                    try
                    {
                        var file = Path.GetFullPath(rar);
                        var mountdest = Path.GetDirectoryName(file) + Path.DirectorySeparatorChar +
                                        Path.GetFileNameWithoutExtension(file);

                        var szfs = new SevenZipFs(file);
                        Console.WriteLine(szfs.SimpleMountName);
                        Console.WriteLine(mountdest);

                        Directory.CreateDirectory(mountdest);

                        mounts.Add(mountdest);

                        new Thread(() =>
                        {
                            szfs.Mount(mountdest, DokanOptions.MountManager, 4);
                        }).Start();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }
            );

      
            while (!cancel)
                Thread.Sleep(500);

            foreach (var mount in mounts)
            {
                if (DokanNet.Dokan.RemoveMountPoint(mount))
                {
                    Directory.Delete(mount);
                }
            }

            //szfs.MountSimple(4);
            //if (args.Contains("--open"))
            //Process.Start(mountdest);

            return 0;
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            cancel = true;
        }
    }
}
