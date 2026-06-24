#if ENABLE_IL2CPP && XLUA_IL2CPP
using System;
using System.Reflection;
using XLua.TypeMapping;
using System.Collections;
using System.Collections.Generic;
using RealStatePtr = System.IntPtr;
using System.Runtime.InteropServices;
using LuaAPI = XLua.LuaDLL.Lua;
using LuaCSFunction = XLua.LuaDLL.lua_CSFunction;
using UnityEngine;

namespace XLua
{
    [UnityEngine.Scripting.Preserve]
    public class LuaEnv : LuaEnvBase
    {
        private static bool isInitialized = false;
        private static MethodInfo extensionMethodGetMethodInfo;

        IntPtr nativePesapiEnv;
        IntPtr luaEnvPrivate;

        ObjectPool objectPool = new ObjectPool();

        [UnityEngine.Scripting.Preserve]
        private void Preserver()
        {
            var p1 = typeof(Type).GetNestedTypes();
        }

        public int errorFuncRef = -1;

        const int LIB_VERSION_EXPECT = 105;

        public LuaEnv(CustomLoader loader)
        {
            osgame_log.info(osgame_log.cat.Lua, "Native XLua Env");
            if (!isInitialized)
            {
                if (!isInitialized)
                {
                    //only once is enough

                    XLua.NativeAPI.InitialXLua(XLua.NativeAPI.GetRegisterApi());
                    extensionMethodGetMethodInfo = typeof(XLua.ExtensionMethodInfo).GetMethod("Get");

                    XLua.NativeAPI.SetExtensionMethodGet(extensionMethodGetMethodInfo);
                    NativeAPI.SetGlobalType_LuaTable(typeof(LuaTable));
                    NativeAPI.SetGlobalType_Array(typeof(Array));
                    NativeAPI.SetGlobalType_ArrayBuffer(typeof(byte[]));
                    NativeAPI.SetGlobalType_IntPtr(typeof(IntPtr));
                    NativeAPI.SetGlobalType_IEnumerable(typeof(IEnumerable));
                    NativeAPI.SetGlobalType_IDictionary(typeof(IDictionary));
                    NativeAPI.SetGlobalType_LuaException(typeof(LuaException));
                    NativeAPI.SetGlobalType_Object(typeof(UnityEngine.Object));
                    XLua.ExtensionMethodInfo.LoadExtensionMethodInfo();
                    isInitialized = true;
                }
            }
            osgame.common.UnityObjectDestroyEvent.onDestroyByLuaInvoke = OnDestroyByLuaInvoke;
            Init(loader);

            nativePesapiEnv = XLua.NativeAPI.GetPapiEnvRef(nativeLuaEnv);
            var objectPoolType = typeof(ObjectPool);
            luaEnvPrivate = NativeAPI.InitialPapiEnvRef(apis, nativePesapiEnv, objectPool, objectPoolType.GetMethod("Add"), objectPoolType.GetMethod("Remove"));
#if OSGAME
            var perfType = typeof(Assets.Plugins.Perf.StatsLite);
            NativeAPI.InitPerf(perfType.GetMethod("beginSample"), perfType.GetMethod("endSampleByIndex"));
#endif
            XLua.NativeAPI.SetObjectToGlobal(apis, nativePesapiEnv, "luaEnv", this);
            _G = (LuaTable)XLua.NativeAPI.GetGlobalTable(apis, nativePesapiEnv);

            DoString("require 'vm/init'");
        }

        private static void OnDestroyByLuaInvoke(UnityEngine.Object obj)
        {
            if (LuaEnv.Instance != null)
                NativeAPI.OnUnityObjectDestroyByLua(LuaEnv.Instance.rawL, obj, obj.name);
        }

        static IntPtr storeCallback = IntPtr.Zero;

        public T LoadString<T>(byte[] chunk, string chunkName = "chunk", LuaTable env = null)
        {
            return (T)XLua.NativeAPI.LoadString(apis, nativePesapiEnv, chunk, chunkName, env, typeof(T));
        }

        public T LoadString<T>(string chunk, string chunkName = "chunk", LuaTable env = null)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(chunk + '\0');
            return LoadString<T>(bytes, chunkName, env);
        }

        public T DoString<T>(byte[] chunk, string chunkName = "chunk", LuaTable env = null)
        {
            return (T)XLua.NativeAPI.DoString(apis, nativePesapiEnv, chunk, chunkName, env, typeof(T));
        }

        public T DoString<T>(string chunk, string chunkName = "chunk", LuaTable env = null)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(chunk);
            return DoString<T>(bytes, chunkName, env);
        }

        public void DoString(string chunk, string chunkName = "chunk", LuaTable env = null)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(chunk);
            XLua.NativeAPI.DoString(apis, nativePesapiEnv, bytes, chunkName, env, null);
        }

        public object DoString(string chunk, Type t, string chunkName = "chunk", LuaTable env = null)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(chunk);
            return XLua.NativeAPI.DoString(apis, nativePesapiEnv, bytes, chunkName, env, t);
        }

        public void Tick()
        {
            XLua.NativeAPI.CleanupPendingKillScriptObjects(luaEnvPrivate, apis);
        }

        public void GC()
        {
#if OSGAME
            Int32 sampleIndex = -1;
            Assets.Plugins.Perf.StatsLite.BeginSample(Assets.Plugins.Perf.StatsSampleId.LuaEnv_GC, ref sampleIndex);
#endif
            Tick();
#if OSGAME
            Assets.Plugins.Perf.StatsLite.EndSampleByIndex(ref sampleIndex);
#endif
        }

        public LuaTable NewTable()
        {
            return (LuaTable)XLua.NativeAPI.NewTable(apis, nativePesapiEnv);
        }

        private bool disposed = false;

        public override void Dispose()
        {
            Dispose(true);
            base.Dispose();
        }

        public virtual void Dispose(bool dispose)
        {
            lock (this)
            {
                if (disposed)
                    return;
                XLua.NativeAPI.DestroyLuaEnvPrivate(apis);
                XLua.NativeAPI.CleanupPapiEnvRef(apis, nativePesapiEnv);
                Instance = null;
                apis = IntPtr.Zero;
                nativePesapiEnv = IntPtr.Zero;
                luaEnvPrivate = IntPtr.Zero;
                disposed = true;
            }
        }

        [UnityEngine.Scripting.Preserve]
        public Type GetTypeByString(string className)
        {
            return XLua.TypeUtils.GetType(className);
        }
    }
}

#endif
