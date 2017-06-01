using System;
using System.Collections.Generic;

namespace dotNSASM
{
    class Util
    {
        public static void Print(Object value)
        {
            Console.Write(value);
        }

        public static String Scan()
        {
            return Console.ReadLine();
        }

        private static String cleanSymbol(String var, String symbol, String trash)
        {
            String tmp = var;
            while (tmp.Contains(symbol + trash))
                tmp = tmp.Replace(symbol + trash, symbol);
            while (tmp.Contains(trash + symbol))
                tmp = tmp.Replace(trash + symbol, symbol);
            return tmp;
        }

        private static String cleanSymbol(String var, String symbol, String trashA, String trashB)
        {
            String tmp = var;
            while (tmp.Contains(symbol + trashA) || tmp.Contains(symbol + trashB))
                tmp = tmp.Replace(symbol + trashA, symbol).Replace(symbol + trashB, symbol);
            while (tmp.Contains(trashA + symbol) || tmp.Contains(trashB + symbol))
                tmp = tmp.Replace(trashA + symbol, symbol).Replace(trashB + symbol, symbol);
            return tmp;
        }

        public static String formatCode(String var)
        {
            if (var.isEmpty()) return "";
            while (var.Contains("\r"))
            {
                var = var.Replace("\r", "");
                if (var.isEmpty()) return "";
            }
            while (var.charAt(0) == '\t' || var.charAt(0) == ' ')
            {
                var = var.substring(1);
                if (var.isEmpty()) return "";
            }

            String left, right;
            if (var.Contains("\'"))
            {
                left = var.split("\'")[0];
                right = var.substring(left.length());
            }
            else if (var.Contains("\""))
            {
                left = var.split("\"")[0];
                right = var.substring(left.length());
                if (right.substring(1).split("\"").length > 1)
                {
                    if (right.substring(1).split("\"")[1].Contains("*"))
                    {
                        right = cleanSymbol(right, "*", "\t", " ");
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
            left = cleanSymbol(left, ",", " ");
            left = cleanSymbol(left, "=", " ");
            left = cleanSymbol(left, "{", "\t", " ");
            left = cleanSymbol(left, "}", "\t", " ");

            return left + right;
        }

        public static String[][] getSegments(String var)
        {
            LinkedHashMap<String, String> segBuf = new LinkedHashMap<>();
            String varBuf = ""; Scanner scanner = new Scanner(var);
            LinkedList<String> pub = new LinkedList<>();

            while (scanner.hasNextLine())
            {
                varBuf = varBuf.concat(formatCode(scanner.nextLine()) + "\n");
            }
            while (varBuf.Contains("\n\n"))
            {
                varBuf = varBuf.Replace("\n\n", "\n");
            }
            scanner = new Scanner(varBuf);

            String head, body = "", tmp;
            while (scanner.hasNextLine())
            {
                head = scanner.nextLine();
                if (!head.Contains("{"))
                {
                    pub.add(head);
                    continue;
                }
                head = head.Replace("{", "");

                if (scanner.hasNextLine())
                {
                    tmp = scanner.nextLine();
                    while (!tmp.Contains("}") && scanner.hasNextLine())
                    {
                        body = body.concat(tmp + "\n");
                        tmp = scanner.nextLine();
                    }
                }

                segBuf.put(head, body);
                body = "";
            }

            String[][] out = new String[segBuf.size() + 1][2];

        out[0] [0] = "_pub_" + Integer.toHexString(Integer.signum(var.hashCode()) * var.hashCode());
        out[0] [1] = "";
        for (String i : pub) {
            out[0] [1] = out[0] [1].concat(i + "\n");
    }

        for (int i = 0; i<segBuf.keySet().size(); i++) {
            out[i + 1] [0] = (String) segBuf.keySet().toArray()[i];
            out[i + 1] [1] = segBuf.get(out[i + 1] [0]);
        }

        return out;
    }

    public static String getSegment(String var, String head)
{
    String[][] segments = getSegments(var);
    String result = "";
    for (String[] i : segments)
    {
        if (i[0].equals(head))
        {
            if (result.isEmpty())
                result = i[1];
            else
                return null;
        }

    }
    return result;
}

public static String read(String path)
{
    BufferedReader reader;
    try
    {
        reader = new BufferedReader(new FileReader(path));
    }
    catch (Exception e)
    {
        print("File open failed.\n");
        print("At file: " + path + "\n\n");
        return null;
    }

    String str = "";
    try
    {
        while (reader.ready())
            str = str.concat(reader.readLine() + "\n");
    }
    catch (Exception e)
    {
        print("File read error.\n");
        print("At file: " + path + "\n\n");
        return null;
    }
    return str;
}

public static void run(String path)
{
    String str = read(path);
    if (str == null) return;

    int heap = 64, stack = 32, regs = 16;

    String conf = getSegment(str, ".<conf>");
    if (conf == null)
    {
        print("Conf load error.\n");
        print("At file: " + path + "\n\n");
        return;
    }
    if (!conf.isEmpty())
    {
        Scanner confReader = new Scanner(conf);
        try
        {
            String buf;
            while (confReader.hasNextLine())
            {
                buf = confReader.nextLine();
                switch (buf.split(" ")[0])
                {
                    case "heap":
                        heap = Integer.valueOf(buf.split(" ")[1]);
                        break;
                    case "stack":
                        stack = Integer.valueOf(buf.split(" ")[1]);
                        break;
                    case "reg":
                        regs = Integer.valueOf(buf.split(" ")[1]);
                        break;
                }
            }
        }
        catch (Exception e)
        {
            print("Conf load error.\n");
            print("At file: " + path + "\n\n");
            return;
        }
    }

    String[][] code = getSegments(str);
    NSASM nsasm = new NSASM(heap, stack, regs, code);
    nsasm.run();
    print("\nNSASM running finished.\n\n");
}

public static void execute(String str)
{
    String path = "local";
    if (str == null) return;

    int heap = 64, stack = 32, regs = 16;

    String conf = getSegment(str, ".<conf>");
    if (conf == null)
    {
        print("Conf load error.\n");
        print("At file: " + path + "\n\n");
        return;
    }
    if (!conf.isEmpty())
    {
        Scanner confReader = new Scanner(conf);
        try
        {
            String buf;
            while (confReader.hasNextLine())
            {
                buf = confReader.nextLine();
                switch (buf.split(" ")[0])
                {
                    case "heap":
                        heap = Integer.valueOf(buf.split(" ")[1]);
                        break;
                    case "stack":
                        stack = Integer.valueOf(buf.split(" ")[1]);
                        break;
                    case "reg":
                        regs = Integer.valueOf(buf.split(" ")[1]);
                        break;
                }
            }
        }
        catch (Exception e)
        {
            print("Conf load error.\n");
            print("At file: " + path + "\n\n");
            return;
        }
    }

    String[][] code = getSegments(str);
    NSASM nsasm = new NSASM(heap, stack, regs, code);
    nsasm.run();
    print("\nNSASM running finished.\n\n");
}

public static void console()
{
    Util.print("Now in console mode.\n\n");
    String buf;
    int lines = 1; Result result;

    NSASM nsasm = new NSASM(64, 32, 16, null);
    Scanner scanner = new Scanner(System.in);

    while (true)
    {
        Util.print(lines + " >>> ");
        buf = scanner.nextLine();
        if (buf.length() == 0)
        {
            lines += 1;
            continue;
        }
        buf = formatCode(buf);

        if (buf.Contains("#"))
        {
            Util.print("<" + buf + ">\n");
            continue;
        }
        result = nsasm.execute(buf);
        if (result == Result.ERR)
        {
            Util.print("\nNSASM running error!\n");
            Util.print("At line " + lines + ": " + buf + "\n\n");
        }
        else if (result == Result.ETC)
        {
            break;
        }
        lines += 1;
    }
}

public static void gui()
{
    new Editor().show();
}
    }
}
