using System;
using System.IO;
using System.Diagnostics;

namespace dotNSASM
{
    class Launcher
    {
        static void Main(string[] args)
        {
            Console.Title = ".NSASM Launcher";
            Util.Output = (value) => Console.Write(value);
            Util.Input = () => { return Console.ReadLine(); };
            Util.FileInput = (path) =>
            {
                StreamReader reader = new StreamReader(new FileStream(path, FileMode.Open));
                String var = reader.ReadToEnd();
                reader.Close();
                return var;
            };

            Util.Print("NyaSama Assembly Script Module\n");
            Util.Print("Version: ");
            Util.Print(NSASM.Version);
            Util.Print("\n\n");

            if (args.Length < 1)
            {
                Util.Print("Usage: nsasm [c/r] [FILE]\n\n");
                Util.Interactive();
            }
            else
            {
                if (args.Length == 2)
                {
                    if (args[0].Equals("r"))
                    {
                        Stopwatch stopwatch = new Stopwatch();
                        stopwatch.Start();
                        Util.Run(args[1]);
                        stopwatch.Stop();
                        Console.WriteLine("This script took " +
                            stopwatch.Elapsed.TotalMilliseconds
                        + "ms.\n");
                        return;
                    }
                }
                if (args[0].Equals("c"))
                {
                    Util.Interactive();
                    return;
                }
                Util.Run(args[0]);
            }
        }
    }
}
