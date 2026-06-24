using LuaAPI = XLua.LuaDLL.Lua;
using RealStatePtr = System.IntPtr;
using LuaCSFunction = XLua.LuaDLL.lua_CSFunction;

using System;
using System.Collections.Generic;
using System.Reflection;

namespace XLua
{
    public partial class InternalGlobals
    {
#if !THREAD_SAFE
        internal static byte[] strBuff = new byte[256];
#endif
#if !ENABLE_IL2CPP || !XLUA_IL2CPP
        public delegate bool TryArrayGet(Type type, RealStatePtr L, ObjectTranslator translator, object obj, int index);
        public delegate bool TryArraySet(Type type, RealStatePtr L, ObjectTranslator translator, object obj, int array_idx, int obj_idx);
        public static volatile TryArrayGet genTryArrayGetPtr = null;
        public static volatile TryArraySet genTryArraySetPtr = null;

        internal static volatile ObjectTranslatorPool objectTranslatorPool = new ObjectTranslatorPool();
#endif
        internal static volatile int LUA_REGISTRYINDEX = -10000;

        internal static volatile Dictionary<string, string> supportOp = new Dictionary<string, string>()
        {
            { "op_Addition", "__add" },
            { "op_Subtraction", "__sub" },
            { "op_Multiply", "__mul" },
            { "op_Division", "__div" },
            { "op_Equality", "__eq" },
            { "op_UnaryNegation", "__unm" },
            { "op_LessThan", "__lt" },
            { "op_LessThanOrEqual", "__le" },
            { "op_Modulus", "__mod" },
            { "op_BitwiseAnd", "__band" },
            { "op_BitwiseOr", "__bor" },
            { "op_ExclusiveOr", "__bxor" },
            { "op_OnesComplement", "__bnot" },
            { "op_LeftShift", "__shl" },
            { "op_RightShift", "__shr" },
        };

        public static volatile Dictionary<Type, List<MethodInfo>> extensionMethodMap = null;
    }
}
