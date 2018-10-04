using System;
using System.IO;

namespace dotNSASM.PreProcessor
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = ".NSASM Pre-Processor";
            Util.Output = (value) => Console.Write(value);
            Util.Input = () => { return Console.ReadLine(); };
            Util.FileInput = (path) =>
            {
                StreamReader reader = new StreamReader(new FileStream(path, FileMode.Open));
                String var = reader.ReadToEnd();
                reader.Close();
                return var;
            };

            Util.Print("NSASM PreP\n");
            Util.Print("\n\n");

            if (args.Length < 1)
            {
                Util.Print("Usage: prep [FILE]\n\n");
            }
            else
            {
                string str = Util.Read(args[0]);
                Utility.GetDefBlocks(str);
            }
        }
    }
}
