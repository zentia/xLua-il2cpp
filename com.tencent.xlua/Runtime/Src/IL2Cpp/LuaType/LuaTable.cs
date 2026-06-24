#if XLUA_IL2CPP && ENABLE_IL2CPP

using System;
using System.Runtime.CompilerServices;

namespace XLua
{
    [UnityEngine.Scripting.Preserve]
    public class LuaTable
    {
        IntPtr apis; // PObjectRefInfo first ptr
        IntPtr valueRef;
        int authCode;

        [MethodImpl(MethodImplOptions.InternalCall)]
        object GetLuaTableValueByString(IntPtr apis, string key, Type resultType)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        object GetLuaTableValueByUInt64(IntPtr apis, ulong index, Type resutlType)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        void SetLuaTableValueByString(IntPtr apis, string key, object value)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        void SetLuaTableValueByUInt64(IntPtr apis, ulong index, object value)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        uint GetLuaTableLength(IntPtr apis)
        {
            throw new NotImplementedException();
        }

        public T Get<T>(string key)
        {
            return (T)GetLuaTableValueByString(apis, key, typeof(T));
        }

        public void Get<T>(string key, out T value)
        {
            value = (T)GetLuaTableValueByString(apis, key, typeof(T));
        }

        public T Get<T>(int key)
        {
            return (T)GetLuaTableValueByUInt64(apis, (ulong)key, typeof(T));
        }

        public void Get<T>(int key, out T value)
        {
            value = (T)GetLuaTableValueByUInt64(apis, (ulong)key, typeof(T));
        }

        public T Get<T>(uint key)
        {
            return (T)GetLuaTableValueByUInt64(apis, (ulong)key, typeof(T));
        }

        public T Get<T>(ulong key)
        {
            return (T)GetLuaTableValueByUInt64(apis, key, typeof(T));
        }

        public void Set<T>(string key, T value)
        {
            SetLuaTableValueByString(apis, key, value);
        }

        public void Set<T>(int index, T value)
        {
            SetLuaTableValueByUInt64(apis, (ulong)index, value);
        }

        public void Set<T>(uint index, T value)
        {
            SetLuaTableValueByUInt64(apis, (ulong)index, value);
        }

        public void Set<T>(ulong index, T value)
        {
            SetLuaTableValueByUInt64(apis, index, value);
        }

        public uint Length
        {
            get
            {
                return GetLuaTableLength(apis);
            }
        }

        ~LuaTable()
        {
            if (LuaEnv.Instance != null)
            {
                lock(LuaEnv.Instance)
                {
                    if (LuaEnv.Instance != null && LuaEnv.Instance.authCode == authCode)
                    {
                        XLua.NativeAPI.AddPendingKillScriptObjects(apis, valueRef);
                    }
                }
            }
        }
    }
}

#endif
