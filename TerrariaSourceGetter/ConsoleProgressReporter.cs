using System;
using ConsoleProgressBar;
using ICSharpCode.Decompiler.CSharp;

namespace TerrariaSourceGetter
{
    public class ConsoleProgressReporter : IProgress<DecompilationProgress>, IDisposable
    {
        private ProgressBar progressBar = new ProgressBar { StatusOnSeparateLine = true };
        
        private bool started;
        private int totalNumberOfFiles;
        private int decompiledFileCount;
        
        public void Report(DecompilationProgress value)
        {
            if (!started)
            {
                started = true;
                totalNumberOfFiles = value.TotalNumberOfFiles;
                Console.WriteLine($"Total files: {totalNumberOfFiles}");
            }

            decompiledFileCount++;
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            progressBar.Progress.Report((double) decompiledFileCount / totalNumberOfFiles
                , $"Decompiled {value.Status}");
            Console.ForegroundColor = ConsoleColor.White;
        }


        public void Dispose()
        {
            progressBar?.Dispose();
        }
    }
}