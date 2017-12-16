using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;

namespace dotNSASM
{
    public class Util
    {
        public delegate void Printer(Object value);
        public delegate string Scanner();
        public delegate string FileReader(string path);

        public static Printer Output
        {
            internal get; set;
        }
        public static Scanner Input
        {
            internal get; set;
        }
        public static FileReader FileInput
        {
            internal get; set;
        }

        public static void Print(Object value)
        {
            Output.Invoke(value);
        }

        public static string Scan()
        {
            return Input.Invoke();
        }

        private static string CleanSymbol(string var, string symbol, string trash)
        {
            string tmp = var;
            while (tmp.Contains(symbol + trash))
                tmp = tmp.Replace(symbol + trash, symbol);
            while (tmp.Contains(trash + symbol))
                tmp = tmp.Replace(trash + symbol, symbol);
            return tmp;
        }

        private static string CleanSymbol(string var, string symbol, string trashA, string trashB)
        {
            string tmp = var;
            while (tmp.Contains(symbol + trashA) || tmp.Contains(symbol + trashB))
                tmp = tmp.Replace(symbol + trashA, symbol).Replace(symbol + trashB, symbol);
            while (tmp.Contains(trashA + symbol) || tmp.Contains(trashB + symbol))
                tmp = tmp.Replace(trashA + symbol, symbol).Replace(trashB + symbol, symbol);
            return tmp;
        }

        public static string FormatLine(string var)
        {
            if (var.Length == 0) return "";
            while (var.Contains("\r"))
            {
                var = var.Replace("\r", "");
                if (var.Length == 0) return "";
            }
            while (var[0] == '\t' || var[0] == ' ')
            {
                var = var.Substring(1);
                if (var.Length == 0) return "";
            }

            string left, right;
            if (var.Contains("\'"))
            {
                left = var.Split('\'')[0];
                right = var.Substring(left.Length);
            }
            else if (var.Contains("\""))
            {
                left = var.Split('\"')[0];
                right = var.Substring(left.Length);
                if (right.Substring(1).Split('\"').Length > 1)
                {
                    if (right.Substring(1).Split('\"')[1].Contains("*"))
                    {
                        right = CleanSymbol(right, "*", "\t", " ");
                    }
                }
            }
            else
            {
                left = var;
                right = "";
            }
            while (left.Contains("\t"))
                left = left.Replace("\t", " ");
            while (left.Contains("  "))
                left = left.Replace("  ", " ");
            left = CleanSymbol(left, ",", " ");
            left = CleanSymbol(left, "=", " ");
            left = CleanSymbol(left, "{", "\t", " ");
            left = CleanSymbol(left, "}", "\t", " ");
            left = CleanSymbol(left, "(", "\t", " ");
            left = CleanSymbol(left, ")", "\t", " ");

            return left + right;
        }

        public static String FormatCode(String var)
        {
            String varBuf = ""; StringReader reader = new StringReader(var);
            while (reader.Peek() != -1)
            {
                varBuf = varBuf + FormatLine(reader.ReadLine()) + "\n";
            }
            while (varBuf.Contains("\n\n"))
            {
                varBuf = varBuf.Replace("\n\n", "\n");
            }
            reader.Dispose();
            return varBuf;
        }

        public static String RepairBrackets(String var, String left, String right)
        {
            while (var.Contains('\n' + left))
                var = var.Replace('\n' + left, left);
            var = var.Replace(left, left + '\n');
            var = var.Replace(right, '\n' + right);
            while (var.Contains("\n\n"))
                var = var.Replace("\n\n", "\n");
            while (var.Contains(left + " "))
                var = var.Replace(left + " ", left);
            while (var.Contains(" \n" + right))
                var = var.Replace(" \n" + right, "\n" + right);
            return var;
        }

        public static String EncodeLambda(String var)
        {
            return var.Replace("\n", "\f");
        }

        public static String DecodeLambda(String var)
        {
            return var.Replace("\f", "\n");
        }

        public static String FormatString(String var)
        {
            return var.Replace("\\\"", "\"").Replace("\\\'", "\'")
                    .Replace("\\\\", "\\").Replace("\\n", "\n")
                    .Replace("\\t", "\t");
        }

        public static String FormatLambda(String var)
        {
            const int IDLE = 0, RUN = 1, DONE = 2;
            int state = IDLE, count = 0, begin = 0, end = 0;

            for (int i = 0; i < var.Length; i++)
            {
                switch (state)
                {
                    case IDLE:
                        count = begin = end = 0;
                        if (var[i] == '(')
                        {
                            begin = i;
                            count += 1;
                            state = RUN;
                        }
                        break;
                    case RUN:
                        if (var[i] == '(')
                            count += 1;
                        else if (var[i] == ')')
                            count -= 1;
                        if (count == 0)
                        {
                            end = i;
                            state = DONE;
                        }
                        break;
                    case DONE:
                        String a, b, c;
                        a = var.Substring(0, begin);
                        b = var.Substring(begin, end - begin + 1);
                        c = var.Substring(end + 1);
                        b = EncodeLambda(b);
                        var = a + b + c;
                        state = IDLE;
                        break;
                    default:
                        return var;
                }
            }

            return var;
        }

        public static string[][] GetSegments(string var)
        {
            Dictionary<string, string> segBuf = new Dictionary<string, string>();
            LinkedList<string> pub = new LinkedList<string>();
            string varBuf = var;

            varBuf = FormatCode(varBuf);
            varBuf = RepairBrackets(varBuf, "{", "}");
            varBuf = RepairBrackets(varBuf, "(", ")");
            varBuf = FormatCode(varBuf);

            varBuf = FormatLambda(varBuf);

            StringReader reader = new StringReader(varBuf);

            string head, body = "", tmp;
            while (reader.Peek() != -1)
            {
                head = reader.ReadLine();
                if (!head.Contains("{"))
                {
                    pub.AddLast(head);
                    continue;
                }
                head = head.Replace("{", "");

                if (reader.Peek() != -1)
                {
                    tmp = reader.ReadLine();
                    while (!tmp.Contains("}") && reader.Peek() != -1)
                    {
                        body = body + (tmp + "\n");
                        tmp = reader.ReadLine();
                    }
                }

                segBuf.Add(head, body);
                body = "";
            }

            string[][] ret = new string[segBuf.Count + 1][];
            for (int i = 0; i < ret.Length; i++)
                ret[i] = new string[2];

            ret[0][0] = "_pub_" + var.GetHashCode().ToString("x");
            ret[0][1] = "";
            foreach (string i in pub)
            {
                ret[0][1] = ret[0][1] + (i + "\n");
            }

            string[] segKeys = new string[segBuf.Keys.Count];
            segBuf.Keys.CopyTo(segKeys, 0);
            for (int i = 0; i < segKeys.Length; i++)
            {
                ret[i + 1][0] = segKeys[i];
                ret[i + 1][1] = segBuf[ret[i + 1][0]];
            }

            return ret;
        }

        public static string GetSegment(string var, string head)
        {
            string[][] segments = GetSegments(var);
            string result = "";
            foreach (string[] i in segments)
            {
                if (i[0].Equals(head))
                {
                    if (result.Length == 0)
                        result = i[1];
                    else
                        return null;
                }
            }
            return result;
        }

        public static string Read(string path)
        {
            StringReader reader;
            try
            {
                reader = new StringReader(FileInput.Invoke(path));
            }
            catch (Exception e)
            {
                Print("File open failed.\n");
                Print("At file: " + path + "\n\n");
                return null;
            }

            string str = "";
            try
            {
                while (reader.Peek() != -1)
                    str = str + (reader.ReadLine() + "\n");
                reader.Dispose();
            }
            catch (Exception e)
            {
                Print("File read error.\n");
                Print("At file: " + path + "\n\n");
                return null;
            }
            return str;
        }

        public static void Run(string path)
        {
            string str = Read(path);
            if (str == null) return;

            int heap = 64, stack = 32, regs = 16;

            string conf = GetSegment(str, ".<conf>");
            if (conf == null)
            {
                Print("Conf load error.\n");
                Print("At file: " + path + "\n\n");
                return;
            }
            if (conf.Length > 0)
            {
                StringReader confReader = new StringReader(conf);
                try
                {
                    string buf;
                    while (confReader.Peek() != -1)
                    {
                        buf = confReader.ReadLine();
                        switch (buf.Split(' ')[0])
                        {
                            case "heap":
                                heap = int.Parse(buf.Split(' ')[1]);
                                break;
                            case "stack":
                                stack = int.Parse(buf.Split(' ')[1]);
                                break;
                            case "reg":
                                regs = int.Parse(buf.Split(' ')[1]);
                                break;
                        }
                    }
                }
                catch (Exception e)
                {
                    Print("Conf load error.\n");
                    Print("At file: " + path + "\n\n");
                    return;
                }
            }

            string[][] code = GetSegments(str);
            NSASM nsasm = new NSASM(heap, stack, regs, code);
            nsasm.Run();
            Print("\nNSASM running finished.\n\n");
        }

        public static void Execute(string str)
        {
            string path = "local";
            if (str == null) return;

            int heap = 64, stack = 32, regs = 16;

            string conf = GetSegment(str, ".<conf>");
            if (conf == null)
            {
                Print("Conf load error.\n");
                Print("At file: " + path + "\n\n");
                return;
            }
            if (conf.Length > 0)
            {
                StringReader confReader = new StringReader(conf);
                try
                {
                    string buf;
                    while (confReader.Peek() != -1)
                    {
                        buf = confReader.ReadLine();
                        switch (buf.Split(' ')[0])
                        {
                            case "heap":
                                heap = int.Parse(buf.Split(' ')[1]);
                                break;
                            case "stack":
                                stack = int.Parse(buf.Split(' ')[1]);
                                break;
                            case "reg":
                                regs = int.Parse(buf.Split(' ')[1]);
                                break;
                        }
                    }
                }
                catch (Exception e)
                {
                    Print("Conf load error.\n");
                    Print("At file: " + path + "\n\n");
                    return;
                }
            }

            string[][] code = GetSegments(str);
            NSASM nsasm = new NSASM(heap, stack, regs, code);
            nsasm.Run();
            Print("\nNSASM running finished.\n\n");
        }

        public static void Interactive()
        {
            Print("Now in console mode.\n\n");
            string buf;
            int lines = 1; NSASM.Result result;

            string[][] code = GetSegments("nop\n"); //ld func allowed
            NSASM nsasm = new NSASM(64, 32, 16, code);

            while (true)
            {
                Print(lines + " >>> ");
                buf = Scan();
                if (buf.Length == 0)
                {
                    lines += 1;
                    continue;
                }
                buf = FormatLine(buf);

                if (buf.Contains("#"))
                {
                    Print("<" + buf + ">\n");
                    continue;
                }
                result = nsasm.Execute(buf);
                if (result == NSASM.Result.ERR)
                {
                    Print("\nNSASM running error!\n");
                    Print("At line " + lines + ": " + buf + "\n\n");
                }
                else if (result == NSASM.Result.ETC)
                {
                    break;
                }
                if (buf.StartsWith("run") || buf.StartsWith("call"))
                {
                    nsasm.Run();
                }

                lines += 1;
            }
        }

    }
}
