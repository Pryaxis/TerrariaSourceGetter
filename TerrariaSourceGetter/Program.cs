using System;
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
                
                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine($"Complete. Took {stopwatch.Elapsed.TotalSeconds}s");
            }
            
            progressReporter.Dispose();
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
        
        const string URL = "https://terraria.org/system/dedicated_servers/archives/000/000/038/original/terraria-server-1404.zip";
        const string FileName = "terraria-server-1404";
        
        static void DecompileDedicatedServer()
        {
            var zipName = $"{FileName}.zip";
            
            if (File.Exists(zipName))
            {
                Console.WriteLine("File existed, do you want to download it again? [y/N]");
                if (Console.ReadKey().Key == ConsoleKey.Y)
                    DownloadDedicatedServerBin(zipName);
                Console.WriteLine();
            }
            else
            {
                DownloadDedicatedServerBin(zipName);
            }

            if (Directory.Exists(FileName))
            {
                Console.WriteLine("There has been extracted files, do you want to extract it again? [y/N]");
                if (Console.ReadKey().Key == ConsoleKey.Y)
                    ExtractFiles(zipName);
                Console.WriteLine();
            }
            else
            {
                ExtractFiles(zipName);
            }

            Console.WriteLine("Choose the platform you want to decompile: [W(Windows)/l(Linux)/m(Mac)/a(All)]");
            var commonDirPath = Path.GetFullPath(Path.Combine(FileName, "1404"));
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

        static void ExtractFiles(string zipFile)
        {
            Console.WriteLine("Start to extract files...");
            Directory.CreateDirectory(FileName);
            ZipFile.ExtractToDirectory(zipFile, FileName);
            Console.WriteLine("Extracted");
        }
        
        static void DownloadDedicatedServerBin(string fileName)
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
                    webClient.DownloadFileTaskAsync(new Uri(URL), fileName).Wait();
                }
            }
            Console.WriteLine();
            Console.WriteLine("Downloaded.");
        }
    }
}