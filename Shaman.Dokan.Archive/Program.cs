using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using SevenZip;
using System.IO;
using System.Threading;
using DokanNet;
using DokanNet.Logging;
using SharpCompress.Archives.Rar;

namespace Shaman.Dokan
{
    class SevenZipProgram
    {
        static bool cancel = false;
        static List<string> mounts = new List<string>();

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

            Console.WriteLine("64bit process: " + Environment.Is64BitProcess);

            if (Directory.Exists(filedir))
                filedir = filedir.TrimEnd('\\') + Path.DirectorySeparatorChar;

            Console.CancelKeyPress += Console_CancelKeyPress;
            Archive.ConsoleExit.Setup((type) =>
            {
                cleanup();
                cancel = true;
                return true;
            });

            new Thread(() =>
            {
                var myfs = new MyMirror(filedir);
                mounts.Add("X:");
                myfs.Mount("X:", DokanOptions.NetworkDrive, 4, new NullLogger());

            }).Start();
      
            while (!cancel)
                Thread.Sleep(500);

            cleanup();

            return 0;
        }

        private static void cleanup()
        {
            foreach (var mount in mounts)
            {
                Console.WriteLine("RemoveMountPoint {0}", mount);
                if (DokanNet.Dokan.RemoveMountPoint(mount))
                {

                }
            }
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            cancel = true;
        }
    }
}
