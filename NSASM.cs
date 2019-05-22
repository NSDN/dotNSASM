using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;

namespace dotNSASM
{
    public class NSASM
    {
        public const string Version = "0.54 (.NET Standard 1.1)";

        public enum RegType
        {
            CHAR, STR, INT, FLOAT, CODE, MAP
        }

        public class Register
        {
            public RegType type;
            public object data;
            public int strPtr = 0;
            public bool readOnly;

            public override string ToString()
            {
                switch (type)
                {
                    case RegType.CODE:
                        return "(\n" + data.ToString() + "\n)";
                    default:
                        return data.ToString();
                }
            }

            public override bool Equals(object obj)
            {
                if (obj is Register)
                    return type.Equals(((Register)obj).type) && data.Equals(((Register)obj).data);
                return false;
            }

            public override int GetHashCode()
            {
                return data.GetHashCode();
            }

            public void Copy(Register reg)
            {
                type = reg.type;
                data = reg.data;
                strPtr = reg.strPtr;
                readOnly = reg.readOnly;
            }

            public Register() { }

            public Register(Register reg)
            {
                Copy(reg);
            }
        }

        public class Map : Dictionary<Register, Register>
        {
            public Map() : base() {}

            public override string ToString()
            {
                string str = "M(\n";
                foreach (Register key in Keys)
                {
                    str += (key.ToString() + "->" + this[key].ToString() + "\n");
                }
                str += ")";

                return str;
            }
        }

        public delegate Result Operator(Register dst, Register src, Register ext);

        private Dictionary<string, Register> heapManager;
        private Stack<Register> stackManager;
        private int heapSize, stackSize, regCnt;
        protected Register useReg;
        protected Register[] regGroup;
        private Register stateReg;
        private Register prevDstReg;

        private Stack<int> backupReg;
        private int progSeg, tmpSeg;
        private int progCnt, tmpCnt;

        protected Dictionary<string, Operator> funcList;
        private Dictionary<string, string[]> code;

        public enum Result
        {
            OK, ERR, ETC
        }

        private enum WordType
        {
            REG, CHAR, STR, INT,
            FLOAT, VAR, TAG, SEG,
            CODE, MAP
        }

        private bool VerifyBound(string var, char left, char right)
        {
            if (var.Length == 0) return false;
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
                case WordType.TAG:
                    return VerifyBound(var, '[', ']');
                case WordType.SEG:
                    return VerifyBound(var, '<', '>');
                case WordType.CODE:
                    return VerifyBound(var, '(', ')');
                case WordType.MAP:
                    if (var[0] == 'm' || var[0] == 'M')
                        return VerifyBound(var.Substring(1), '(', ')');
                    else return false;
                case WordType.VAR:
                    return !VerifyWord(var, WordType.REG) && !VerifyWord(var, WordType.CHAR) &&
                           !VerifyWord(var, WordType.STR) && !VerifyWord(var, WordType.INT) &&
                           !VerifyWord(var, WordType.FLOAT) && !VerifyWord(var, WordType.TAG) &&
                           !VerifyWord(var, WordType.SEG) && !VerifyWord(var, WordType.CODE) &&
                           !VerifyWord(var, WordType.MAP);
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
                            case '0': tmp = '\0'; break;
                            case 'b': tmp = '\b'; break;
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
                        if (var.Contains("*"))
                        {
                            tmp = rep = var.Split(new string[]{ "\"*" }, StringSplitOptions.RemoveEmptyEntries)[0].Substring(1);
                            Register repeat = GetRegister(var.Split(new string[] { "\"*" }, StringSplitOptions.RemoveEmptyEntries)[1]);
                            if (repeat == null) return null;
                            if (repeat.type != RegType.INT) return null;
                            for (int i = 1; i < (int)repeat.data; i++)
                                tmp = tmp + rep;
                        }
                        else
                        {
                            tmp = var.Substring(1, var.Length - 2);
                        }
                    }
                    catch (Exception e)
                    {
                        return null;
                    }

                    tmp = Util.FormatString(tmp);

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
                        if (
                            (var[0] < '0' || var[0] > '9') &&
                            (var[0] != '+' || var[0] != '-')
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
                else if (VerifyWord(var, WordType.TAG) || VerifyWord(var, WordType.SEG))
                {
                    register.type = RegType.STR;
                    register.readOnly = true;
                    register.data = var;
                }
                else if (VerifyWord(var, WordType.CODE))
                {
                    register.type = RegType.CODE;
                    register.readOnly = true;
                    String code = var.Substring(1, var.Length - 2);
                    code = Util.DecodeLambda(code);
                    register.data = code;
                }
                else if (VerifyWord(var, WordType.MAP))
                {
                    String code = var.Substring(2, var.Length - 3);

                    register = new Register();
                    register.type = RegType.MAP;
                    register.readOnly = true;
                    register.data = new Map();
                    code = Util.DecodeLambda(code);
                    funcList["mov"].Invoke(regGroup[regCnt], register, null);

                    Register reg = new Register();
                    reg.type = RegType.CODE; reg.readOnly = true;
                    reg.data = code + "\n" + "ret r" + regCnt + "\n";
                    register = Eval(reg);
                }
                else return null;
                return register;
            }
        }

        public Result Execute(string var)
        {
            string op, dst, src, ext;
            Register dr = null, sr = null, er = null;

            op = var.Split(' ')[0];
            op = op.ToLower(); //To lower case
            if (op.Length + 1 < var.Length)
            {
                if (
                        op.Equals("var") || op.Equals("int") ||
                        op.Equals("char") || op.Equals("float") ||
                        op.Equals("str") || op.Equals("code") ||
                        op.Equals("map")
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
                else if (op == "rem")
                {
                    //Comment
                    return Result.OK;
                }
                else
                {
                    //Normal code
                    string regs = var.Substring(op.Length + 1), res = "";
                    var strings = Util.GetStrings(regs, out res);
                    var args = Util.ParseArgs(res, ',');
                    for (int i = 0; i < args.Count; i++)
                        foreach (var it in strings)
                            args[i].Replace(it.Key, it.Value);

                    dst = src = ext = "";
                    if (args.Count > 0) dst = args[0];
                    if (args.Count > 1) src = args[1];
                    if (args.Count > 2) ext = args[2];

                    dr = GetRegister(dst);
                    sr = GetRegister(src);
                    er = GetRegister(ext);
                }
            }

            if (!funcList.ContainsKey(op))
                return VerifyWord(op, WordType.TAG) ? Result.OK : Result.ERR;

            prevDstReg = dr != null ? dr : prevDstReg;
            return funcList[op].Invoke(dr, sr, er);
        }

        public Register Run()
        {
            if (code == null) return null;
            Result result; string segBuf, codeBuf;

            progSeg = progCnt = 0;

            for (; progSeg < code.Keys.Count; progSeg++)
            {
                string[] codeKeys = new string[code.Keys.Count];
                code.Keys.CopyTo(codeKeys, 0);
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
                        return null;
                    }
                    else if (result == Result.ETC)
                    {
                        if (prevDstReg != null) prevDstReg.readOnly = false;
                        return prevDstReg;
                    }
                }

                if (backupReg.Count > 0)
                {
                    progCnt = backupReg.Pop() + 1;
                    progSeg = backupReg.Pop() - 1;
                }
                else progCnt = 0;
            }

            if (prevDstReg != null) prevDstReg.readOnly = false;
            return prevDstReg;
        }

        public void Call(string segName)
        {
            Result result; string segBuf, codeBuf;
            string[] codeKeys = new string[code.Keys.Count];

            code.Keys.CopyTo(codeKeys, 0);
            for (int seg = 0; seg < codeKeys.Length; seg++)
            {
                segBuf = codeKeys[seg];
                if (segName.Equals(segBuf))
                {
                    tmpSeg = seg;
                    tmpCnt = 0;
                    break;
                }
            }

            for (; progSeg < code.Keys.Count; progSeg++)
            {
                codeKeys = new string[code.Keys.Count];
                code.Keys.CopyTo(codeKeys, 0);
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

        protected virtual NSASM Instance(NSASM super, String[][] code)
        {
            return new NSASM(super, code);
        }

        protected Register Eval(Register register)
        {
            if (register == null) return null;
            if (register.type != RegType.CODE) return null;
            String[][] code = Util.GetSegments(register.data.ToString());
            return Instance(this, code).Run();
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
                if (seg[0].StartsWith("@")) //This is override seg
                {
                    if (!this.code.ContainsKey(seg[0].Substring(1)))
                    {
                        Util.Print("\nNSASM loading error!\n");
                        Util.Print("At " + seg[0].Substring(1) + "\n");
                        return Result.ERR;
                    }
                    this.code.Remove(seg[0].Substring(1));
                    this.code.Add(seg[0].Substring(1), ConvToArray(seg[1]));
                }
                else
                {
                    if (this.code.ContainsKey(seg[0]))
                    {
                        if (seg[0].StartsWith("_pub_")) continue; //This is pub seg
                        Util.Print("\nNSASM loading error!\n");
                        Util.Print("At " + seg[0] + "\n");
                        return Result.ERR;
                    }
                    this.code.Add(seg[0], ConvToArray(seg[1]));
                }
            }
            return Result.OK;
        }

        private void CopyRegGroup(NSASM super)
        {
            for (int i = 0; i < super.regGroup.Length; i++)
                this.regGroup[i].Copy(super.regGroup[i]);
        }

        private NSASM(NSASM super, String[][] code) : this(super.heapSize, super.stackSize, super.regCnt, code)
        {
            CopyRegGroup(super);
        }

        public NSASM(int heapSize, int stackSize, int regCnt, string[][] code)
        {
            heapManager = new Dictionary<string, Register>(heapSize);
            stackManager = new Stack<Register>();
            this.heapSize = heapSize;
            this.stackSize = stackSize;
            this.regCnt = regCnt;

            stateReg = new Register();
            stateReg.data = 0;
            stateReg.readOnly = false;
            stateReg.type = RegType.INT;

            backupReg = new Stack<int>();
            progSeg = 0; progCnt = 0;
            tmpSeg = -1; tmpCnt = -1;

            regGroup = new Register[regCnt + 1];
            for (int i = 0; i < regGroup.Length; i++)
            {
                regGroup[i] = new Register();
                regGroup[i].type = RegType.INT;
                regGroup[i].readOnly = false;
                regGroup[i].data = 0;
            }
            useReg = regGroup[regCnt];

            funcList = new Dictionary<string, Operator>();
            LoadFuncList();

            this.code = new Dictionary<string, string[]>();
            if (AppendCode(code) == Result.ERR)
            {
                Util.Print("At file: " + "_main_" + "\n\n");
                this.code.Clear();
            }
        }

        private Object ConvValue(Object value, RegType type)
        {
            switch (type)
            {
                case RegType.INT:
                    return int.Parse(value.ToString());
                case RegType.CHAR:
                    return (value.ToString())[0];
                case RegType.FLOAT:
                    return float.Parse(value.ToString());
            }
            return value;
        }

        private Result CalcInt(Register dst, Register src, char type)
        {
            switch (type)
            {
                case '+': dst.data = (int)ConvValue(dst.data, RegType.INT) + (int)ConvValue(src.data, RegType.INT); break;
                case '-': dst.data = (int)ConvValue(dst.data, RegType.INT) - (int)ConvValue(src.data, RegType.INT); break;
                case '*': dst.data = (int)ConvValue(dst.data, RegType.INT) * (int)ConvValue(src.data, RegType.INT); break;
                case '/': dst.data = (int)ConvValue(dst.data, RegType.INT) / (int)ConvValue(src.data, RegType.INT); break;
                case '%': dst.data = (int)ConvValue(dst.data, RegType.INT) % (int)ConvValue(src.data, RegType.INT); break;
                case '&': dst.data = (int)ConvValue(dst.data, RegType.INT) & (int)ConvValue(src.data, RegType.INT); break;
                case '|': dst.data = (int)ConvValue(dst.data, RegType.INT) | (int)ConvValue(src.data, RegType.INT); break;
                case '~': dst.data = ~(int)ConvValue(dst.data, RegType.INT); break;
                case '^': dst.data = (int)ConvValue(dst.data, RegType.INT) ^ (int)ConvValue(src.data, RegType.INT); break;
                case '<': dst.data = (int)ConvValue(dst.data, RegType.INT) << (int)ConvValue(src.data, RegType.INT); break;
                case '>': dst.data = (int)ConvValue(dst.data, RegType.INT) >> (int)ConvValue(src.data, RegType.INT); break;
                default: return Result.ERR;
            }
            return Result.OK;
        }

        private Result CalcChar(Register dst, Register src, char type)
        {
            switch (type)
            {
                case '+': dst.data = (char)ConvValue(dst.data, RegType.CHAR) + (char)ConvValue(src.data, RegType.CHAR); break;
                case '-': dst.data = (char)ConvValue(dst.data, RegType.CHAR) - (char)ConvValue(src.data, RegType.CHAR); break;
                case '*': dst.data = (char)ConvValue(dst.data, RegType.CHAR) * (char)ConvValue(src.data, RegType.CHAR); break;
                case '/': dst.data = (char)ConvValue(dst.data, RegType.CHAR) / (char)ConvValue(src.data, RegType.CHAR); break;
                case '%': dst.data = (char)ConvValue(dst.data, RegType.CHAR) % (char)ConvValue(src.data, RegType.CHAR); break;
                case '&': dst.data = (char)ConvValue(dst.data, RegType.CHAR) & (char)ConvValue(src.data, RegType.CHAR); break;
                case '|': dst.data = (char)ConvValue(dst.data, RegType.CHAR) | (char)ConvValue(src.data, RegType.CHAR); break;
                case '~': dst.data = ~(char)ConvValue(dst.data, RegType.CHAR); break;
                case '^': dst.data = (char)ConvValue(dst.data, RegType.CHAR) ^ (char)ConvValue(src.data, RegType.CHAR); break;
                case '<': dst.data = (char)ConvValue(dst.data, RegType.CHAR) << (char)ConvValue(src.data, RegType.CHAR); break;
                case '>': dst.data = (char)ConvValue(dst.data, RegType.CHAR) >> (char)ConvValue(src.data, RegType.CHAR); break;
                default: return Result.ERR;
            }
            return Result.OK;
        }

        private Result CalcFloat(Register dst, Register src, char type)
        {
            switch (type)
            {
                case '+': dst.data = (float)ConvValue(dst.data, RegType.FLOAT) + (float)ConvValue(src.data, RegType.FLOAT); break;
                case '-': dst.data = (float)ConvValue(dst.data, RegType.FLOAT) - (float)ConvValue(src.data, RegType.FLOAT); break;
                case '*': dst.data = (float)ConvValue(dst.data, RegType.FLOAT) * (float)ConvValue(src.data, RegType.FLOAT); break;
                case '/': dst.data = (float)ConvValue(dst.data, RegType.FLOAT) / (float)ConvValue(src.data, RegType.FLOAT); break;
                default: return Result.ERR;
            }
            return Result.OK;
        }

        private Result CalcStr(Register dst, Register src, char type)
        {
            switch (type)
            {
                case '+': dst.strPtr = dst.strPtr + (int)ConvValue(src.data, RegType.INT); break;
                case '-': dst.strPtr = dst.strPtr - (int)ConvValue(src.data, RegType.INT); break;
                default: return Result.ERR;
            }
            if (dst.strPtr >= dst.data.ToString().Length) dst.strPtr = dst.data.ToString().Length - 1;
            if (dst.strPtr < 0) dst.strPtr = 0;
            return Result.OK;
        }

        private Result Calc(Register dst, Register src, char type)
        {
            switch (dst.type)
            {
                case RegType.INT:
                    return CalcInt(dst, src, type);
                case RegType.CHAR:
                    return CalcChar(dst, src, type);
                case RegType.FLOAT:
                    return CalcFloat(dst, src, type);
                case RegType.STR:
                    return CalcStr(dst, src, type);
            }
            return Result.OK;
        }

        protected virtual void LoadFuncList()
        {
            funcList.Add("rem", (dst, src, ext) =>
            {
                return Result.OK;
            });

            funcList.Add("var", (dst, src, ext) =>
            {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.VAR)) return Result.ERR;
                if (heapManager.ContainsKey((string)dst.data)) return Result.ERR;
                if (src.type != RegType.STR) src.readOnly = false;
                heapManager.Add((string)dst.data, src);
                return Result.OK;
            });

            funcList.Add("int", (dst, src, ext) =>
            {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.VAR)) return Result.ERR;
                if (heapManager.ContainsKey((string)dst.data)) return Result.ERR;
                if (src.type != RegType.INT) return Result.ERR;

                src.readOnly = false;
                heapManager.Add((string)dst.data, src);
                return Result.OK;
            });

            funcList.Add("char", (dst, src, ext) =>
            {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.VAR)) return Result.ERR;
                if (heapManager.ContainsKey((string)dst.data)) return Result.ERR;
                if (src.type != RegType.CHAR) return Result.ERR;

                src.readOnly = false;
                heapManager.Add((string)dst.data, src);
                return Result.OK;
            });

            funcList.Add("float", (dst, src, ext) =>
            {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.VAR)) return Result.ERR;
                if (heapManager.ContainsKey((string)dst.data)) return Result.ERR;
                if (src.type != RegType.FLOAT) return Result.ERR;

                src.readOnly = false;
                heapManager.Add((string)dst.data, src);
                return Result.OK;
            });

            funcList.Add("str", (dst, src, ext) =>
            {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.VAR)) return Result.ERR;
                if (heapManager.ContainsKey((string)dst.data)) return Result.ERR;
                if (src.type != RegType.STR) return Result.ERR;

                src.readOnly = true;
                heapManager.Add((string)dst.data, src);
                return Result.OK;
            });

            funcList.Add("code", (dst, src, ext) =>
            {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.VAR)) return Result.ERR;
                if (heapManager.ContainsKey((string)dst.data)) return Result.ERR;
                if (src.type != RegType.CODE) return Result.ERR;

                src.readOnly = false;
                heapManager.Add((string)dst.data, src);
                return Result.OK;
            });

            funcList.Add("map", (dst, src, ext) =>
            {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.VAR)) return Result.ERR;
                if (heapManager.ContainsKey((string)dst.data)) return Result.ERR;
                if (src.type != RegType.MAP) return Result.ERR;

                src.readOnly = false;
                heapManager.Add((string)dst.data, src);
                return Result.OK;
            });

            funcList.Add("mov", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (dst.type == RegType.CHAR && src.type == RegType.STR)
                {
                    dst.data = ((string)src.data)[src.strPtr];
                }
                else if (dst.type == RegType.STR && src.type == RegType.CHAR)
                {
                    char[] array = ((string)dst.data).ToCharArray();
                    array[dst.strPtr] = (char)src.data;
                    dst.data = new string(array);
                }
                else
                {
                    dst.Copy(src);
                    if (dst.readOnly) dst.readOnly = false;
                }
                return Result.OK;
            });

            funcList.Add("push", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (stackManager.Count >= stackSize) return Result.ERR;
                stackManager.Push(dst);
                return Result.OK;
            });

            funcList.Add("pop", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                dst.Copy(stackManager.Pop());
                return Result.OK;
            });

            funcList.Add("in", (dst, src, ext) =>
            {
                if (src == null)
                {
                    src = new Register();
                    src.type = RegType.INT;
                    src.data = 0x00;
                    src.readOnly = true;
                }
                if (dst == null) return Result.ERR;
                if (src.type != RegType.INT) return Result.ERR;
                string buf; Register reg;
                switch ((int)src.data)
                {
                    case 0x00:
                        if (dst.readOnly && dst.type != RegType.STR) return Result.ERR;
                        buf = Util.Scan();
                        switch (dst.type)
                        {
                            case RegType.INT:
                                reg = GetRegister(buf);
                                if (reg == null) return Result.OK;
                                if (reg.type != RegType.INT) return Result.OK;
                                dst.data = reg.data;
                                break;
                            case RegType.CHAR:
                                if (buf.Length < 1) return Result.OK;
                                dst.data = buf[0];
                                break;
                            case RegType.FLOAT:
                                reg = GetRegister(buf);
                                if (reg == null) return Result.OK;
                                if (reg.type != RegType.FLOAT) return Result.OK;
                                dst.data = reg.data;
                                break;
                            case RegType.STR:
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
                            case RegType.INT:
                                reg = GetRegister(buf);
                                if (reg == null) return Result.OK;
                                if (reg.type != RegType.INT) return Result.OK;
                                dst.data = reg.data;
                                break;
                            case RegType.CHAR:
                                if (buf.Length < 1) return Result.OK;
                                dst.data = buf[0];
                                break;
                            case RegType.FLOAT:
                                reg = GetRegister(buf);
                                if (reg == null) return Result.OK;
                                if (reg.type != RegType.FLOAT) return Result.OK;
                                dst.data = reg.data;
                                break;
                            case RegType.STR:
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

            funcList.Add("out", (dst, src, ext) =>
            {
                if (dst == null) return Result.ERR;
                if (src == null)
                {
                    if (dst.type == RegType.STR)
                    {
                        Util.Print(((String)dst.data).Substring(dst.strPtr));
                    }
                    else if (dst.type == RegType.CODE)
                    {
                        Register register = Eval(dst);
                        if (register == null) return Result.ERR;
                        Util.Print(register.data);
                    }
                    else Util.Print(dst.data);
                }
                else
                {
                    if (dst.type != RegType.INT)
                        return Result.ERR;
                    switch ((int)dst.data)
                    {
                        case 0x00:
                            if (src.type == RegType.STR)
                            {
                                Util.Print(((String)src.data).Substring(src.strPtr));
                            }
                            else if (src.type == RegType.CODE)
                            {
                                Register register = Eval(src);
                                if (register == null) return Result.ERR;
                                Util.Print(register.data);
                            }
                            else Util.Print(src.data);
                            break;
                        case 0xFF:
                            Util.Print("[DEBUG] >>> ");
                            if (src.type == RegType.STR)
                            {
                                Util.Print(((String)src.data).Substring(src.strPtr));
                            }
                            else if (src.type == RegType.CODE)
                            {
                                Register register = Eval(src);
                                if (register == null) return Result.ERR;
                                Util.Print(register.data);
                            }
                            else Util.Print(src.data);
                            Util.Print('\n');
                            break;
                        default:
                            return Result.ERR;
                    }
                }
                return Result.OK;
            });

            funcList.Add("prt", (dst, src, ext) =>
            {
                if (dst == null) return Result.ERR;
                if (src != null)
                {
                    if (dst.type == RegType.STR)
                    {
                        if (dst.readOnly) return Result.ERR;
                        if (src.type == RegType.CHAR && src.data.Equals('\b'))
                        {
                            if (dst.data.ToString().Contains("\n"))
                            {
                                String[] parts = dst.data.ToString().Split('\n');
                                String res = "";
                                for (int i = 0; i < parts.Length - 1; i++)
                                {
                                    res = res + parts[i];
                                    if (i < parts.Length - 2) res = res + "\n";
                                }
                                dst.data = res;
                            }
                        }
                        else if (src.type == RegType.CODE)
                        {
                            Register register = Eval(src);
                            if (register == null) return Result.ERR;
                            dst.data = dst.data.ToString() + '\n' + register.data.ToString();
                        }
                        else if (src.type == RegType.STR)
                        {
                            dst.data = dst.data.ToString() + '\n' + src.data.ToString().Substring(src.strPtr);
                        }
                        else return Result.ERR;
                    }
                    else if (dst.type == RegType.CODE)
                    {
                        if (dst.readOnly) return Result.ERR;
                        if (src.type == RegType.CHAR && src.data.Equals('\b'))
                        {
                            if (dst.data.ToString().Contains("\n"))
                            {
                                String[] parts = dst.data.ToString().Split('\n');
                                String res = "";
                                for (int i = 0; i < parts.Length - 1; i++)
                                {
                                    res = res + parts[i];
                                    if (i < parts.Length - 2) res = res + "\n";
                                }
                                dst.data = res;
                            }
                        }
                        else if (src.type == RegType.CODE)
                        {
                            dst.data = dst.data.ToString() + '\n' + src.data.ToString();
                        }
                        else if (src.type == RegType.STR)
                        {
                            dst.data = dst.data.ToString() + '\n' + src.data.ToString().Substring(src.strPtr);
                        }
                        else return Result.ERR;
                    }
                    else return Result.ERR;
                }
                else
                {
                    if (dst == null) return Result.ERR;
                    if (dst.type == RegType.STR)
                    {
                        Util.Print(((String)dst.data).Substring(dst.strPtr) + '\n');
                    }
                    else if (dst.type == RegType.CODE)
                    {
                        Register register = Eval(dst);
                        if (register == null) return Result.ERR;
                        Util.Print(register.data.ToString() + '\n');
                    }
                    else Util.Print(dst.data.ToString() + '\n');
                }
                return Result.OK;
            });

            funcList.Add("add", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["add"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type == RegType.CODE)
                    return Calc(dst, Eval(src), '+');
                else
                    return Calc(dst, src, '+');
            });

            funcList.Add("inc", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                Register register = new Register();
                register.readOnly = false;
                register.type = RegType.CHAR;
                register.data = 1;
                return Calc(dst, register, '+');
            });

            funcList.Add("sub", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["sub"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type == RegType.CODE)
                    return Calc(dst, Eval(src), '-');
                else
                    return Calc(dst, src, '-');
            });

            funcList.Add("dec", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                Register register = new Register();
                register.readOnly = false;
                register.type = RegType.CHAR;
                register.data = 1;
                return Calc(dst, register, '-');
            });

            funcList.Add("mul", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mul"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type == RegType.CODE)
                    return Calc(dst, Eval(src), '*');
                else
                    return Calc(dst, src, '*');
            });

            funcList.Add("div", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["div"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type == RegType.CODE)
                    return Calc(dst, Eval(src), '/');
                else
                    return Calc(dst, src, '/');
            });

            funcList.Add("mod", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mod"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type == RegType.CODE)
                    return Calc(dst, Eval(src), '%');
                else
                    return Calc(dst, src, '%');
            });

            funcList.Add("and", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["and"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type == RegType.CODE)
                    return Calc(dst, Eval(src), '&');
                else
                    return Calc(dst, src, '&');
            });

            funcList.Add("or", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["or"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type == RegType.CODE)
                    return Calc(dst, Eval(src), '|');
                else
                    return Calc(dst, src, '|');
            });

            funcList.Add("xor", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["xor"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type == RegType.CODE)
                    return Calc(dst, Eval(src), '^');
                else
                    return Calc(dst, src, '^');
            });

            funcList.Add("not", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                return Calc(dst, null, '~');
            });

            funcList.Add("shl", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["shl"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type == RegType.CODE)
                    return Calc(dst, Eval(src), '<');
                else
                    return Calc(dst, src, '<');
            });

            funcList.Add("shr", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["shr"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type == RegType.CODE)
                    return Calc(dst, Eval(src), '>');
                else
                    return Calc(dst, src, '>');
            });

            funcList.Add("cmp", (dst, src, ext) =>
            {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (funcList["mov"].Invoke(stateReg, dst, null) == Result.ERR)
                    return Result.ERR;
                if (src.type == RegType.CODE)
                {
                    if (funcList["sub"].Invoke(stateReg, Eval(src), null) == Result.ERR)
                        return Result.ERR;
                }
                else
                {
                    if (funcList["sub"].Invoke(stateReg, src, null) == Result.ERR)
                        return Result.ERR;
                }
                return Result.OK;
            });

            funcList.Add("test", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.type == RegType.CODE)
                {
                    if (funcList["mov"].Invoke(stateReg, Eval(dst), null) == Result.ERR)
                        return Result.ERR;
                }
                else
                {
                    if (funcList["mov"].Invoke(stateReg, dst, null) == Result.ERR)
                        return Result.ERR;
                }

                Register reg = new Register();
                reg.type = dst.type; reg.readOnly = false; reg.data = 0;
                if (funcList["sub"].Invoke(stateReg, reg, null) == Result.ERR)
                    return Result.ERR;
                return Result.OK;
            });

            funcList.Add("jmp", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.type != RegType.STR) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.TAG)) return Result.ERR;
                string tag = (string)dst.data;
                string segBuf, lineBuf;

                string[] codeKeys = new string[code.Keys.Count];
                code.Keys.CopyTo(codeKeys, 0);

                for (int seg = 0; seg < codeKeys.Length; seg++)
                {
                    segBuf = codeKeys[seg];
                    if (code[segBuf] == null) continue;
                    for (int line = 0; line < code[segBuf].Length; line++)
                    {
                        lineBuf = code[segBuf][line];
                        if (tag.Equals(lineBuf))
                        {
                            tmpSeg = seg;
                            tmpCnt = line;
                            return Result.OK;
                        }
                    }
                }

                return Result.ERR;
            });

            funcList.Add("jz", (dst, src, ext) =>
            {
                if ((float)ConvValue(stateReg.data, RegType.FLOAT) == 0)
                {
                    return funcList["jmp"].Invoke(dst, src, null);
                }
                return Result.OK;
            });

            funcList.Add("jnz", (dst, src, ext) =>
            {
                if ((float)ConvValue(stateReg.data, RegType.FLOAT) != 0)
                {
                    return funcList["jmp"].Invoke(dst, src, null);
                }
                return Result.OK;
            });

            funcList.Add("jg", (dst, src, ext) =>
            {
                if ((float)ConvValue(stateReg.data, RegType.FLOAT) > 0)
                {
                    return funcList["jmp"].Invoke(dst, src, null);
                }
                return Result.OK;
            });

            funcList.Add("jl", (dst, src, ext) =>
            {
                if ((float)ConvValue(stateReg.data, RegType.FLOAT) < 0)
                {
                    return funcList["jmp"].Invoke(dst, src, null);
                }
                return Result.OK;
            });

            funcList.Add("loop", (dst, src, ext) =>
            {
                if (dst == null) return Result.ERR;
                if (src == null) return Result.ERR;
                if (ext == null) return Result.ERR;

                if (dst.type != RegType.INT) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type != RegType.INT) return Result.ERR;
                if (ext.type != RegType.STR) return Result.ERR;
                if (!VerifyWord((string)ext.data, WordType.TAG)) return Result.ERR;

                if ((int)src.data > 0)
                {
                    if (funcList["inc"].Invoke(dst, null, null) == Result.ERR)
                        return Result.ERR;
                }
                else
                {
                    if (funcList["dec"].Invoke(dst, null, null) == Result.ERR)
                        return Result.ERR;
                }
                if (funcList["cmp"].Invoke(dst, src, null) == Result.ERR)
                    return Result.ERR;
                if (funcList["jnz"].Invoke(ext, null, null) == Result.ERR)
                    return Result.ERR;

                return Result.OK;
            });

            funcList.Add("end", (dst, src, ext) =>
            {
                if (dst == null && src == null)
                    return Result.ETC;
                return Result.ERR;
            });

            funcList.Add("ret", (dst, src, ext) =>
            {
                if (src == null)
                {
                    if (dst != null) prevDstReg = dst;
                    else prevDstReg = regGroup[0];
                    return Result.ETC;
                }
                return Result.ERR;
            });

            funcList.Add("nop", (dst, src, ext) =>
            {
                if (dst == null && src == null)
                    return Result.OK;
                return Result.ERR;
            });

            funcList.Add("rst", (dst, src, ext) =>
            {
                if (dst == null && src == null)
                {
                    tmpSeg = 0;
                    tmpCnt = 0;
                    return Result.OK;
                }
                return Result.ERR;
            });

            funcList.Add("run", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.type != RegType.STR) return Result.ERR;
                if (!VerifyWord((String)dst.data, WordType.SEG)) return Result.ERR;
                string segBuf, target = (string)dst.data;
                string[] codeKeys = new string[code.Keys.Count];
                code.Keys.CopyTo(codeKeys, 0);
                for (int seg = 0; seg < codeKeys.Length; seg++)
                {
                    segBuf = codeKeys[seg];
                    if (target.Equals(segBuf))
                    {
                        tmpSeg = seg;
                        tmpCnt = 0;
                        return Result.OK;
                    }
                }
                return Result.ERR;
            });

            funcList.Add("call", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.type != RegType.STR) return Result.ERR;
                if (!VerifyWord((String)dst.data, WordType.SEG)) return Result.ERR;
                string segBuf, target = (string)dst.data;
                string[] codeKeys = new string[code.Keys.Count];
                code.Keys.CopyTo(codeKeys, 0);
                for (int seg = 0; seg < codeKeys.Length; seg++)
                {
                    segBuf = codeKeys[seg];
                    if (target.Equals(segBuf))
                    {
                        tmpSeg = seg;
                        tmpCnt = 0;
                        backupReg.Push(progSeg);
                        backupReg.Push(progCnt);
                        return Result.OK;
                    }
                }
                return Result.OK;
            });

            funcList.Add("ld", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.type != RegType.STR && dst.type != RegType.CODE)
                    return Result.ERR;

                string path;
                if (dst.type == RegType.CODE)
                {
                    Register res = Eval(dst);
                    if (res == null) return Result.ERR;
                    if (res.type != RegType.STR) return Result.ERR;
                    path = res.data.ToString();
                }
                else path = dst.data.ToString();

                string code = Util.Read(path);
                if (code == null) return Result.ERR;
                string[][] segs = Util.GetSegments(code);
                if (AppendCode(segs) == Result.ERR)
                {
                    Util.Print("At file: " + path + "\n");
                    return Result.ERR;
                }
                return Result.OK;
            });

            funcList.Add("eval", (dst, src, ext) => {
                if (dst == null) return Result.ERR;

                if (src == null) Eval(dst);
                else
                {
                    if (dst.readOnly) return Result.ERR;
                    dst.Copy(Eval(src));
                }

                return Result.OK;
            });

            funcList.Add("par", (dst, src, ext) =>
            {
                if (dst == null) return Result.ERR;
                if (src == null) return Result.ERR;
                if (ext == null) return Result.ERR;

                if (dst.type != RegType.MAP) return Result.ERR;
                if (src.type != RegType.CODE) return Result.ERR;
                if (ext.type != RegType.INT) return Result.ERR;

                Register reg = new Register(), count = new Register();
                count.type = RegType.INT; count.readOnly = false;
                for (int i = 0; i < (int)ext.data; i++)
                {
                    count.data = i;
                    if (funcList["eval"].Invoke(reg, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["put"].Invoke(dst, count, reg) == Result.ERR)
                        return Result.ERR;
                }

                return Result.OK;
            });

            funcList.Add("use", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (dst.type != RegType.MAP) return Result.ERR;
                useReg = dst;
                return Result.OK;
            });

            funcList.Add("put", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["use"].Invoke(dst, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["put"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (useReg == null) return Result.ERR;
                if (useReg.type != RegType.MAP) return Result.ERR;
                if (dst.type == RegType.CODE)
                {
                    Register reg = Eval(dst);
                    if (reg == null) return Result.ERR;
                    if (!(reg.data is Map)) return Result.ERR;
                    if (((Map)useReg.data).ContainsKey(reg))
                        ((Map)useReg.data).Remove(reg);
                    ((Map)useReg.data).Add(new Register(reg), new Register(src));
                }
                else
                {
                    if (((Map)useReg.data).ContainsKey(dst))
                        ((Map)useReg.data).Remove(dst);
                    ((Map)useReg.data).Add(new Register(dst), new Register(src));
                }

                return Result.OK;
            });

            funcList.Add("get", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["use"].Invoke(dst, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["get"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (useReg == null) return Result.ERR;
                if (useReg.type != RegType.MAP) return Result.ERR;

                if (src.type == RegType.CODE)
                {
                    Register reg = Eval(src);
                    if (reg == null) return Result.ERR;
                    if (!(reg.data is Map)) return Result.ERR;
                    if (!((Map)useReg.data).ContainsKey(reg)) return Result.ERR;
                    return funcList["mov"](dst, ((Map)useReg.data)[reg], null);
                }
                else
                {
                    if (!((Map)useReg.data).ContainsKey(src)) return Result.ERR;
                    return funcList["mov"](dst, ((Map)useReg.data)[src], null);
                }
            });

            funcList.Add("cat", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["cat"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                switch (dst.type)
                {
                    case RegType.STR:
                        if (src.type != RegType.STR)
                            return Result.ERR;
                        dst.data = (string)dst.data + (string)src.data;
                        break;
                    case RegType.MAP:
                        if (src.type != RegType.MAP)
                            return Result.ERR;
                        if (!(dst.data is Map)) return Result.ERR;
                        if (!(src.data is Map)) return Result.ERR;
                        foreach (var i in (Map)src.data)
                        {
                            if (((Map)dst.data).ContainsKey(i.Key))
                                ((Map)dst.data).Remove(i.Key);
                            ((Map)dst.data).Add(i.Key, i.Value);
                        }
                        break;
                    default:
                        return Result.ERR;
                }
                return Result.OK;
            });

            funcList.Add("dog", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["dog"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                switch (dst.type)
                {
                    case RegType.STR:
                        if (src.type != RegType.STR)
                            return Result.ERR;
                        dst.data = ((string)dst.data).Replace((string)src.data, "");
                        break;
                    case RegType.MAP:
                        if (src.type != RegType.MAP)
                            return Result.ERR;
                        foreach (var i in (Map)src.data)
                            if (((Map)dst.data).ContainsKey(i.Key))
                                ((Map)dst.data).Remove(i.Key);
                        break;
                    default:
                        return Result.ERR;
                }
                return Result.OK;
            });

            funcList.Add("type", (dst, src, ext) =>
            {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;

                Register reg = new Register();
                reg.type = RegType.STR;
                reg.readOnly = true;
                switch (src.type)
                {
                    case RegType.INT: reg.data = "int"; break;
                    case RegType.CHAR: reg.data = "char"; break;
                    case RegType.FLOAT: reg.data = "float"; break;
                    case RegType.STR: reg.data = "str"; break;
                    case RegType.CODE: reg.data = "code"; break;
                    case RegType.MAP: reg.data = "map"; break;
                }
                return funcList["mov"](dst, reg, null);
            });

            funcList.Add("len", (dst, src, ext) =>
            {
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                Register reg = new Register();
                reg.type = RegType.INT;
                reg.readOnly = true;
                if (src == null)
                {
                    if (useReg == null) return Result.ERR;
                    if (useReg.type != RegType.MAP) return Result.ERR;
                    if (!(useReg.data is Map)) return Result.ERR;
                    reg.data = ((Map)useReg.data).Count;
                }
                else
                {
                    if (src.type != RegType.STR) return Result.ERR;
                    reg.data = ((string)src.data).Length;
                }
                return funcList["mov"](dst, reg, null);
            });

            funcList.Add("ctn", (dst, src, ext) =>
            {
                if (dst == null) return Result.ERR;
                Register reg = new Register();
                reg.type = RegType.INT;
                reg.readOnly = true;
                if (src == null)
                {
                    if (useReg == null) return Result.ERR;
                    if (useReg.type != RegType.MAP) return Result.ERR;
                    if (!(useReg.data is Map)) return Result.ERR;
                    reg.data = ((Map)useReg.data).ContainsKey(dst) ? 1 : 0;
                }
                else
                {
                    if (src.type != RegType.STR) return Result.ERR;
                    if (dst.type != RegType.STR) return Result.ERR;
                    reg.data = ((string)dst.data).Contains((string)src.data) ? 1 : 0;
                }
                return funcList["mov"](stateReg, reg, null);
            });

            funcList.Add("equ", (dst, src, ext) =>
            {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (src.type != RegType.STR) return Result.ERR;
                if (dst.type != RegType.STR) return Result.ERR;
                Register reg = new Register();
                reg.type = RegType.INT;
                reg.readOnly = true;
                reg.data = ((string)dst.data).Equals((string)src.data) ? 0 : 1;
                return funcList["mov"](stateReg, reg, null);
            });
        }
    }
}
