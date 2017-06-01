using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;

namespace dotNSASM
{
    public class NSASM
    {
        public const string version = "0.20 (Java)";

        protected enum RegType
        {
            CHAR, STR, INT, FLOAT
        }

        protected class Register
        {
            public RegType type;
            public Object data;
            public int strPtr = 0;
            public bool readOnly;

            public override string Tostring()
            {
                return "Type: " + type.Tostring() + "\n" +
                       "Data: " + data.Tostring() + "\n" +
                       "ReadOnly: " + readOnly;
            }

            public void Copy(Register reg)
            {
                type = reg.type;
                data = reg.data;
                strPtr = reg.strPtr;
                readOnly = reg.readOnly;
            }
        }

        protected delegate Result Operator(Register dst, Register src);

        private Dictionary<string, Register> heapManager;
        private Stack<Register> stackManager;
        private int stackSize;
        protected Register[] regGroup;
        private Register stateReg;

        private Stack<int> backupReg;
        private int progSeg, tmpSeg;
        private int progCnt, tmpCnt;

        protected Dictionary<string, Operator> funList;
        private Dictionary<string, string[]> code;

        public enum Result
        {
            OK, ERR, ETC
        }

        private enum WordType
        {
            REG, CHAR, STR, INT,
            FLOAT, VAR, TAG
        }

        private bool VerifyBound(string var, char left, char right)
        {
            return var[0] == left && var[var.Length - 1] == right;
        }

        private bool VerifyWord(string var, WordType type)
        {
            switch (type)
            {
                case WordType.REG:
                    return var[0] == 'r' || var[0] == 'R';
                case WordType.CHAR:
                    return VerifyBound(var, '\'', '\'');
                case WordType.STR:
                    return VerifyBound(var, '\"', '\"') ||
                           (var.Split('\"').Length > 2 && var.Contains("*"));
                case WordType.INT:
                    if (var.EndsWith("f") || var.EndsWith("F"))
                        return var.StartsWith("0x") || var.StartsWith("0X");
                    return (
                        !var.Contains(".")
                    ) && (
                        (var[0] >= '0' && var[0] <= '9') ||
                        var[0] == '-' || var[0] == '+' ||
                        var.EndsWith("h") || var.EndsWith("H")
                    );
                case WordType.FLOAT:
                    return (
                        var.Contains(".") ||
                        var.EndsWith("f") || var.EndsWith("F")
                    ) && (
                        (var[0] >= '0' && var[0] <= '9') ||
                        var[0] == '-' || var[0] == '+'
                    ) && (!var.StartsWith("0x") || !var.StartsWith("0X"));
                case WordType.VAR:
                    return !VerifyWord(var, WordType.REG) && !VerifyWord(var, WordType.CHAR) &&
                           !VerifyWord(var, WordType.STR) && !VerifyWord(var, WordType.INT) &&
                           !VerifyWord(var, WordType.FLOAT) && !VerifyWord(var, WordType.TAG);
                case WordType.TAG:
                    return VerifyBound(var, '[', ']') || VerifyBound(var, '<', '>');
            }
            return false;
        }

        private Register GetRegister(string var)
        {
            if (var.Length == 0) return null;
            if (VerifyWord(var, WordType.REG))
            {
                //Register
                int index = int.Parse(var.Substring(1));
                if (index < 0 || index >= regGroup.Length) return null;
                return regGroup[index];
            }
            else if (VerifyWord(var, WordType.VAR))
            {
                //Variable
                if (!heapManager.ContainsKey(var)) return null;
                return heapManager[var];
            }
            else
            {
                //Immediate number
                Register register = new Register();
                if (VerifyWord(var, WordType.CHAR))
                {
                    if (var.Length < 3) return null;
                    char tmp = (char)0;
                    if (var[1] == '\\')
                    {
                        if (var.Length < 4) return null;
                        switch (var[2])
                        {
                            case 'n': tmp = '\n'; break;
                            case 'r': tmp = '\r'; break;
                            case 't': tmp = '\t'; break;
                            case '\\': tmp = '\\'; break;
                        }
                    }
                    else
                    {
                        tmp = var[1];
                    }
                    register.type = RegType.CHAR;
                    register.readOnly = true;
                    register.data = tmp;
                }
                else if (VerifyWord(var, WordType.STR))
                {
                    if (var.Length < 3) return null;
                    string tmp, rep;
                    try
                    {
                        if (var.Split('\"').Length > 2)
                        {
                            tmp = rep = var.Split('\"')[1];
                            Register repeat = GetRegister(var.Split('\"')[2].Replace("*", ""));
                            if (repeat == null) return null;
                            if (repeat.type != RegType.INT) return null;
                            for (int i = 1; i < (int)repeat.data; i++)
                                tmp = tmp + rep;
                        }
                        else
                        {
                            tmp = var.Split('\"')[1];
                        }
                    }
                    catch (Exception e)
                    {
                        return null;
                    }
                    register.type = RegType.STR;
                    register.readOnly = true;
                    register.data = tmp;
                }
                else if (VerifyWord(var, WordType.INT))
                {
                    int tmp;
                    if (
                        (var.Contains("x") || var.Contains("X")) ^
                        (var.Contains("h") || var.Contains("H"))
                    )
                    {
                        if (
                            (var.Contains("x") || var.Contains("X")) &&
                            (var.Contains("h") || var.Contains("H"))
                        ) return null;
                        try
                        {
                            tmp = int.Parse(
                                    var.Replace("h", "").Replace("H", "")
                                       .Replace("x", "").Replace("X", ""),
                                NumberStyles.HexNumber);
                        }
                        catch (Exception e)
                        {
                            return null;
                        }
                    }
                    else
                    {
                        try
                        {
                            tmp = int.Parse(var);
                        }
                        catch (Exception e)
                        {
                            return null;
                        }
                    }
                    register.type = RegType.INT;
                    register.readOnly = true;
                    register.data = tmp;
                }
                else if (VerifyWord(var, WordType.FLOAT))
                {
                    float tmp;
                    try
                    {
                        tmp = float.Parse(var.Replace("f", "").Replace("F", ""));
                    }
                    catch (Exception e)
                    {
                        return null;
                    }
                    register.type = RegType.FLOAT;
                    register.readOnly = true;
                    register.data = tmp;
                }
                else if (VerifyWord(var, WordType.TAG))
                {
                    register.type = RegType.STR;
                    register.readOnly = true;
                    register.data = var;
                }
                else return null;
                return register;
            }
        }

        public Result Execute(string var)
        {
            string op, dst, src;
            Register dr = null, sr = null;

            op = var.Split(' ')[0];
            op = op.ToLower(); //To lower case
            if (op.Length + 1 < var.Length)
            {
                if (
                        op.Equals("var") || op.Equals("int") ||
                        op.Equals("char") || op.Equals("float") ||
                        op.Equals("str")
                    )
                { 
                    //Variable define
                    dst = var.Substring(op.Length + 1).Split('=')[0];
                    if (var.Length <= op.Length + 1 + dst.Length) return Result.ERR;
                    if (var[op.Length + 1 + dst.Length] == '=')
                        src = var.Substring(op.Length + 1 + dst.Length + 1);
                    else src = "";
                    dr = new Register();
                    dr.readOnly = true; dr.type = RegType.STR; dr.data = dst;
                    sr = GetRegister(src);
                }
                else
                { 
                    //Normal code
                    if (
                        VerifyWord(var.Substring(op.Length + 1), WordType.STR) ||
                        VerifyWord(var.Substring(op.Length + 1), WordType.CHAR)
                    )
                    {
                        dst = var.Substring(op.Length + 1);
                        src = "";
                    }
                    else
                    {
                        dst = var.Substring(op.Length + 1).Split(',')[0];
                        if (var.Length <= op.Length + 1 + dst.Length)
                            src = "";
                        else if (var[op.Length + 1 + dst.Length] == ',')
                            src = var.Substring(op.Length + 1 + dst.Length + 1);
                        else src = "";
                    }
                    dr = GetRegister(dst);
                    sr = GetRegister(src);
                }
            }

            if (!funList.ContainsKey(op))
                return VerifyWord(op, WordType.TAG) ? Result.OK : Result.ERR;

            return funList[op].Invoke(dr, sr);
        }

        public void Run()
        {
            if (code == null) return;
            Result result; string segBuf, codeBuf;

            progSeg = progCnt = 0;

            string[] codeKeys = new string[code.Keys.Count];
            code.Keys.CopyTo(codeKeys, 0);

            for (; progSeg < codeKeys.Length; progSeg++)
            {
                segBuf = codeKeys[progSeg];
                if (code[segBuf] == null) continue;

                for (; progCnt < code[segBuf].Length; progCnt++)
                {
                    if (tmpSeg >= 0 || tmpCnt >= 0)
                    {
                        progSeg = tmpSeg; progCnt = tmpCnt;
                        tmpSeg = -1; tmpCnt = -1;
                    }

                    segBuf = codeKeys[progSeg];
                    if (code[segBuf] == null) break;
                    codeBuf = code[segBuf][progCnt];

                    if (codeBuf.Length == 0)
                    {
                        continue;
                    }

                    result = Execute(codeBuf);
                    if (result == Result.ERR)
                    {
                        Util.Print("\nNSASM running error!\n");
                        Util.Print("At " + segBuf + ", line " + (progCnt + 1) + ": " + codeBuf + "\n\n");
                        return;
                    }
                    else if (result == Result.ETC)
                    {
                        return;
                    }
                }

                if (backupReg.Count > 0)
                {
                    progCnt = backupReg.Pop() + 1;
                    progSeg = backupReg.Pop() - 1;
                }
                else progCnt = 0;
            }
        }

        private string[] ConvToArray(string var)
        {
            StringReader reader = new StringReader(var);
            LinkedList<string> buf = new LinkedList<string>();

            while (reader.Peek() != -1)
            {
                buf.AddLast(reader.ReadLine());
            }

            if (buf.Count == 0) return null;

            string[] array = new string[buf.Count];
            buf.CopyTo(array, 0);

            reader.Dispose();
            return array;
        }

        private Result AppendCode(string[][] code)
        {
            if (code == null) return Result.OK;
            foreach (string[] seg in code)
            {
                if (seg[0].StartsWith(".")) continue; //This is conf seg
                if (this.code.ContainsKey(seg[0]))
                {
                    Util.Print("\nNSASM loading error!\n");
                    Util.Print("At " + seg[0] + "\n");
                    return Result.ERR;
                }
                this.code.Add(seg[0], ConvToArray(seg[1]));
            }
            return Result.OK;
        }

        public NSASM(int heapSize, int stackSize, int regCnt, string[][] code)
        {
            heapManager = new Dictionary<string, Register>(heapSize);
            stackManager = new Stack<Register>();
            this.stackSize = stackSize;

            stateReg = new Register();
            stateReg.data = 0;
            stateReg.readOnly = false;
            stateReg.type = RegType.INT;

            backupReg = new Stack<int>();
            progSeg = 0; progCnt = 0;
            tmpSeg = -1; tmpCnt = -1;

            regGroup = new Register[regCnt];
            for (int i = 0; i < regGroup.Length; i++)
            {
                regGroup[i] = new Register();
                regGroup[i].type = RegType.CHAR;
                regGroup[i].readOnly = false;
                regGroup[i].data = 0;
            }

            funList = new Dictionary<string, Operator>();
            loadFunList();

            this.code = new Dictionary<string, string[]>();
            if (AppendCode(code) == Result.ERR)
            {
                Util.Print("At file: " + "_main_" + "\n\n");
                this.code.Clear();
            }
        }

        private Object convValue(Object value, RegType type)
        {
            switch (type)
            {
                case INT:
                    return Integer.valueOf(value.tostring());
                case CHAR:
                    return (value.tostring())[0);
                case FLOAT:
                    return Float.valueOf(value.tostring());
            }
            return value;
        }

        private Result calcInt(Register dst, Register src, char type)
        {
            switch (type)
            {
                case '+': dst.data = (int)convValue(dst.data, RegType.INT) + (int)convValue(src.data, RegType.INT); break;
                case '-': dst.data = (int)convValue(dst.data, RegType.INT) - (int)convValue(src.data, RegType.INT); break;
                case '*': dst.data = (int)convValue(dst.data, RegType.INT) * (int)convValue(src.data, RegType.INT); break;
                case '/': dst.data = (int)convValue(dst.data, RegType.INT) / (int)convValue(src.data, RegType.INT); break;
                case '&': dst.data = (int)convValue(dst.data, RegType.INT) & (int)convValue(src.data, RegType.INT); break;
                case '|': dst.data = (int)convValue(dst.data, RegType.INT) | (int)convValue(src.data, RegType.INT); break;
                case '~': dst.data = ~(int)convValue(dst.data, RegType.INT); break;
                case '^': dst.data = (int)convValue(dst.data, RegType.INT) ^ (int)convValue(src.data, RegType.INT); break;
                case '<': dst.data = (int)convValue(dst.data, RegType.INT) << (int)convValue(src.data, RegType.INT); break;
                case '>': dst.data = (int)convValue(dst.data, RegType.INT) >> (int)convValue(src.data, RegType.INT); break;
                default: return Result.ERR;
            }
            return Result.OK;
        }

        private Result calcChar(Register dst, Register src, char type)
        {
            switch (type)
            {
                case '+': dst.data = (char)convValue(dst.data, RegType.CHAR) + (char)convValue(src.data, RegType.CHAR); break;
                case '-': dst.data = (char)convValue(dst.data, RegType.CHAR) - (char)convValue(src.data, RegType.CHAR); break;
                case '*': dst.data = (char)convValue(dst.data, RegType.CHAR) * (char)convValue(src.data, RegType.CHAR); break;
                case '/': dst.data = (char)convValue(dst.data, RegType.CHAR) / (char)convValue(src.data, RegType.CHAR); break;
                case '&': dst.data = (char)convValue(dst.data, RegType.CHAR) & (char)convValue(src.data, RegType.CHAR); break;
                case '|': dst.data = (char)convValue(dst.data, RegType.CHAR) | (char)convValue(src.data, RegType.CHAR); break;
                case '~': dst.data = ~(char)convValue(dst.data, RegType.CHAR); break;
                case '^': dst.data = (char)convValue(dst.data, RegType.CHAR) ^ (char)convValue(src.data, RegType.CHAR); break;
                case '<': dst.data = (char)convValue(dst.data, RegType.CHAR) << (char)convValue(src.data, RegType.CHAR); break;
                case '>': dst.data = (char)convValue(dst.data, RegType.CHAR) >> (char)convValue(src.data, RegType.CHAR); break;
                default: return Result.ERR;
            }
            return Result.OK;
        }

        private Result calcFloat(Register dst, Register src, char type)
        {
            switch (type)
            {
                case '+': dst.data = (float)convValue(dst.data, RegType.FLOAT) + (float)convValue(src.data, RegType.FLOAT); break;
                case '-': dst.data = (float)convValue(dst.data, RegType.FLOAT) - (float)convValue(src.data, RegType.FLOAT); break;
                case '*': dst.data = (float)convValue(dst.data, RegType.FLOAT) * (float)convValue(src.data, RegType.FLOAT); break;
                case '/': dst.data = (float)convValue(dst.data, RegType.FLOAT) / (float)convValue(src.data, RegType.FLOAT); break;
                default: return Result.ERR;
            }
            return Result.OK;
        }

        private Result calcStr(Register dst, Register src, char type)
        {
            switch (type)
            {
                case '+': dst.strPtr = dst.strPtr + (int)convValue(src.data, RegType.INT); break;
                case '-': dst.strPtr = dst.strPtr - (int)convValue(src.data, RegType.INT); break;
                default: return Result.ERR;
            }
            return Result.OK;
        }

        private Result calc(Register dst, Register src, char type)
        {
            switch (dst.type)
            {
                case INT:
                    return calcInt(dst, src, type);
                case CHAR:
                    return calcChar(dst, src, type);
                case FLOAT:
                    return calcFloat(dst, src, type);
                case STR:
                    return calcStr(dst, src, type);
            }
            return Result.OK;
        }

        protected void loadFunList()
        {
            funList.put("rem", (dst, src)-> {
                return Result.OK;
            });

            funList.put("var", (dst, src)-> {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.VAR)) return Result.ERR;
                if (heapManager.ContainsKey((string)dst.data)) return Result.ERR;
                if (src.type != RegType.STR) src.readOnly = false;
                heapManager.put((string)dst.data, src);
                return Result.OK;
            });

            funList.put("int", (dst, src)-> {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.VAR)) return Result.ERR;
                if (heapManager.ContainsKey((string)dst.data)) return Result.ERR;
                if (src.type != RegType.STR) src.readOnly = false;
                if (src.type != RegType.INT) return Result.ERR;
                heapManager.put((string)dst.data, src);
                return Result.OK;
            });

            funList.put("char", (dst, src)-> {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.VAR)) return Result.ERR;
                if (heapManager.ContainsKey((string)dst.data)) return Result.ERR;
                if (src.type != RegType.STR) src.readOnly = false;
                if (src.type != RegType.CHAR) return Result.ERR;
                heapManager.put((string)dst.data, src);
                return Result.OK;
            });

            funList.put("float", (dst, src)-> {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.VAR)) return Result.ERR;
                if (heapManager.ContainsKey((string)dst.data)) return Result.ERR;
                if (src.type != RegType.STR) src.readOnly = false;
                if (src.type != RegType.FLOAT) return Result.ERR;
                heapManager.put((string)dst.data, src);
                return Result.OK;
            });

            funList.put("str", (dst, src)-> {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.VAR)) return Result.ERR;
                if (heapManager.ContainsKey((string)dst.data)) return Result.ERR;
                if (src.type != RegType.STR) src.readOnly = false;
                if (src.type != RegType.STR) return Result.ERR;
                heapManager.put((string)dst.data, src);
                return Result.OK;
            });

            funList.put("mov", (dst, src)-> {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (dst.type == RegType.CHAR && src.type == RegType.STR)
                {
                    dst.data = ((string)src.data)[src.strPtr);
                }
                else if (dst.type == RegType.STR && src.type == RegType.CHAR)
                {
                    char[] array = ((string)dst.data).toCharArray();
                    array[dst.strPtr] = (char)src.data;
                    dst.data = new string(array);
                }
                else
                {
                    dst.copy(src);
                    if (dst.readOnly) dst.readOnly = false;
                }
                return Result.OK;
            });

            funList.put("push", (dst, src)-> {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (stackManager.size() >= stackSize) return Result.ERR;
                stackManager.push(dst);
                return Result.OK;
            });

            funList.put("pop", (dst, src)-> {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                dst.copy(stackManager.pop());
                return Result.OK;
            });

            funList.put("in", (dst, src)-> {
                if (src == null)
                {
                    src = new Register();
                    src.type = RegType.INT;
                    src.data = 0x00;
                    src.readOnly = true;
                }
                if (dst == null) return Result.ERR;
                string buf; Register reg;
                switch ((int)src.data)
                {
                    case 0x00:
                        if (dst.readOnly && dst.type != RegType.STR) return Result.ERR;
                        buf = Util.Scan();
                        switch (dst.type)
                        {
                            case INT:
                                reg = GetRegister(buf);
                                if (reg == null) return Result.OK;
                                if (reg.type != RegType.INT) return Result.OK;
                                dst.data = reg.data;
                                break;
                            case CHAR:
                                if (buf.Length < 1) return Result.OK;
                                dst.data = buf[0);
                                break;
                            case FLOAT:
                                reg = GetRegister(buf);
                                if (reg == null) return Result.OK;
                                if (reg.type != RegType.FLOAT) return Result.OK;
                                dst.data = reg.data;
                                break;
                            case STR:
                                if (buf.Length < 1) return Result.OK;
                                dst.data = buf;
                                dst.strPtr = 0;
                                break;
                        }
                        break;
                    case 0xFF:
                        Util.Print("[DEBUG] <<< ");
                        if (dst.readOnly && dst.type != RegType.STR) return Result.ERR;
                        buf = Util.Scan();
                        switch (dst.type)
                        {
                            case INT:
                                reg = GetRegister(buf);
                                if (reg == null) return Result.OK;
                                if (reg.type != RegType.INT) return Result.OK;
                                dst.data = reg.data;
                                break;
                            case CHAR:
                                if (buf.Length < 1) return Result.OK;
                                dst.data = buf[0);
                                break;
                            case FLOAT:
                                reg = GetRegister(buf);
                                if (reg == null) return Result.OK;
                                if (reg.type != RegType.FLOAT) return Result.OK;
                                dst.data = reg.data;
                                break;
                            case STR:
                                if (buf.Length < 1) return Result.OK;
                                dst.data = buf;
                                dst.strPtr = 0;
                                break;
                        }
                        break;
                    default:
                        return Result.ERR;
                }
                return Result.OK;
            });

            funList.put("out", (dst, src)-> {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                switch ((int)dst.data)
                {
                    case 0x00:
                        if (src.type == RegType.STR)
                        {
                            Util.Print(((string)src.data).Substring(src.strPtr));
                        }
                        else Util.print(src.data);
                        break;
                    case 0xFF:
                        Util.Print("[DEBUG] >>> ");
                        if (src.type == RegType.STR)
                        {
                            Util.Print(((string)src.data).Substring(src.strPtr));
                        }
                        else Util.print(src.data);
                        break;
                    default:
                        return Result.ERR;
                }
                return Result.OK;
            });

            funList.put("prt", (dst, src)-> {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.type == RegType.STR)
                {
                    Util.print(((string)dst.data).Substring(dst.strPtr) + '\n');
                }
                else Util.print(dst.data.tostring() + '\n');
                return Result.OK;
            });

            funList.put("add", (dst, src)-> {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                return calc(dst, src, '+');
            });

            funList.put("inc", (dst, src)-> {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                Register register = new Register();
                register.readOnly = false;
                register.type = RegType.CHAR;
                register.data = 1;
                return calc(dst, register, '+');
            });

            funList.put("sub", (dst, src)-> {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                return calc(dst, src, '-');
            });

            funList.put("dec", (dst, src)-> {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                Register register = new Register();
                register.readOnly = false;
                register.type = RegType.CHAR;
                register.data = 1;
                return calc(dst, register, '-');
            });

            funList.put("mul", (dst, src)-> {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                return calc(dst, src, '*');
            });

            funList.put("div", (dst, src)-> {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                return calc(dst, src, '/');
            });

            funList.put("and", (dst, src)-> {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                return calc(dst, src, '&');
            });

            funList.put("or", (dst, src)-> {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                return calc(dst, src, '|');
            });

            funList.put("xor", (dst, src)-> {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                return calc(dst, src, '^');
            });

            funList.put("not", (dst, src)-> {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                return calc(dst, null, '~');
            });

            funList.put("shl", (dst, src)-> {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                return calc(dst, src, '<');
            });

            funList.put("shr", (dst, src)-> {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                return calc(dst, src, '>');
            });

            funList.put("cmp", (dst, src)-> {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (funList.get("mov").run(stateReg, dst) == Result.ERR)
                    return Result.ERR;
                if (funList.get("sub").run(stateReg, src) == Result.ERR)
                    return Result.ERR;
                return Result.OK;
            });

            funList.put("jmp", (dst, src)-> {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.type != RegType.STR) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.TAG)) return Result.ERR;
                string tag = (string)dst.data;
                string segBuf, lineBuf;

                for (int seg = 0; seg < code.keySet().size(); seg++)
                {
                    segBuf = (string)(code.keySet().toArray())[seg];
                    if (code.get(segBuf) == null) continue;
                    for (int line = 0; line < code.get(segBuf).Length; line++)
                    {
                        lineBuf = code.get(segBuf)[line];
                        if (tag.equals(lineBuf))
                        {
                            tmpSeg = seg;
                            tmpCnt = line;
                            return Result.OK;
                        }
                    }
                }

                return Result.ERR;
            });

            funList.put("jz", (dst, src)-> {
                if ((float)convValue(stateReg.data, RegType.FLOAT) == 0)
                {
                    return funList.get("jmp").run(dst, src);
                }
                return Result.OK;
            });

            funList.put("jnz", (dst, src)-> {
                if ((float)convValue(stateReg.data, RegType.FLOAT) != 0)
                {
                    return funList.get("jmp").run(dst, src);
                }
                return Result.OK;
            });

            funList.put("jg", (dst, src)-> {
                if ((float)convValue(stateReg.data, RegType.FLOAT) > 0)
                {
                    return funList.get("jmp").run(dst, src);
                }
                return Result.OK;
            });

            funList.put("jl", (dst, src)-> {
                if ((float)convValue(stateReg.data, RegType.FLOAT) < 0)
                {
                    return funList.get("jmp").run(dst, src);
                }
                return Result.OK;
            });

            funList.put("end", (dst, src)-> {
                if (dst == null && src == null)
                    return Result.ETC;
                return Result.ERR;
            });

            funList.put("nop", (dst, src)-> {
                if (dst == null && src == null)
                    return Result.OK;
                return Result.ERR;
            });

            funList.put("rst", (dst, src)-> {
                if (dst == null && src == null)
                {
                    tmpSeg = 0;
                    tmpCnt = 0;
                    return Result.OK;
                }
                return Result.ERR;
            });

            funList.put("run", (dst, src)-> {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                string segBuf, target = (string)dst.data;
                for (int seg = 0; seg < code.keySet().size(); seg++)
                {
                    segBuf = (string)(code.keySet().toArray())[seg];
                    if (target.equals(segBuf))
                    {
                        tmpSeg = seg;
                        tmpCnt = 0;
                        return Result.OK;
                    }
                }
                return Result.ERR;
            });

            funList.put("call", (dst, src)-> {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                string segBuf, target = (string)dst.data;
                for (int seg = 0; seg < code.keySet().size(); seg++)
                {
                    segBuf = (string)(code.keySet().toArray())[seg];
                    if (target.equals(segBuf))
                    {
                        tmpSeg = seg;
                        tmpCnt = 0;
                        backupReg.push(progSeg);
                        backupReg.push(progCnt);
                        return Result.OK;
                    }
                }
                return Result.OK;
            });

            funList.put("ld", (dst, src)-> {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.type != RegType.STR) return Result.ERR;
                string path = (string)dst.data;
                string code = Util.read(path);
                if (code == null) return Result.ERR;
                string[][] segs = Util.getSegments(code);
                if (AppendCode(segs) == Result.ERR)
                {
                    Util.Print("At file: " + path + "\n");
                    return Result.ERR;
                }
                return Result.OK;
            });
        }
    }
}
