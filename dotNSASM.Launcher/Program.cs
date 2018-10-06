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
            Util.BinaryInput = (path) =>
            {
                BinaryReader reader = new BinaryReader(new FileStream(path, FileMode.Open));
                byte[] bytes = reader.ReadBytes((int)reader.BaseStream.Length);
                reader.Close();
                return bytes;
            };
            Util.BinaryOutput = (path, bytes) =>
            {
                BinaryWriter writer = new BinaryWriter(new FileStream(path, FileMode.OpenOrCreate));
                writer.Write(bytes);
                writer.Flush();
                writer.Close();
            };

            Util.Print("NyaSama Assembly Script Module\n");
            Util.Print("Version: ");
            Util.Print(NSASM.Version);
            Util.Print("\n\n");

            if (args.Length < 1)
            {
                Util.Print("Usage: nsasm [c/r/i] [FILE]\n\n");
                Util.Interactive();
            }
            else
            {
                if (args.Length == 3)
                {
                    if (args[0].Equals("c"))
                    {
                        string res = Util.Compile(args[1], args[2]);
                        if (res != null)
                            Console.WriteLine("Compilation OK.\n");
                        return;
                    }
                }
                else if (args.Length == 2)
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
                    else if (args[0].Equals("c"))
                    {
                        string res = Util.Compile(args[1], null);
                        Console.WriteLine("\n" + res.ToString() + "\n");
                        return;
                    }
                    else
                    {
                        var segs = Util.GetSegments(Util.Read(args[0]));
                        NSASM nsasm = new NSASM(64, 32, 32, segs);
                        Stopwatch stopwatch = new Stopwatch();
                        stopwatch.Start();
                        nsasm.Call(args[1]);
                        stopwatch.Stop();
                        Console.WriteLine("This script took " +
                            stopwatch.Elapsed.TotalMilliseconds
                        + "ms.\n");
                        return;
                    }
                }
                if (args[0].Equals("i"))
                {
                    Util.Interactive();
                    return;
                }
                Util.Run(args[0]);
            }
        }
    }
}
