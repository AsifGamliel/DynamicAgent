using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DynamicAgent
{
    // Represents a method, with all it's components, so we can 'save' a method
    // to build it manually later
    public struct Method
    {
        [JsonPropertyName("MaxStackSize")]
        public int MaxStackSize;
        [JsonPropertyName("ReturnType")]
        public string ReturnType;
        [JsonPropertyName("B64MethodBody")]
        public string B64MethodBody;
        [JsonPropertyName("LocalVarTypes")]
        public List<string> LocalVarTypes;
        [JsonPropertyName("ParameterTypes")]
        public List<string> ParameterTypes;
        [JsonPropertyName("ExecutionParameters")]
        public object[] ExecutionParameters;
        [JsonPropertyName("InlineTokenInfos")]
        public List<InlineTokenInfo> InlineTokenInfos;
    }

    // Info about a used member inside the method we want to save.
    // For the method to execute properly, it needs to resolve the correct members
    // it uses. This struct represents and holds info about such a member, so it can
    // be referenced correctly later when building the method
    public struct InlineTokenInfo
    {
        [JsonPropertyName("Index")]
        public int Index;
        [JsonPropertyName("FullName")]
        public string FullName;
        [JsonPropertyName("TypeName")]
        public string TypeName;
        [JsonPropertyName("MemberType")]
        public string MemberType;
        [JsonPropertyName("IsGenericMethod")]
        public bool IsGenericMethod;
        [JsonPropertyName("IsGenericMethodDefinition")]
        public bool IsGenericMethodDefinition;
        [JsonPropertyName("ContainsGenericParameters")]
        public bool ContainsGenericParameters;
        [JsonPropertyName("GenericParameters")]
        public List<string> GenericParameters;
    }
}
