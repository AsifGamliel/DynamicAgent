using System;
using System.Linq;
using System.Text.Json;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DynamicAgent
{
    internal class GenerateMethod
    {
        // Generate a method object of the method we want to 'save' and serialize it to a JSON string
        public static string GenerateMethodAsJSON(MethodInfo methodInfo, object[] parameters, bool prettyPrintJson = false)
        {
            // Just JSON deserializer options
            JsonSerializerOptions serializerOptions = new JsonSerializerOptions()
            {
                MaxDepth = 30,
                AllowTrailingCommas = true,
                IncludeFields = true,
                UnknownTypeHandling = JsonUnknownTypeHandling.JsonElement,
                WriteIndented = prettyPrintJson
            };

            // Generate the Method object from specified method
            Method method = Generate(methodInfo, parameters);

            // Serialize Method struct to string and return it
            return JsonSerializer.Serialize(method, typeof(Method), serializerOptions);
        }

        // Generate a method object of the method we want to 'save'
        public static Method Generate(MethodInfo methodInfo, object[] parameters)
        {
            // Initialize Method struct
            Method method = new Method();
            method.LocalVarTypes = new List<string>();
            method.ParameterTypes = new List<string>();
            method.InlineTokenInfos = new List<InlineTokenInfo>();
            method.ExecutionParameters = parameters;

            MethodBody methodBody = methodInfo.GetMethodBody();

            // Get values from the MethodInfo object and put them in the struct
            method.MaxStackSize = methodBody.MaxStackSize;
            method.ReturnType = methodInfo.ReturnType.FullName;
            method.B64MethodBody = Convert.ToBase64String(methodBody.GetILAsByteArray());

            foreach (ParameterInfo info in methodInfo.GetParameters())
            {
                method.ParameterTypes.Add(info.ParameterType.FullName);
            }
            foreach (LocalVariableInfo var in methodBody.LocalVariables)
            {
                method.LocalVarTypes.Add(var.LocalType.FullName);
            }

            // Iterate the method body, detect metadata token of members, and store this information in the struct
            GetTokensFromCIL(methodInfo.Module, ref method.InlineTokenInfos, methodBody.GetILAsByteArray());

            return method;
        }

        private static void GetTokensFromCIL(Module module, ref List<InlineTokenInfo> tokensList, byte[] cilBytes)
        {
            // List all the opcodes
            IEnumerable<OpCode> opCodes = typeof(OpCodes).GetFields().Select(opc => (OpCode)opc.GetValue(null));
            int counter = 0;

            // Iterate CIL method body
            while (counter < cilBytes.Length)
            {
                OpCode mappedOpCode;
                if (cilBytes[counter] == 0xFE)
                {
                    counter++;
                    mappedOpCode = opCodes.FirstOrDefault(opc => opc.Value == (-512 + cilBytes[counter]));
                }
                else
                {
                    // Get the opcode type for current byte
                    mappedOpCode = opCodes.FirstOrDefault(opc => opc.Value == cilBytes[counter]);
                }

                if (mappedOpCode.OperandType != OperandType.InlineNone)
                {
                    // Get the opernad used with current opcode.
                    // The size of the different operands is different, but most of them
                    // are 32-bit (4-bytes), so we'll start with this size as default
                    int byteCount = 4;
                    long token = 0;

                    switch (mappedOpCode.OperandType)
                    {
                        // These are all 32bit metadata tokens
                        case OperandType.InlineMethod:
                            break;
                        case OperandType.InlineField:
                            break;
                        case OperandType.InlineSig:
                            break;
                        case OperandType.InlineString:
                            break;
                        case OperandType.InlineType:
                            break;
                        // These are plain old 32bit operands
                        case OperandType.InlineI:
                        case OperandType.InlineBrTarget:
                        case OperandType.InlineSwitch:
                        case OperandType.ShortInlineR:
                            break;
                        // These are 64bit operands
                        case OperandType.InlineI8:
                        case OperandType.InlineR:
                            byteCount = 8;
                            break;
                        // These are all 8bit values
                        case OperandType.ShortInlineBrTarget:
                        case OperandType.ShortInlineI:
                        case OperandType.ShortInlineVar:
                            byteCount = 1;
                            break;
                    }

                    // Retreive the full operand
                    counter++;
                    for (int i = 0; i < byteCount; i++)
                    {
                        token |= ((long)cilBytes[counter + i]) << (8 * i);
                    }

                    // Try to resolve the operand and store it's data as a 'InlineTokenInfo' sub-structure
                    TryResolveToken(module, ref tokensList, (int)token, counter);

                    // Move on
                    counter += byteCount;
                }
                else
                {
                    counter++;
                }
            }
        }

        private static void TryResolveToken(Module module, ref List<InlineTokenInfo> tokensList, int token, int index)
        {
            if (token >= 0x70000000 && token < 0x7000FFFF)
            {
                // Strings, with their tokens, are defined in their original binary, so if we create a JSON from a method,
                // send it to execution on another machine (or even another program), we won't be able to use those strings
                throw new Exception("Method can't contain any strings");
            }

            MemberInfo memberInfo;

            try
            {
                memberInfo = module.ResolveMember(token, null, null);
            }
            catch
            {
                // Why is there a try-catch block?
                // Some operands are followed by 32-bit integers, or other values which has the size of a token, and will
                // get to this method for resolving as tokens. Because they're not really tokens, we will get an error trying
                // to resolve them, so we'll exit and leave them as they are.
                return;
            }

            // Initialize a 'InlineTokenInfo' struct and start set it's values by the current member's info
            InlineTokenInfo definition = new InlineTokenInfo();
            definition.IsGenericMethod = false;
            definition.IsGenericMethodDefinition = false;
            definition.ContainsGenericParameters = false;
            definition.GenericParameters = new List<string>();

            if (memberInfo.MemberType == MemberTypes.TypeInfo || memberInfo.MemberType == MemberTypes.NestedType)
            {
                definition.MemberType = "RuntimeType";
            }
            else if (memberInfo.MemberType == MemberTypes.Field)
            {
                definition.MemberType = "RtFieldInfo";
                definition.FullName = memberInfo.Name;
            }
            else if (memberInfo.MemberType == MemberTypes.Constructor)
            {
                definition.MemberType = "Constructor";
                definition.IsGenericMethod = ((ConstructorInfo)memberInfo).IsGenericMethod;
                definition.IsGenericMethodDefinition = ((ConstructorInfo)memberInfo).IsGenericMethod;
                definition.ContainsGenericParameters = ((ConstructorInfo)memberInfo).IsGenericMethod;
                definition.FullName = GetMethodFullName((ConstructorInfo)memberInfo);
            }
            else if (memberInfo.MemberType == MemberTypes.Method)
            {
                foreach (Type type in ((MethodInfo)memberInfo).GetGenericArguments())
                {
                    definition.GenericParameters.Add(type.FullName);
                }

                definition.MemberType = "Method";
                definition.IsGenericMethod = ((MethodInfo)memberInfo).IsGenericMethod;
                definition.IsGenericMethodDefinition = ((MethodInfo)memberInfo).IsGenericMethod;
                definition.ContainsGenericParameters = ((MethodInfo)memberInfo).IsGenericMethod;
                definition.FullName = GetMethodFullName((MethodInfo)memberInfo);
            }
            else
            {
                throw new Exception($"Invalid member type: '{memberInfo.MemberType}'");
            }

            definition.Index = index;
            definition.TypeName = (memberInfo.MemberType == MemberTypes.TypeInfo || memberInfo.MemberType == MemberTypes.NestedType) ?
                ((TypeInfo)memberInfo).FullName : memberInfo.DeclaringType.FullName;

            // Add member to list of members in our method
            tokensList.Add(definition);
        }

        private static string GetMethodFullName(MethodBase method)
        {
            ParameterInfo[] parameterInfos = method.GetParameters();
            string[] parameters = new string[parameterInfos.Length];

            // Get the parameter types as strings so we can put it in the method's full name
            for (int i = 0; i < parameterInfos.Length; i++)
            {
                parameters[i] = parameterInfos[i].ParameterType.Name;
            }

            // Concat method's properties to perform the full name. Full name example: 'System.IO.File.OpenRead(String)'
            return $"{method.DeclaringType.FullName}.{method.Name}({String.Join(",", parameters)})";
        }
    }
}
