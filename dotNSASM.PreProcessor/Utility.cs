using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace dotNSASM.PreProcessor
{
    class Utility
    {
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

        public static String FormatCode(String var)
        {
            String varBuf = Util.FormatCode(var);
            varBuf = CleanSymbol(varBuf, "<", "\t", " ");
            varBuf = CleanSymbol(varBuf, ">", "\t", " ");
            return varBuf;
        }

        public class DefBlock
        {
            public string name;
            public List<string> args;
            public string block;

            public DefBlock()
            {
                name = "";
                args = new List<string>();
                block = "";
            }

            public DefBlock(DefBlock defBlock)
            {
                name = defBlock.name;
                args = new List<string>(defBlock.args);
                block = defBlock.name;
            }

            public static DefBlock GetBlock(string head, string body)
            {
                if (!head.Contains("<") || !head.EndsWith(">"))
                    return null;

                DefBlock ret = new DefBlock();
                ret.name = head.Replace("def.", "").Split('<')[0];
                string arg = head.Split(new char[]{ '<', '>' })[1];
                foreach (string i in arg.Split(','))
                    ret.args.Add(i);
                ret.block = body;

                if (ret.block.Contains(ret.name)) // Self-call not allowed
                    return null;

                return ret;
            }
        }

        public static List<DefBlock> GetDefBlocks(string var)
        {
            List<DefBlock> blocks = new List<DefBlock>();
            string varBuf = var;

            varBuf = FormatCode(varBuf);
            varBuf = Util.RepairBrackets(varBuf, "{", "}");
            varBuf = Util.RepairBrackets(varBuf, "(", ")");
            varBuf = FormatCode(varBuf);

            varBuf = Util.FormatLambda(varBuf);

            StringReader reader = new StringReader(varBuf);

            String head = "", body = "", tmp; DefBlock blk;
            const int IDLE = 0, RUN = 1;
            int state = IDLE, count = 0;
            while (reader.Peek() != -1)
            {
                switch (state)
                {
                    case IDLE:
                        head = reader.ReadLine();
                        count = 0; body = "";
                        if (head.Contains("{"))
                        {
                            head = head.Replace("{", "");
                            count += 1;
                            state = RUN;
                        }
                        break;
                    case RUN:
                        if (reader.Peek() != -1)
                        {
                            tmp = reader.ReadLine();
                            if (tmp.Contains("{"))
                                count += 1;
                            else if (tmp.Contains("}"))
                                count -= 1;
                            if (tmp.Contains("(") && tmp.Contains(")"))
                            {
                                if (tmp.Contains("{") && tmp.Contains("}"))
                                    count -= 1;
                            }
                            if (count == 0)
                            {
                                if (head.StartsWith("def."))
                                {
                                    blk = DefBlock.GetBlock(head, body);
                                    if (blk == null)
                                    {
                                        Util.Print("Error at: \"" + head + "\"\n\n");
                                        return null;
                                    }
                                    blocks.Add(blk);
                                }
                                state = IDLE;
                            }
                            body = body + (tmp + "\n");
                        }
                        break;
                    default:
                        break;
                }
            }

            return blocks;
        }

        public class DefCall
        {
            public string name;
            public List<string> args;

            public DefCall()
            {
                name = "";
                args = new List<string>();
            }

            public DefCall(DefCall defCall)
            {
                name = defCall.name;
                args = new List<string>(defCall.args);
            }

            public static DefCall GetCall(string str)
            {
                DefCall ret = new DefCall();
                ret.name = str.Replace("def.", "").Split('<')[0];
                string arg = str.Split(new char[] { '<', '>' })[1];
                foreach (string i in arg.Split(','))
                    ret.args.Add(i);

                return ret;
            }
        }

        public static string DoPreProcess(List<DefBlock> blocks, string var)
        {
            string varBuf = var;

            varBuf = FormatCode(varBuf);
            varBuf = Util.RepairBrackets(varBuf, "{", "}");
            varBuf = Util.RepairBrackets(varBuf, "(", ")");
            varBuf = FormatCode(varBuf);

            varBuf = Util.FormatLambda(varBuf);

            StringReader reader = new StringReader(varBuf);
            StringBuilder builder = new StringBuilder();
            string line, block; DefCall call;
            builder.Clear();
            while (reader.Peek() != -1)
            {
                line = reader.ReadLine();
                if (line.Contains("<") && !line.StartsWith("<") && line.EndsWith(">") && !line.Contains(" "))
                {
                    call = DefCall.GetCall(line);
                    foreach (DefBlock blk in blocks)
                    {
                        if (blk.name == call.name)
                            if (blk.args.Count == call.args.Count)
                            {
                                block = blk.block;
                                for(int i = 0; i < call.args.Count; i++)
                                    block.Replace(blk.args[i], call.args[i]); // TODO: Use a proper method to replace
                                builder.AppendLine(block);
                                continue;
                            }
                    }
                    Util.Print("Error at: \"" + line + "\"\n\n");
                    return null;
                }
                else builder.AppendLine(line);
            }

            return var;
        }
    }
}
