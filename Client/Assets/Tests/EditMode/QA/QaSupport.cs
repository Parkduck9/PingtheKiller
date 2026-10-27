using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace PingTheKiller.Client.QA.Tests
{
    // Helpers shared by the U4b QA tests: source-file access (code-read checks) and an IL reference scanner.
    internal static class QaPaths
    {
        // PTK_QA_ASSETS lets a headless runner point at Client/Assets; inside Unity Application.dataPath is used.
        public static string Assets()
        {
            string env = Environment.GetEnvironmentVariable("PTK_QA_ASSETS");
            return !string.IsNullOrEmpty(env) ? env : UnityEngine.Application.dataPath;
        }

        public static string ScriptsDir() => Path.Combine(Assets(), "Scripts");

        public static string ReadScript(string relative) => File.ReadAllText(Path.Combine(ScriptsDir(), relative));

        public static IEnumerable<string> AllScripts() => Directory.GetFiles(ScriptsDir(), "*.cs", SearchOption.AllDirectories);
    }

    internal static class QaSource
    {
        // Removes // and /* */ comments, keeping string and char literals intact.
        public static string StripComments(string source)
        {
            var output = new StringBuilder(source.Length);
            int i = 0;
            while (i < source.Length)
            {
                char c = source[i];
                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    output.Append(c); i++;
                    while (i < source.Length)
                    {
                        char d = source[i];
                        output.Append(d); i++;
                        if (d == '\\' && i < source.Length) { output.Append(source[i]); i++; continue; }
                        if (d == quote) break;
                    }
                    continue;
                }
                if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    while (i < source.Length && source[i] != '\n') i++;
                    continue;
                }
                if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) i++;
                    i += 2;
                    continue;
                }
                output.Append(c); i++;
            }
            return output.ToString();
        }

        // Text of the first method whose declaration contains the signature fragment, from its '{' to the matching '}'.
        public static string MethodText(string source, string signatureFragment)
        {
            int at = source.IndexOf(signatureFragment, StringComparison.Ordinal);
            if (at < 0) return null;
            int open = source.IndexOf('{', at);
            if (open < 0) return null;
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
            }
            return null;
        }

        // Every string literal in the (comment-free) source, without the quotes.
        public static List<string> StringLiterals(string stripped)
        {
            var found = new List<string>();
            int i = 0;
            while (i < stripped.Length)
            {
                if (stripped[i] != '"') { i++; continue; }
                var text = new StringBuilder();
                i++;
                while (i < stripped.Length && stripped[i] != '"')
                {
                    if (stripped[i] == '\\' && i + 1 < stripped.Length) { text.Append(stripped[i]); i++; }
                    text.Append(stripped[i]); i++;
                }
                i++;
                found.Add(text.ToString());
            }
            return found;
        }
    }

    // Walks the IL of every method/constructor of a type (and its nested types, which hold lambdas) and records the
    // declaring type of every method, field and type token it references.
    internal sealed class IlScan
    {
        private static readonly Dictionary<short, OpCode> Table = BuildTable();
        public readonly List<string> References = new List<string>();
        public readonly List<string> Violations = new List<string>();
        public int Bodies, Tokens, Unresolved;

        private static Dictionary<short, OpCode> BuildTable()
        {
            var map = new Dictionary<short, OpCode>();
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.GetValue(null) is OpCode code) map[code.Value] = code;
            return map;
        }

        public static bool Forbidden(Type type)
        {
            while (type != null && type.HasElementType) type = type.GetElementType();
            if (type == null) return false;
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
                foreach (Type argument in type.GetGenericArguments()) if (Forbidden(argument)) return true;
            string ns = type.Namespace ?? string.Empty;
            if (ns == "System.Net.Sockets") return true;
            string name = type.Name;
            return name == "TcpTransport" || name == "NetClient" || name == "TcpClient" || name == "TcpListener"
                || name == "Socket" || name == "UdpClient" || name == "NetworkStream";
        }

        public static IlScan Of(Type root)
        {
            var scan = new IlScan();
            scan.Visit(root);
            return scan;
        }

        public static IlScan Of(MethodBase root)
        {
            var scan = new IlScan();
            scan.Body(root.DeclaringType, root);
            return scan;
        }

        private void Visit(Type type)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (FieldInfo field in type.GetFields(all)) Note(type, "field " + field.Name, field.FieldType);
            foreach (ConstructorInfo constructor in type.GetConstructors(all)) Body(type, constructor);
            foreach (MethodInfo method in type.GetMethods(all))
            {
                Note(type, method.Name + " (return)", method.ReturnType);
                foreach (ParameterInfo parameter in method.GetParameters()) Note(type, method.Name + " (parameter)", parameter.ParameterType);
                Body(type, method);
            }
            foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) Visit(nested);
        }

        private void Note(Type owner, string where, Type referenced)
        {
            if (referenced == null) return;
            string label = owner.Name + "." + where + " -> " + referenced.FullName;
            References.Add(label);
            if (Forbidden(referenced)) Violations.Add(label);
        }

        private void Body(Type owner, MethodBase method)
        {
            MethodBody body;
            byte[] il;
            try { body = method.GetMethodBody(); il = body == null ? null : body.GetILAsByteArray(); }
            catch (Exception) { return; }
            if (il == null) return;
            Bodies++;
            foreach (LocalVariableInfo local in body.LocalVariables) Note(owner, method.Name + " (local)", local.LocalType);
            Module module = owner.Module;
            Type[] typeArguments = owner.IsGenericType ? owner.GetGenericArguments() : null;
            Type[] methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
            int i = 0;
            while (i < il.Length)
            {
                int first = il[i++];
                short key = first == 0xFE && i < il.Length ? unchecked((short)(0xFE00 | il[i++])) : (short)first;
                OpCode code;
                if (!Table.TryGetValue(key, out code)) return; // unknown opcode: stop rather than misread the stream
                switch (code.OperandType)
                {
                    case OperandType.InlineNone: break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: i += 1; break;
                    case OperandType.InlineVar: i += 2; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: i += 8; break;
                    case OperandType.InlineSwitch:
                        int count = BitConverter.ToInt32(il, i);
                        i += 4 + 4 * count; break;
                    case OperandType.InlineMethod:
                    case OperandType.InlineField:
                    case OperandType.InlineType:
                    case OperandType.InlineTok:
                        Resolve(owner, method, module, BitConverter.ToInt32(il, i), typeArguments, methodArguments);
                        i += 4; break;
                    default: i += 4; break;
                }
            }
        }

        private void Resolve(Type owner, MethodBase method, Module module, int token, Type[] typeArguments, Type[] methodArguments)
        {
            Tokens++;
            MemberInfo member;
            try { member = module.ResolveMember(token, typeArguments, methodArguments); }
            catch (Exception) { Unresolved++; return; }
            if (member == null) { Unresolved++; return; }
            Type referenced = member as Type ?? member.DeclaringType;
            string label = owner.Name + "." + method.Name + " -> " + (referenced == null ? "?" : referenced.FullName) + "::" + member.Name;
            References.Add(label);
            if (referenced != null && Forbidden(referenced)) Violations.Add(label);
            MethodInfo called = member as MethodInfo;
            if (called != null && Forbidden(called.ReturnType)) Violations.Add(label + " (returns " + called.ReturnType.FullName + ")");
        }
    }
}
