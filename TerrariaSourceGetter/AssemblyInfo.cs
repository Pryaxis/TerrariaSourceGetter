
using System;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace TerrariaSourceGetter
{
    public enum Side
    {
        Client,
        Server,
        Unknown,
    }

    public enum Platform
    {
        Windows,
        Linux,
        Mac,
        Unknown,
    }
    
    public class AssemblyInfo : IDisposable
    {
        public byte[] FileRawBytes { get; }
        public AssemblyDefinition Assembly { get; }
        public Side Side { get; }
        public Platform Platform { get; }
        public Version AssemblyVersion { get; }
        public int ReleaseNumber { get; }

        private MemoryStream asmStream;

        public AssemblyInfo(Stream stream)
        {
            if(stream is null) throw new ArgumentNullException(nameof(stream));
            using (var br = new BinaryReader(stream))
                FileRawBytes = br.ReadBytes((int) stream.Length);
            asmStream = new MemoryStream(FileRawBytes);
            Assembly = AssemblyDefinition.ReadAssembly(asmStream);

            switch (Assembly.Name.Name)
            {
            case "Terraria":
                Side = Side.Client;
                break;
            case "TerrariaServer":
                Side = Side.Server;
                break;
            default:
                Side = Side.Unknown;
                break;
            }
            
            switch (Assembly.MainModule.EntryPoint.DeclaringType.FullName)
            {
            case "Terraria.WindowsLaunch":
                Platform = Platform.Windows;
                break;
            case "Terraria.LinuxLaunch":
                Platform = Platform.Linux;
                break;
            case "Terraria.MacLaunch":
                Platform = Platform.Mac;
                break;
            default:
                Platform = Platform.Unknown;
                break;
            }

            AssemblyVersion = Assembly.Name.Version;

            ReleaseNumber = (int) Assembly.MainModule
                .Types.First(t => t.FullName == "Terraria.Main")
                .Fields.First(f => f.Name == "curRelease")
                .Constant;
        }

        public void Dispose()
        {
            Assembly?.Dispose();
            asmStream?.Dispose();
        }
    }
}