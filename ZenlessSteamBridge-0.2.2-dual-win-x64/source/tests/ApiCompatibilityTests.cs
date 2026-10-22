using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace ZenlessSteamBridge
{
    // Inspect emitted calls, so a newer Mono runtime cannot conceal this Windows regression.
    internal static class ApiCompatibilityTests
    {
        internal static int Run(string executable)
        {
            var assembly = Assembly.LoadFile(System.IO.Path.GetFullPath(executable));
            var codes = typeof(OpCodes).GetFields(BindingFlags.Static | BindingFlags.Public)
                .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(c => c.Value);
            int calls = 0, unsupported = 0;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var type in assembly.GetTypes())
            {
                var methods = type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags).Cast<MethodBase>());
                foreach (var method in methods)
                {
                    var body = method.GetMethodBody(); if (body == null) continue;
                    byte[] il = body.GetILAsByteArray(); int pos = 0;
                    while (pos < il.Length)
                    {
                        short value = il[pos++];
                        if (value == 0xfe) value = unchecked((short)(0xfe00 | il[pos++]));
                        OpCode code = codes[value]; int size;
                        switch (code.OperandType)
                        {
                            case OperandType.InlineNone: size = 0; break;
                            case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; break;
                            case OperandType.InlineVar: size = 2; break;
                            case OperandType.InlineI8: case OperandType.InlineR: size = 8; break;
                            case OperandType.InlineSwitch: size = 4 + 4 * BitConverter.ToInt32(il, pos); break;
                            default: size = 4; break;
                        }
                        if (code.OperandType == OperandType.InlineMethod)
                        {
                            var called = method.Module.ResolveMethod(BitConverter.ToInt32(il, pos), type.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes);
                            if (called.DeclaringType == typeof(string) && called.Name == "Split")
                            {
                                calls++;
                                var parameters = called.GetParameters();
                                bool compatible = parameters.Length > 0 && parameters[0].ParameterType == typeof(char[]);
                                Console.WriteLine((compatible ? "PASS " : "FAIL ") + type.Name + "." + method.Name + " calls " + called);
                                if (!compatible) unsupported++;
                            }
                        }
                        pos += size;
                    }
                }
            }
            if (calls < 2 || unsupported != 0)
            {
                Console.Error.WriteLine("Framework compatibility regression: " + unsupported + " unsupported Split calls; " + calls + " total calls.");
                return 1;
            }
            Console.WriteLine("PASS all " + calls + " emitted String.Split calls use Framework-compatible Char[] overloads: " + executable);
            return 0;
        }
    }
}
