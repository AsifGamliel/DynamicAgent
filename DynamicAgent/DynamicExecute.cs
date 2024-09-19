using System;
using System.Text.Json;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json.Serialization;

namespace DynamicAgent
{
    internal class DynamicExecute
    {
        public static object ExecuteMethod(string methodAsJson)
        {
            // Just JSON deserializer options
            JsonSerializerOptions serializerOptions = new JsonSerializerOptions()
            {
                MaxDepth = 30,
                AllowTrailingCommas = true,
                IncludeFields = true,
                UnknownTypeHandling = JsonUnknownTypeHandling.JsonElement,
            };

            // Deserilize the json string into a Method struct
            Method method = (Method)JsonSerializer.Deserialize(methodAsJson, typeof(Method), serializerOptions);

            // The values of 'method.ExecutionParameters' are of type 'JsonElement' because thats how the deserilizer works,
            // so in order to retrieve the original values we need to manually check their types and get them
            for (int i = 0; i < method.ExecutionParameters.Length; i++)
            {
                method.ExecutionParameters[i] = GetObjectFromJsonElement((JsonElement)method.ExecutionParameters[i]);
            }

            return ExecuteMethod(method);
        }

        // Turn the JSON string to a Method struct and parse it to a Dynamic Method - then execute it
        public static object ExecuteMethod(Method method)
        {
            // Declare the return type for the Dynamic Method
            Type returnType = Type.GetType(method.ReturnType);

            // Declare the parameters the Dynamic Method receives
            Type[] parameterTypes = new Type[method.ParameterTypes.Count];
            for (int i = 0; i < method.ParameterTypes.Count; i++)
            {
                parameterTypes[i] = Type.GetType(method.ParameterTypes[i]);
            }

            // Get the signature of the method's local variables
            SignatureHelper signatureHelper = SignatureHelper.GetLocalVarSigHelper();
            foreach (string varType in method.LocalVarTypes)
            {
                signatureHelper.AddArgument(Type.GetType(varType));
            }
            byte[] signature = signatureHelper.GetSignature();

            // Declaring the Dynamic Method and start bulding it
            DynamicMethod dynamicMethod = new DynamicMethod("", returnType, parameterTypes, true);
            DynamicILInfo dynamicILInfo = dynamicMethod.GetDynamicILInfo();
            dynamicILInfo.SetLocalSignature(signature);

            // Get the actual body of the method
            byte[] methodBody = Convert.FromBase64String(method.B64MethodBody);

            // Iterate the members used by the method. Those need to be specifically resolved
            // for the Dynamic Method so they can be used in it's scope
            foreach (InlineTokenInfo definition in method.InlineTokenInfos)
            {
                int tokenFor =  ResolveTokenForDynamicMethod(definition, ref dynamicILInfo);

                if (tokenFor <= 0)
                {
                    // Shit
                    throw new Exception("Failed to get token for: " + definition.FullName);
                }

                // Place the resolved token in it's index in the method body. (as little-endian,
                // cause that's how the tokens are aligned in the method's body
                for (int i = 0; i < 4; i++)
                {
                    methodBody[definition.Index + i] = (byte)(tokenFor >> (8 * i));
                }
            }

            // Set the code with all the resolved tokens and execute the Dynamic Method
            dynamicILInfo.SetCode(methodBody, method.MaxStackSize);
            return dynamicMethod.Invoke(null, method.ExecutionParameters);
        }

        private static int ResolveTokenForDynamicMethod(InlineTokenInfo tokenInfo, ref DynamicILInfo dynamicILInfo)
        {
            // This will be the token for the current member so it can be used
            // specifically in the scope of the Dynamic Method (that's why 'ref dynamicILInfo' is a parameter here,
            // we need to use it to get the token)
            int tokenForDynamicMethod;

            if (tokenInfo.MemberType == "RuntimeType")
            {
                // The current member is a 'Type'
                TypeInfo typeInfo = Type.GetType(tokenInfo.TypeName).GetTypeInfo();
                // Get it's token 
                tokenForDynamicMethod = dynamicILInfo.GetTokenFor(typeInfo.TypeHandle);
            }
            else if (tokenInfo.MemberType == "RtFieldInfo")
            {
                // The current member is a type's 'Field'
                FieldInfo fieldInfo = Type.GetType(tokenInfo.TypeName).GetField(tokenInfo.FullName);
                tokenForDynamicMethod = dynamicILInfo.GetTokenFor(fieldInfo.FieldHandle, ((TypeInfo)fieldInfo.DeclaringType).TypeHandle);
            }
            else if (tokenInfo.MemberType == "Constructor")
            {
                // The current member is a constructor method
                ConstructorInfo constructorInfo = GetConstructorInfo(tokenInfo);
                tokenForDynamicMethod = dynamicILInfo.GetTokenFor(constructorInfo.MethodHandle, ((TypeInfo)constructorInfo.DeclaringType).TypeHandle);
            }
            else if (tokenInfo.MemberType == "Method")
            {
                // The current member is a regular method
                MethodInfo methodInfo = GetMethodInfo(tokenInfo);
                tokenForDynamicMethod = dynamicILInfo.GetTokenFor(methodInfo.MethodHandle, ((TypeInfo)methodInfo.DeclaringType).TypeHandle);
            }
            else { throw new NotSupportedException($"Invalid member type: '{(string.IsNullOrWhiteSpace(tokenInfo.MemberType) ? "" : tokenInfo.MemberType)}'"); }

            return tokenForDynamicMethod;
        }

        private static ConstructorInfo GetConstructorInfo(InlineTokenInfo tokenInfo)
        {
            // There might be several constructors, and we need to get the specific constructor used in the original method,
            // so we iterate constructors of the type and try to find the exact one
            foreach (ConstructorInfo constructoInfo in Type.GetType(tokenInfo.TypeName).GetConstructors())
            {
                // Check if the constructor has the same declaration as the needed one.
                // 'GetMethodFullName' returns the constructor's name with the parameters it accepts,
                // and we check if it's equal to the constructor we need.
                // In addition, we check if both the constructor we need and current iterated constructor
                // are generic
                if (GetMethodFullName(constructoInfo) == tokenInfo.FullName &&
                    constructoInfo.IsGenericMethod == tokenInfo.IsGenericMethod &&
                    constructoInfo.IsGenericMethodDefinition == tokenInfo.IsGenericMethodDefinition &&
                    constructoInfo.ContainsGenericParameters == tokenInfo.ContainsGenericParameters)
                {
                    return constructoInfo;
                }
            }

            // Couldn't find the specific constructor we need :(
            throw new Exception("Failed to get ConstructorInfo for: " + tokenInfo.FullName);
        }

        private static MethodInfo GetMethodInfo(InlineTokenInfo tokenInfo)
        {
            // Iterate all methods in the method's type and try to find the exact one we need.
            // For some reason, 'Type.GetType("System.Linq.Enumerable"))' returnes null, so if the type of the method is 'System.Linq.Enumerable'
            // we get it's type directly, not with it's string name
            foreach (MethodInfo methodInfo in (tokenInfo.TypeName == "System.Linq.Enumerable" ? typeof(System.Linq.Enumerable) : Type.GetType(tokenInfo.TypeName)).GetMethods())
            {
                // Check if the current iterated method is the one we need, by comparing it's full name with the
                // desired method's full name. For additional validation, we also make sure it's the right method
                // by checking if both of the methods are generic.
                if (GetMethodFullName(methodInfo) == tokenInfo.FullName &&
                    methodInfo.IsGenericMethod == tokenInfo.IsGenericMethod &&
                    methodInfo.IsGenericMethodDefinition == tokenInfo.IsGenericMethodDefinition &&
                    methodInfo.ContainsGenericParameters == tokenInfo.ContainsGenericParameters)
                {
                    if (methodInfo.IsGenericMethod || methodInfo.IsGenericMethodDefinition || methodInfo.ContainsGenericParameters)
                    {
                        // If the method IS generic, we need to convert it to the wanted type used in the method.
                        // For example, let's say we need 'Join', to join a list of chars.
                        // If we return the method as 'Join[T]' we won't be able to use it!
                        // We'll have to convert it to 'Join[Char]' for it to work, so we take the desired parameter type(s)
                        // and convert the method before returning it
                        Type[] generics = new Type[tokenInfo.GenericParameters.Count];
                        for (int i = 0; i < tokenInfo.GenericParameters.Count; i++)
                        {
                            generics[i] = Type.GetType(tokenInfo.GenericParameters[i]);
                        }

                        return methodInfo.MakeGenericMethod(generics);
                    }
                    return methodInfo;
                }
            }

            throw new Exception("Failed to get MethodInfo for: " + tokenInfo.FullName);
        }

        private static object GetObjectFromJsonElement(JsonElement element)
        {
            // Get the value kind, then use the relevant method to get the actual value
            JsonValueKind kind = element.ValueKind;

            switch (kind)
            {
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                    return null;
                case JsonValueKind.Number:
                    switch (kind.GetTypeCode())
                    {
                        case TypeCode.Decimal:
                            return element.GetDecimal();
                        case TypeCode.Double:
                            return element.GetDouble();
                        case TypeCode.Int16:
                            return element.GetInt16();
                        case TypeCode.Int32:
                            return element.GetInt32();
                        case TypeCode.Int64:
                            return element.GetInt64();
                        case TypeCode.UInt16:
                            return element.GetUInt16();
                        case TypeCode.UInt32:
                            return element.GetUInt32();
                        case TypeCode.UInt64:
                            return element.GetUInt64();
                        case TypeCode.Single:
                            return element.GetSingle();
                        case TypeCode.Byte:
                            return element.GetByte();
                        case TypeCode.SByte:
                            return element.GetSByte();
                    }
                    break;
                case JsonValueKind vKind when (vKind == JsonValueKind.Object || vKind == JsonValueKind.Undefined):
                    switch (kind.GetTypeCode())
                    {
                        case TypeCode.Byte:
                            return element.GetByte();
                        case TypeCode.DateTime:
                            return element.GetDateTime();
                        case TypeCode.Single:
                            return element.GetSingle();
                        case TypeCode.SByte:
                            return element.GetSByte();
                    }
                    break;
                default:
                    return null;
            }

            return null;
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