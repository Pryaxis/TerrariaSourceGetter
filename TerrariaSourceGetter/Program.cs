using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using ConsoleProgressBar;
using Humanizer;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using Mono.Cecil;

namespace TerrariaSourceGetter
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args is null || args.Length == 0)
            {
                DecompileDedicatedServer();
            }
            else if (args.Length == 1)
            {
                var asmPath = args[0];
                if (!File.Exists(asmPath))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Invalid path: {asmPath}");
                    return;
                }

                Console.WriteLine(asmPath);
                var asmInfo = new AssemblyInfo(File.OpenRead(asmPath));
                var dirPath = Path.GetDirectoryName(asmPath);
                Decompile(asmInfo, additionalSearchDirs: new []{ dirPath });
            }
        }
        
        public static void Decompile(AssemblyInfo asmInfo, 
            string outputDirPath = null,
            bool ignoreError = false,
            string[] additionalSearchDirs = null)
        {
            if (outputDirPath is null)
                outputDirPath = $"{asmInfo.AssemblyVersion}-{asmInfo.ReleaseNumber}-{asmInfo.Platform.Humanize()}-{asmInfo.Side.Humanize()}";

            if (Directory.Exists(outputDirPath))
            {
                var outDirInfo = new DirectoryInfo(outputDirPath);
                if (outDirInfo.GetFiles().Any())
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"{outDirInfo.FullName} is not empty! \n Continue? [y/N]");
                    Console.ForegroundColor = ConsoleColor.White;
                    if (Console.ReadKey().Key != ConsoleKey.Y)
                        return;
                }
            }
            else
            {
                Directory.CreateDirectory(outputDirPath);
            }

            var referenceDirPath = Path.Combine(outputDirPath, "references");
            Directory.CreateDirectory(referenceDirPath);
            ExtractReferences(asmInfo, referenceDirPath);

            var decompiler = new WholeProjectDecompiler();
            
            var progressReporter = new ConsoleProgressReporter();
            decompiler.ProgressIndicator = progressReporter;
            var resolver = new UniversalAssemblyResolver("./", !ignoreError, "");
            resolver.AddSearchDirectory(new DirectoryInfo(referenceDirPath).FullName);
            if (additionalSearchDirs != null)
            {
                foreach (var d in additionalSearchDirs)
                {
                    resolver.AddSearchDirectory(d);
                }
            }
            decompiler.AssemblyResolver = resolver;
            
            using (var ms = new MemoryStream(asmInfo.FileRawBytes))
            {
                Console.WriteLine();
                Console.WriteLine("Start decompiling...");
                var stopwatch = new Stopwatch();
                stopwatch.Start();
                decompiler.DecompileProject(new PEFile(asmInfo.Assembly.Name.Name, ms), outputDirPath);
                stopwatch.Stop();

                var csprojPath = Path.Combine(outputDirPath, $"{asmInfo.Assembly.Name.Name}.csproj");
                File.WriteAllText(csprojPath, PostProcessProjectFile(File.ReadAllText(csprojPath)));
            
                progressReporter.Dispose();
                
                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine($"Complete. Took {stopwatch.Elapsed.TotalSeconds}s");
            }
        }

        //TODO: Operate xml doc
        static string PostProcessProjectFile(string csproj)
        {
            return csproj
                .Replace("<TargetFrameworkProfile>Client</TargetFrameworkProfile>", "")
                .Replace("<TargetFrameworkVersion>v4.0</TargetFrameworkVersion>", "<TargetFrameworkVersion>v4.5</TargetFrameworkVersion>")
                .Replace("<Reference Include=\"System.Core\">", "<Reference Include=\"System.Xml\" />\n    <Reference Include=\"System.Core\">");
        }

        static void ExtractReferences(AssemblyInfo asmInfo, string targetDir)
        {
            foreach (var r in asmInfo.Assembly.MainModule.Resources
                .Where(r => r.ResourceType == Mono.Cecil.ResourceType.Embedded && r.Name.EndsWith(".dll"))
                .Select(r => r as EmbeddedResource))
            {
                if (r is null) continue;
                var asmName = AssemblyDefinition.ReadAssembly(r.GetResourceStream()).Name.Name;
                File.WriteAllBytes(Path.Combine(targetDir, $"{asmName}.dll"), r.GetResourceData());
            }
        }
        
        const string BaseURL = "https://terraria.org/system/dedicated_servers/archives/000/000/{0:D3}/original/terraria-server-{1}.zip";
        const string BaseFileName = "terraria-server-{0}";

        private static readonly Dictionary<int, int> _versions = new Dictionary<int, int>
        {
            [39] = 1405,
            [38] = 1404,
            [37] = 1403,
            [36] = 1402,
            [35] = 1401,
            
            [34] = 1353,
            [33] = 1352,
            
            [26] = 1344,
            [25] = 1343,
            [24] = 1342,
            [23] = 1341,
            [22] = 134,
            
            [21] = 1333,
            [20] = 1332,
            [19] = 1331,
            [18] = 133,
            
            [15] = 1321,
            [14] = 132,
                
            [13] = 1311,
            [11] = 131,
            
            [11] = 131,
            [9] = 1307,
            [8] = 1306,
            [7] = 1305,
            [6] = 1304,
            [5] = 1303,
            [4] = 1302,
            [3] = 1301,
        };

        static void DecompileDedicatedServer()
        {
            bool inputValid = false;
            var versions = _versions.Select(p => (p.Key, p.Value)).ToList();
            int selectedVersion = 0;
            while (!inputValid)
            {
                Console.Clear();
                Console.WriteLine("Avaliable versions:");
                for (int i = 0; i < versions.Count; i++)
                {
                    Console.WriteLine("{0, 3}.    {1, 6}".FormatWith(i, versions[i].Value));
                }

                Console.WriteLine("Input the No. of the version you want to decompile:");
                if (int.TryParse(Console.ReadLine(), out int v))
                {
                    if (v >= 0 && v < versions.Count)
                    {
                        selectedVersion = v;
                        inputValid = true;
                    }
                }
            }
            
            DecompileDedicatedServerOfVersion(versions[selectedVersion].Key);
        }
        
        static void DecompileDedicatedServerOfVersion(int versionNumber)
        {
            var version = _versions[versionNumber];
            var fileName = $"{BaseFileName.FormatWith(version)}";
            var zipName = $"{BaseFileName.FormatWith(version)}.zip";

            if (File.Exists(zipName))
            {
                Console.WriteLine("File existed, do you want to download it again? [y/N]");
                if (Console.ReadKey().Key == ConsoleKey.Y)
                    DownloadDedicatedServerBin(BaseURL.FormatWith(versionNumber, version), zipName);
                Console.WriteLine();
            }
            else
            {
                DownloadDedicatedServerBin(BaseURL.FormatWith(versionNumber, version), zipName);
            }

            if (Directory.Exists(fileName))
            {
                Console.WriteLine("There has been extracted files, do you want to extract it again? [y/N]");
                if (Console.ReadKey().Key == ConsoleKey.Y)
                    ExtractFiles(zipName, fileName);
                Console.WriteLine();
            }
            else
            {
                ExtractFiles(zipName, fileName);
            }

            Console.WriteLine("Choose the platform you want to decompile: [W(Windows)/l(Linux)/m(Mac)/a(All)]");
            var commonDirPath = Path.GetFullPath(Path.Combine(fileName, version.ToString()));
            switch (Console.ReadKey().Key)
            {
            case ConsoleKey.L:
                DecompileLinux(Path.Combine(commonDirPath, "Linux"));
                break;
            case ConsoleKey.M:
                DecompileMac(Path.Combine(commonDirPath, "Mac"));
                break;
            case ConsoleKey.A:
                DecompileWindows(Path.Combine(commonDirPath, "Windows"));
                DecompileLinux(Path.Combine(commonDirPath, "Linux"));
                DecompileMac(Path.Combine(commonDirPath, "Mac"));
                break;
            default:
                DecompileWindows(Path.Combine(commonDirPath, "Windows"));
                break;
            }
        }

        static void DecompileWindows(string dirPath)
        {
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("Decompiling Windows server assembly...");
            var asmInfo = new AssemblyInfo(File.OpenRead(Path.Combine(dirPath, "TerrariaServer.exe")));
            Decompile(asmInfo);
        }

        static void DecompileLinux(string dirPath)
        {
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("Decompiling Linux server assembly...");
            var asmInfo = new AssemblyInfo(File.OpenRead(Path.Combine(dirPath, "TerrariaServer.exe")));
            Decompile(asmInfo, additionalSearchDirs: new []{ dirPath });
        }

        static void DecompileMac(string dirPath)
        {
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("Decompiling Mac server assembly...");
            var realDirPath = Path.Combine(dirPath, "Terraria Server.app", "Contents", "MacOS");
            var asmInfo = new AssemblyInfo(File.OpenRead(Path.Combine(realDirPath, "TerrariaServer.exe")));
            Decompile(asmInfo, additionalSearchDirs: new []{ realDirPath });
        }

        static void ExtractFiles(string zipFile, string targetDir)
        {
            Console.WriteLine("Start to extract files...");
            Directory.CreateDirectory(targetDir);
            ZipFile.ExtractToDirectory(zipFile, targetDir);
            Console.WriteLine("Extracted");
        }
        
        static void DownloadDedicatedServerBin(string url, string fileName)
        {
            using (var webClient = new WebClient())
            {
                using (var pb = new ProgressBar())
                {
                    Console.WriteLine("Downloading the dedicated server binary from terraria.org...");
                    webClient.DownloadProgressChanged += (sender, args) =>
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        pb.Progress.Report(args.ProgressPercentage / 100.0);
                        Console.ForegroundColor = ConsoleColor.White;
                    };
                    webClient.DownloadFileTaskAsync(new Uri(url), fileName).Wait();
                }
            }
            Console.WriteLine();
            Console.WriteLine("Downloaded.");
        }
    }
}