
/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

using LuaAPI = XLua.LuaDLL.Lua;
using RealStatePtr = System.IntPtr;
using LuaCSFunction = XLua.LuaDLL.lua_CSFunction;
using System;
using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

#if OSGAME
using Assets.Plugins.Perf;
#endif
namespace XLua
{
    class ReferenceEqualsComparer : IEqualityComparer<object>
    {
        public new bool Equals(object o1, object o2)
        {
            return object.ReferenceEquals(o1, o2);
        }
        public int GetHashCode(object obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }

#pragma warning disable 414
    public class MonoPInvokeCallbackAttribute : System.Attribute
    {
        private Type type;
        public MonoPInvokeCallbackAttribute(Type t) { type = t; }
    }
#pragma warning restore 414

    public enum LuaTypes
    {
        LUA_TNONE = -1,
        LUA_TNIL = 0,
        LUA_TNUMBER = 3,
        LUA_TSTRING = 4,
        LUA_TBOOLEAN = 1,
        LUA_TTABLE = 5,
        LUA_TFUNCTION = 6,
        LUA_TUSERDATA = 7,
        LUA_TTHREAD = 8,
        LUA_TLIGHTUSERDATA = 2
    }

    public enum LuaGCOptions
    {
        LUA_GCSTOP = 0,
        LUA_GCRESTART = 1,
        LUA_GCCOLLECT = 2,
        LUA_GCCOUNT = 3,
        LUA_GCCOUNTB = 4,
        LUA_GCSTEP = 5,
        LUA_GCSETPAUSE = 6,
        LUA_GCSETSTEPMUL = 7,
    }

    public enum LuaThreadStatus
    {
        LUA_RESUME_ERROR = -1,
        LUA_OK = 0,
        LUA_YIELD = 1,
        LUA_ERRRUN = 2,
        LUA_ERRSYNTAX = 3,
        LUA_ERRMEM = 4,
        LUA_ERRERR = 5,
    }

    public class LuaIndexes
    {
        public static int LUA_REGISTRYINDEX
        {
            get
            {
                return InternalGlobals.LUA_REGISTRYINDEX;
            }
            set
            {
                InternalGlobals.LUA_REGISTRYINDEX = value;
            }
        }
    }
#if !XLUA_IL2CPP || !ENABLE_IL2CPP
    public class ObjectTranslator
    {
        internal MethodWrapsCache methodWrapsCache;
        internal ObjectCheckers objectCheckers;
        public ObjectCasters objectCasters;

        internal readonly ObjectPool objects = new();
        public readonly Dictionary<object, int> reverseMap = new(new ReferenceEqualsComparer());
        internal LuaEnv luaEnv;
        internal StaticLuaCallbacks metaFunctions;
        internal List<Assembly> assemblies;
        private LuaCSFunction importTypeFunction, loadAssemblyFunction, castFunction;
        //延迟加载
        private readonly Dictionary<Type, Action<RealStatePtr>> delayWrap = new();

        private readonly Dictionary<Type, Func<int, LuaEnv, LuaBase>> interfaceBridgeCreators = new();

        //无法访问的类，比如声明成internal，可以用其接口、基类的生成代码来访问
        private readonly Dictionary<Type, Type> aliasCfg = new();

        public void DelayWrapLoader(Type type, Action<RealStatePtr> loader)
        {
            delayWrap[type] = loader;
        }

        public void AddInterfaceBridgeCreator(Type type, Func<int, LuaEnv, LuaBase> creator)
        {
            interfaceBridgeCreators.Add(type, creator);
        }

        Dictionary<Type, bool> loaded_types = new();
        public bool TryDelayWrapLoader(RealStatePtr L, Type type)
        {
            LuaAPI.lua_checkstack(L, 1);
            if (loaded_types.ContainsKey(type))
                return true;
            loaded_types.Add(type, true);

            LuaAPI.luaL_newmetatable(L, type.FullName); //先建一个metatable，因为加载过程可能会需要用到
            LuaAPI.lua_pop(L, 1);

            Action<RealStatePtr> loader;
            int top = LuaAPI.lua_gettop(L);
            if (delayWrap.TryGetValue(type, out loader))
            {
                delayWrap.Remove(type);
                loader(L);
            }
            else
            {
                Utils.ReflectionWrap(L, type);
            }
            if (top != LuaAPI.lua_gettop(L))
            {
                throw new Exception("top change, before:" + top + ", after:" + LuaAPI.lua_gettop(L));
            }

            foreach (var nested_type in type.GetNestedTypes(BindingFlags.Public))
            {
                if (nested_type.IsGenericTypeDefinition())
                {
                    continue;
                }
                GetTypeId(L, nested_type);
            }

            return true;
        }

        public void Alias(Type type, string alias)
        {
            Type alias_type = FindType(alias);
            if (alias_type == null)
            {
                throw new ArgumentException("Can not find " + alias);
            }
            aliasCfg[alias_type] = type;
        }

        public int cacheRef;

        void addAssemblieByName(IEnumerable<Assembly> assemblies_usorted, string name)
        {
            foreach (var assemblie in assemblies_usorted)
            {
                if (assemblie.FullName.StartsWith(name) && !assemblies.Contains(assemblie))
                {
                    assemblies.Add(assemblie);
                    break;
                }
            }
        }

        public ObjectTranslator(LuaEnv luaenv, RealStatePtr L, Type bridgeType)
        {
            delegate_birdge_type = bridgeType;
#if XLUA_GENERAL  || (UNITY_WSA && !UNITY_EDITOR)
            var dumb_field = typeof(ObjectTranslator).GetField("s_gen_reg_dumb_obj", BindingFlags.Static| BindingFlags.DeclaredOnly | BindingFlags.NonPublic);
            if (dumb_field != null)
            {
                dumb_field.GetValue(null);
            }
#endif
            assemblies = new List<Assembly>();

#if (UNITY_WSA && !ENABLE_IL2CPP) && !UNITY_EDITOR
            var assemblies_usorted = Utils.GetAssemblies();
#else
            assemblies.Add(Assembly.GetExecutingAssembly());
            var assemblies_usorted = AppDomain.CurrentDomain.GetAssemblies();
#endif
            addAssemblieByName(assemblies_usorted, "mscorlib,");
            addAssemblieByName(assemblies_usorted, "System,");
            addAssemblieByName(assemblies_usorted, "System.Core,");
            foreach (Assembly assembly in assemblies_usorted)
            {
                if (!assemblies.Contains(assembly))
                {
                    assemblies.Add(assembly);
                }
            }

            this.luaEnv = luaenv;
            objectCasters = new ObjectCasters(this);
            objectCheckers = new ObjectCheckers(this);
            methodWrapsCache = new MethodWrapsCache(this, objectCheckers, objectCasters);
            metaFunctions = new StaticLuaCallbacks();

            importTypeFunction = new LuaCSFunction(StaticLuaCallbacks.ImportType);
            loadAssemblyFunction = new LuaCSFunction(StaticLuaCallbacks.LoadAssembly);
            castFunction = new LuaCSFunction(StaticLuaCallbacks.Cast);

            LuaAPI.lua_newtable(L);
            LuaAPI.lua_newtable(L);
            LuaAPI.xlua_pushasciistring(L, "__mode");
            LuaAPI.xlua_pushasciistring(L, "v");
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.lua_setmetatable(L, -2);
            cacheRef = LuaAPI.luaL_ref(L, LuaIndexes.LUA_REGISTRYINDEX);

            initCSharpCallLua();
        }

        public enum LOGLEVEL
        {
            NO,
            INFO,
            WARN,
            ERROR
        }
        Type delegate_birdge_type;

#if UNITY_EDITOR && !NET_STANDARD_2_0
        class CompareByArgRet : IEqualityComparer<MethodInfo>
        {
            public bool Equals(MethodInfo x, MethodInfo y)
            {
                return Utils.IsParamsMatch(x, y);
            }
            public int GetHashCode(MethodInfo method)
            {
                int hc = 0;
                hc += method.ReturnType.GetHashCode();
                foreach (var pi in method.GetParameters())
                {
                    hc += pi.ParameterType.GetHashCode();
                }
                return hc;
            }
        }
#endif

        void initCSharpCallLua()
        {
#if (UNITY_EDITOR || XLUA_GENERAL) && !NET_STANDARD_2_0
            delegate_birdge_type = typeof(DelegateBridge);
            if (!DelegateBridge.Gen_Flag)
            {
                List<Type> cs_call_lua = new List<Type>();
                foreach (var type in Utils.GetAllTypes())
                {
                    if (type.IsDefined(typeof(CSharpCallLuaAttribute), false))
                    {
                        cs_call_lua.Add(type);
                    }

                    if (!type.IsAbstract || !type.IsSealed) continue;

                    var fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    for (int i = 0; i < fields.Length; i++)
                    {
                        var field = fields[i];
                        if (field.IsDefined(typeof(CSharpCallLuaAttribute), false))//
                        {
                            if ((typeof(IEnumerable<Type>)).IsAssignableFrom(field.FieldType))
                            {
                                cs_call_lua.AddRange(field.GetValue(null) as IEnumerable<Type>);
                            }
                            else
                            {
                                UnityEngine.Debug.LogError($"CSharpCallLuaAttribute: invalid_fields: {field}");
                            }
                        }
                    }

                    var props = type.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    for (int i = 0; i < props.Length; i++)
                    {
                        var prop = props[i];
                        if (prop.IsDefined(typeof(CSharpCallLuaAttribute), false))
                        {

                            if ((typeof(IEnumerable<Type>)).IsAssignableFrom(prop.PropertyType))
                            {
                                cs_call_lua.AddRange(prop.GetValue(null, null) as IEnumerable<Type>);
                            }
                            else
                            {
                                UnityEngine.Debug.LogError($"CSharpCallLuaAttribute: invalid_Properties: {prop}");
                            }
                        }
                    }
                }
                IEnumerable<IGrouping<MethodInfo, Type>> groups = (from type in cs_call_lua
                                                                   where typeof(Delegate).IsAssignableFrom(type) && type != typeof(Delegate) && type != typeof(MulticastDelegate)
                                                                   where !type.GetMethod("Invoke").GetParameters().Any(paramInfo => paramInfo.ParameterType.IsGenericParameter)
                                                                   select type).GroupBy(t => t.GetMethod("Invoke"), new CompareByArgRet());

                ce.SetGenInterfaces(cs_call_lua.Where(type => type.IsInterface()).ToList());
                delegate_birdge_type = ce.EmitDelegateImpl(groups);
            }
#endif
        }

#if (UNITY_EDITOR || XLUA_GENERAL) && !NET_STANDARD_2_0
        CodeEmit ce = new();
#endif
        MethodInfo[] genericAction = null;
        MethodInfo[] genericFunc = null;
        Dictionary<Type, Func<DelegateBridgeBase, Delegate>> delegateCreatorCache = new();

        Func<DelegateBridgeBase, Delegate> getCreatorUsingGeneric(DelegateBridgeBase bridge, Type delegateType, MethodInfo delegateMethod)
        {
            Func<DelegateBridgeBase, Delegate> genericDelegateCreator = null;

            if (genericAction == null)
            {
                var methods = typeof(DelegateBridge).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                genericAction = methods.Where(m => m.Name == "Action").OrderBy(m => m.GetParameters().Length).ToArray();
                genericFunc = methods.Where(m => m.Name == "Func").OrderBy(m => m.GetParameters().Length).ToArray();
            }
            if (genericAction.Length != 5 || genericFunc.Length != 5)
            {
                return null;
            }
            var parameters = delegateMethod.GetParameters();
            if ((delegateMethod.ReturnType.IsValueType() && delegateMethod.ReturnType != typeof(void)) || parameters.Length > 4)
            {
                genericDelegateCreator = (x) => null;
            }
            else
            {
                foreach (var pinfo in parameters)
                {
                    if (pinfo.ParameterType.IsValueType() || pinfo.IsOut || pinfo.ParameterType.IsByRef)
                    {
                        genericDelegateCreator = (x) => null;
                        break;
                    }
                }
                if (genericDelegateCreator == null)
                {
                    var typeArgs = parameters.Select(pinfo => pinfo.ParameterType);
                    MethodInfo genericMethodInfo = null;
                    if (delegateMethod.ReturnType == typeof(void))
                    {
                        genericMethodInfo = genericAction[parameters.Length];
                    }
                    else
                    {
                        genericMethodInfo = genericFunc[parameters.Length];
                        typeArgs = typeArgs.Concat(new Type[] { delegateMethod.ReturnType });
                    }
                    if (genericMethodInfo.IsGenericMethodDefinition)
                    {
                        var methodInfo = genericMethodInfo.MakeGenericMethod(typeArgs.ToArray());
                        genericDelegateCreator = (o) =>
#if !UNITY_WSA || UNITY_EDITOR
                            Delegate.CreateDelegate(delegateType, o, methodInfo);
#else
                            methodInfo.CreateDelegate(delegateType, bridge);
#endif
                    }
                    else
                    {
                        genericDelegateCreator = (o) =>
#if !UNITY_WSA || UNITY_EDITOR
                            Delegate.CreateDelegate(delegateType, o, genericMethodInfo);
#else
                            genericMethodInfo.CreateDelegate(delegateType, o);
#endif
                    }
                }
            }

            return genericDelegateCreator;
        }

        Delegate getDelegate(DelegateBridgeBase bridge, Type delegateType)
        {
            Delegate ret = bridge.GetDelegateByType(delegateType);

            if (ret != null)
            {
                return ret;
            }

            if (delegateType == typeof(Delegate) || delegateType == typeof(MulticastDelegate))
            {
                return null;
            }

            Func<DelegateBridgeBase, Delegate> delegateCreator;
            if (!delegateCreatorCache.TryGetValue(delegateType, out delegateCreator))
            {
                // get by parameters
                MethodInfo delegateMethod = delegateType.GetMethod("Invoke");
                var methods = bridge.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => !m.IsGenericMethodDefinition && (m.Name.StartsWith("__Gen_Delegate_Imp") || m.Name == "Action")).ToArray();
                for (int i = 0; i < methods.Length; i++)
                {
                    if (!methods[i].IsConstructor && Utils.IsParamsMatch(delegateMethod, methods[i]))
                    {
                        var foundMethod = methods[i];
                        delegateCreator = (o) =>
#if !UNITY_WSA || UNITY_EDITOR
                            Delegate.CreateDelegate(delegateType, o, foundMethod);
#else
                            foundMethod.CreateDelegate(delegateType, o);
#endif
                        break;
                    }
                }

                if (delegateCreator == null)
                {
                    delegateCreator = getCreatorUsingGeneric(bridge, delegateType, delegateMethod);
                }
                delegateCreatorCache.Add(delegateType, delegateCreator);
            }

            ret = delegateCreator(bridge);
            if (ret != null)
            {
                return ret;
            }
#if NET_STANDARD_2_1
            throw new InvalidCastException($"This type must add to CSharpCallLua: [delegateType.GetFriendlyName()]. may case by .NET Standard 2.1?");
#else
            throw new InvalidCastException("This type must add to CSharpCallLua: " + delegateType.GetFriendlyName());
#endif
        }
        Dictionary<int, WeakReference> delegate_bridges = new();
        public object CreateDelegateBridge(RealStatePtr L, Type delegateType, int idx)
        {
            LuaAPI.lua_pushvalue(L, idx);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
            if (!LuaAPI.lua_isnil(L, -1))
            {
                int referenced = LuaAPI.xlua_tointeger(L, -1);
                LuaAPI.lua_pop(L, 1);

                if (delegate_bridges[referenced].IsAlive)
                {
                    if (delegateType == null)
                    {
                        return delegate_bridges[referenced].Target;
                    }
                    DelegateBridgeBase exist_bridge = delegate_bridges[referenced].Target as DelegateBridgeBase;
                    Delegate exist_delegate;
                    if (exist_bridge.TryGetDelegate(delegateType, out exist_delegate))
                    {
                        return exist_delegate;
                    }
                    else
                    {
                        exist_delegate = getDelegate(exist_bridge, delegateType);
                        exist_bridge.AddDelegate(delegateType, exist_delegate);
                        return exist_delegate;
                    }
                }
            }
            else
            {
                LuaAPI.lua_pop(L, 1);
            }

            LuaAPI.lua_pushvalue(L, idx);
            int reference = LuaAPI.luaL_ref(L);
            LuaAPI.lua_pushvalue(L, idx);
            LuaAPI.lua_pushnumber(L, reference);
            LuaAPI.lua_rawset(L, LuaIndexes.LUA_REGISTRYINDEX);
            DelegateBridgeBase bridge;
            try
            {
                if (delegate_birdge_type != null)
                {
                    bridge = Activator.CreateInstance(delegate_birdge_type, reference, luaEnv) as DelegateBridgeBase;
                }
                else
                {
                    bridge = new DelegateBridge(reference, luaEnv);
                }
            }
            catch (Exception e)
            {
                LuaAPI.lua_pushvalue(L, idx);
                LuaAPI.lua_pushnil(L);
                LuaAPI.lua_rawset(L, LuaIndexes.LUA_REGISTRYINDEX);
                LuaAPI.lua_pushnil(L);
                LuaAPI.xlua_rawseti(L, LuaIndexes.LUA_REGISTRYINDEX, reference);
                throw e;
            }
            if (delegateType == null)
            {
                delegate_bridges[reference] = new WeakReference(bridge);
                return bridge;
            }
            try
            {
                var ret = getDelegate(bridge, delegateType);
                bridge.AddDelegate(delegateType, ret);
                delegate_bridges[reference] = new WeakReference(bridge);
                return ret;
            }
            catch (Exception e)
            {
                bridge.Dispose();
                throw e;
            }
        }

        public void ClearDelegateBridge(RealStatePtr L)
        {
            var keys = delegate_bridges.Keys.ToList();
            for (int i = keys.Count - 1; i >= 0; i--)
            {
                ReleaseLuaBase(L, keys[i], true);
            }
        }

        public bool AllDelegateBridgeReleased()
        {
            foreach (var kv in delegate_bridges)
            {
                if (kv.Value.IsAlive)
                {
                    return false;
                }
            }
            return true;
        }

        public void ReleaseLuaBase(RealStatePtr L, int reference, bool is_delegate)
        {
            if (is_delegate)
            {
                LuaAPI.xlua_rawgeti(L, LuaIndexes.LUA_REGISTRYINDEX, reference);
                if (LuaAPI.lua_isnil(L, -1))
                {
                    LuaAPI.lua_pop(L, 1);
                }
                else
                {
                    LuaAPI.lua_pushvalue(L, -1);
                    LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
                    if (LuaAPI.lua_type(L, -1) == LuaTypes.LUA_TNUMBER && LuaAPI.xlua_tointeger(L, -1) == reference) //
                    {
                        //LogW(LogTag.Unknown,"release delegate ref = " + luaReference);
                        LuaAPI.lua_pop(L, 1);// pop LUA_REGISTRYINDEX[func]
                        LuaAPI.lua_pushnil(L);
                        LuaAPI.lua_rawset(L, LuaIndexes.LUA_REGISTRYINDEX); // LUA_REGISTRYINDEX[func] = nil
                    }
                    else //another Delegate ref the function before the GC tick
                    {
                        LuaAPI.lua_pop(L, 2); // pop LUA_REGISTRYINDEX[func] & func
                    }
                }

                LuaAPI.lua_unref(L, reference);
                delegate_bridges.Remove(reference);
            }
            else
            {
                LuaAPI.lua_unref(L, reference);
            }
        }

        public object CreateInterfaceBridge(RealStatePtr L, Type interfaceType, int idx)
        {
            Func<int, LuaEnv, LuaBase> creator;

            if (!interfaceBridgeCreators.TryGetValue(interfaceType, out creator))
            {
#if (UNITY_EDITOR || XLUA_GENERAL) && !NET_STANDARD_2_0
                var bridgeType = ce.EmitInterfaceImpl(interfaceType);
                creator = (int reference, LuaEnv luaenv) =>
                {
                    return Activator.CreateInstance(bridgeType, reference, luaEnv) as LuaBase;
                };
                interfaceBridgeCreators.Add(interfaceType, creator);
#else
                throw new InvalidCastException("This type must add to CSharpCallLua: " + interfaceType);
#endif
            }
            LuaAPI.lua_pushvalue(L, idx);
            return creator(LuaAPI.luaL_ref(L), luaEnv);
        }

        public int common_array_meta = -1;
        public void CreateArrayMetatable(RealStatePtr L)
        {
            Utils.BeginObjectRegister(null, L, this, 0, 0, 1, 0, common_array_meta);
            Utils.RegisterFunc(L, Utils.GETTER_IDX, "Length", StaticLuaCallbacks.ArrayLength);
            Utils.RegisterRefFunc(L, Utils.OBJ_META_IDX, "__pairs", m_IListEnumerable);
            Utils.EndObjectRegister(null, L, this, null, null, typeof(Array), StaticLuaCallbacks.ArrayIndexer, StaticLuaCallbacks.ArrayNewIndexer);
        }

        int common_delegate_meta = -1;
        public void CreateDelegateMetatable(RealStatePtr L)
        {
            Utils.BeginObjectRegister(null, L, this, 3, 0, 0, 0, common_delegate_meta);
            Utils.RegisterFunc(L, Utils.OBJ_META_IDX, "__call", StaticLuaCallbacks.DelegateCall);
            Utils.RegisterFunc(L, Utils.OBJ_META_IDX, "__add", StaticLuaCallbacks.DelegateCombine);
            Utils.RegisterFunc(L, Utils.OBJ_META_IDX, "__sub", StaticLuaCallbacks.DelegateRemove);
            Utils.EndObjectRegister(null, L, this, null, null,
                 typeof(System.MulticastDelegate), null, null);
        }

        int m_IDictionaryEnumerable = -1;
        private int m_IListEnumerable = -1;

        internal void CreateIDictionaryEnumerable(RealStatePtr L)
        {
            LuaFunction func = luaEnv.DoString(@"
                return function(obj)
                    local function lua_iter(cs_iter, k)
                        if cs_iter:MoveNext() then
                            local current = cs_iter.Current
                            return current.Key, current.Value
                        end
                    end
                    return lua_iter, obj:GetEnumerator(), -1
                end
            ");
            func.push(L);
            m_IDictionaryEnumerable = LuaAPI.luaL_ref(L, LuaIndexes.LUA_REGISTRYINDEX);
            func.Dispose();
        }

        internal void CreateIListEnumerable(RealStatePtr L)
        {
            LuaFunction func = luaEnv.DoString(@"
                return function(obj)
                    local function lua_iter(cs_iter, k)
                        if cs_iter:MoveNext() then
                            return k, cs_iter.Current
                        end
                    end
                    return lua_iter, obj:GetEnumerator(), 0
                end
            ");
            func.push(L);
            m_IListEnumerable = LuaAPI.luaL_ref(L, LuaIndexes.LUA_REGISTRYINDEX);
            func.Dispose();
        }

        public void OpenLib(RealStatePtr L)
        {
            if (0 != LuaAPI.xlua_getglobal(L, "xlua"))
            {
                throw new Exception("call xlua_getglobal fail!" + LuaAPI.lua_tostring(L, -1));
            }
            LuaAPI.xlua_pushasciistring(L, "import_type");
            LuaAPI.lua_pushstdcallcfunction(L, importTypeFunction);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.xlua_pushasciistring(L, "import_generic_type");
            LuaAPI.lua_pushstdcallcfunction(L, StaticLuaCallbacks.ImportGenericType);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.xlua_pushasciistring(L, "cast");
            LuaAPI.lua_pushstdcallcfunction(L, castFunction);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.xlua_pushasciistring(L, "load_assembly");
            LuaAPI.lua_pushstdcallcfunction(L, loadAssemblyFunction);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.xlua_pushasciistring(L, "access");
            LuaAPI.lua_pushstdcallcfunction(L, StaticLuaCallbacks.XLuaAccess);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.xlua_pushasciistring(L, "metatable_operation");
            LuaAPI.lua_pushstdcallcfunction(L, StaticLuaCallbacks.XLuaMetatableOperation);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.xlua_pushasciistring(L, "tofunction");
            LuaAPI.lua_pushstdcallcfunction(L, StaticLuaCallbacks.ToFunction);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.xlua_pushasciistring(L, "get_generic_method");
            LuaAPI.lua_pushstdcallcfunction(L, StaticLuaCallbacks.GetGenericMethod);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.xlua_pushasciistring(L, "release");
            LuaAPI.lua_pushstdcallcfunction(L, StaticLuaCallbacks.ReleaseCsObject);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.lua_pop(L, 1);

            LuaAPI.lua_createtable(L, 1, 4); // 4 for __gc, __tostring, __index, __newindex
            common_array_meta = LuaAPI.luaL_ref(L, LuaIndexes.LUA_REGISTRYINDEX);
            LuaAPI.lua_createtable(L, 1, 4); // 4 for __gc, __tostring, __index, __newindex
            common_delegate_meta = LuaAPI.luaL_ref(L, LuaIndexes.LUA_REGISTRYINDEX);
        }

        internal void createFunctionMetatable(RealStatePtr L)
        {
            LuaAPI.lua_newtable(L);
            LuaAPI.xlua_pushasciistring(L, "__gc");
            LuaAPI.lua_pushstdcallcfunction(L, metaFunctions.GcMeta);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.lua_pushlightuserdata(L, LuaAPI.xlua_tag());
            LuaAPI.lua_pushnumber(L, 1);
            LuaAPI.lua_rawset(L, -3);

            LuaAPI.lua_pushvalue(L, -1);
            int type_id = LuaAPI.luaL_ref(L, LuaIndexes.LUA_REGISTRYINDEX);
            LuaAPI.lua_pushnumber(L, type_id);
            LuaAPI.xlua_rawseti(L, -2, 1);
            LuaAPI.lua_pop(L, 1);

            typeIdMap.Add(typeof(LuaCSFunction), type_id);
        }

        internal Type FindType(string className, bool isQualifiedName = false)
        {
            foreach (Assembly assembly in assemblies)
            {
                Type klass = assembly.GetType(className);

                if (klass != null)
                {
                    return klass;
                }
            }
            int p1 = className.IndexOf('[');
            if (p1 > 0 && !isQualifiedName)
            {
                string qualified_name = className.Substring(0, p1 + 1);
                string[] generic_params = className.Substring(p1 + 1, className.Length - qualified_name.Length - 1).Split(',');
                for (int i = 0; i < generic_params.Length; i++)
                {
                    Type generic_param = FindType(generic_params[i].Trim());
                    if (generic_param == null)
                    {
                        return null;
                    }
                    if (i != 0)
                    {
                        qualified_name += ", ";
                    }
                    qualified_name = qualified_name + "[" + generic_param.AssemblyQualifiedName + "]";
                }
                qualified_name += "]";
                return FindType(qualified_name, true);
            }
            return null;
        }

        bool hasMethod(Type type, string methodName)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.Name == methodName)
                {
                    return true;
                }
            }
            return false;
        }

        internal void collectObject(int obj_index_to_collect)
        {
            object o;

            if (objects.TryGetValue(obj_index_to_collect, out o))
            {
                objects.Remove(obj_index_to_collect);

                if (o != null)
                {
                    int obj_index;
                    //lua gc是先把weak table移除后再调用__gc，这期间同一个对象可能再次push到lua，关联到新的index
                    bool is_enum = o.GetType().IsEnum();
                    if ((is_enum ? enumMap.TryGetValue(o, out obj_index) : reverseMap.TryGetValue(o, out obj_index)) && obj_index == obj_index_to_collect)
                    {
                        if (is_enum)
                        {
                            enumMap.Remove(o);
                        }
                        else
                        {
                            reverseMap.Remove(o);
                        }
                    }
                    if (o is ILuaGCInterface c)
                    {
                        c.OnLuaGC();
                    }
                }
            }
        }

        int addObject(object obj, bool is_valuetype, bool is_enum)
        {
            int index = objects.Add(obj);
            if (is_enum)
            {
                enumMap[obj] = index;
            }
            else if (!is_valuetype)
            {
                reverseMap[obj] = index;
            }

            return index;
        }

        internal object GetObject(RealStatePtr L, int index)
        {
            return (objectCasters.GetCaster(typeof(object), null, null)(L, index, null));
        }

        internal T GetObject<T>(RealStatePtr L, int index)
        {
            return (T)objectCasters.GetCaster(typeof(T), null, null)(L, index, null);
        }

        public Type GetTypeOf(RealStatePtr L, int idx)
        {
            Type type = null;
            int type_id = LuaAPI.xlua_gettypeid(L, idx);
            if (type_id != -1)
            {
                typeMap.TryGetValue(type_id, out type);
            }
            return type;
        }

        public bool Assignable<T>(RealStatePtr L, int index)
        {
            return Assignable(L, index, typeof(T));
        }

        public bool Assignable(RealStatePtr L, int index, Type type)
        {
            if (LuaAPI.lua_type(L, index) == LuaTypes.LUA_TUSERDATA) // 快路径
            {
                int udata = LuaAPI.xlua_tocsobj_safe(L, index);
                object obj;
                if (udata != -1 && objects.TryGetValue(udata, out obj))
                {
                    RawObject rawObject = obj as RawObject;
                    if (rawObject != null)
                    {
                        obj = rawObject.Target;
                    }
                    if (obj == null)
                    {
                        return !type.IsValueType();
                    }
                    return type.IsAssignableFrom(obj.GetType());
                }

                int type_id = LuaAPI.xlua_gettypeid(L, index);
                Type type_of_struct;
                if (type_id != -1 && typeMap.TryGetValue(type_id, out type_of_struct)) // is struct
                {
                    return type.IsAssignableFrom(type_of_struct);
                }
            }

            return objectCheckers.GetChecker(type)(L, index);
        }

        public object GetObject(RealStatePtr L, int index, Type type)
        {
            int udata = LuaAPI.xlua_tocsobj_safe(L, index);

            if (udata != -1)
            {
                object obj = objects.Get(udata);
                RawObject rawObject = obj as RawObject;
                return rawObject == null ? obj : rawObject.Target;
            }
            else
            {
                if (LuaAPI.lua_type(L, index) == LuaTypes.LUA_TUSERDATA)
                {
                    GetCSObject get;
                    int type_id = LuaAPI.xlua_gettypeid(L, index);
                    Type type_of_struct;
                    if (type_id != -1 && typeMap.TryGetValue(type_id, out type_of_struct) && type.IsAssignableFrom(type_of_struct) && custom_get_funcs.TryGetValue(type, out get))
                    {
                        return get(L, index);
                    }
                }
                return (objectCasters.GetCaster(type, null, null)(L, index, null));
            }
        }

        public void Get<T>(RealStatePtr L, int index, out T v)
        {
            Func<RealStatePtr, int, T> get_func;
            if (tryGetGetFuncByType(typeof(T), out get_func))
            {
                v = get_func(L, index);
            }
            else
            {
                v = (T)GetObject(L, index, typeof(T));
            }
        }

        public void PushByType<T>(RealStatePtr L, T v)
        {
            Action<RealStatePtr, T> push_func;
            if (tryGetPushFuncByType(typeof(T), out push_func))
            {
                push_func(L, v);
            }
            else
            {
                PushAny(L, v);
            }
        }

#if GENERIC_SHARING
        public T GetByType<T>(RealStatePtr L, int index)
        {
            Func<RealStatePtr, int, T> get_func;
            if (tryGetGetFuncByType(typeof(T), out get_func))
            {
                return get_func(L, index);
            }
            else
            {
                return (T)GetObject(L, index, typeof(T));
            }
        }
#endif

        public T[] GetParams<T>(RealStatePtr L, int index)
        {
            T[] ret = new T[Math.Max(LuaAPI.lua_gettop(L) - index + 1, 0)];
            for (int i = 0; i < ret.Length; i++)
            {
                Get(L, index + i, out ret[i]);
            }
            return ret;
        }

        public Array GetParams(RealStatePtr L, int index, Type type) //反射版本
        {
            Array ret = Array.CreateInstance(type, Math.Max(LuaAPI.lua_gettop(L) - index + 1, 0)); //这个函数，长度为0的话，返回null
            for (int i = 0; i < ret.Length; i++)
            {
                ret.SetValue(GetObject(L, index + i, type), i);
            }
            return ret;
        }
#if UNITY_EDITOR
        public void PushParams(RealStatePtr L, Array ary)
        {
            if (ary != null)
            {
                for (int i = 0; i < ary.Length; i++)
                {
                    PushAny(L, ary.GetValue(i));
                }
            }
        }
#endif

        public T GetDelegate<T>(RealStatePtr L, int index) where T : class
        {
            Int32 sampleIndex = -1;

            if (LuaAPI.lua_isfunction(L, index))
            {
#if OSGAME
                StatsLite.BeginSample(StatsSampleId.xLua_Delegate, "GetDelegate<#LuaFunc>", ref sampleIndex);
#endif
                
                T ret = CreateDelegateBridge(L, typeof(T), index) as T;
#if OSGAME
                StatsLite.EndSampleByIndex(ref sampleIndex);
#endif
                
                return ret;
            }
            else if (LuaAPI.lua_type(L, index) == LuaTypes.LUA_TUSERDATA)
            {
#if OSGAME
                StatsLite.BeginSample(StatsSampleId.xLua_Delegate, "GetDelegate<T>", ref sampleIndex);
#endif
                
                T ret = (T)SafeGetCSObj(L, index);
#if OSGAME
                StatsLite.EndSampleByIndex(ref sampleIndex);
#endif
                
                return ret;
            }
            else
            {
                return null;
            }
        }

        Dictionary<Type, int> typeIdMap = new();

        //only store the type id to type map for struct
        Dictionary<int, Type> typeMap = new();

        public int GetTypeId(RealStatePtr L, Type type)
        {
            bool isFirst;
            return getTypeId(L, type, out isFirst);
        }

        public int getTypeId(RealStatePtr L, Type type, out bool is_first, LOGLEVEL log_level = LOGLEVEL.WARN)
        {
            LuaAPI.lua_checkstack(L, 4);
            int type_id;
            is_first = false;
            if (!typeIdMap.TryGetValue(type, out type_id)) // no reference
            {
                if (type.IsArray)
                {
                    if (common_array_meta == -1) throw new Exception("Fatal Exception! Array Metatable not inited!");
                    TryDelayWrapLoader(L, type);
                    return common_array_meta;
                }
                if (typeof(MulticastDelegate).IsAssignableFrom(type))
                {
                    if (common_delegate_meta == -1) throw new Exception("Fatal Exception! Delegate Metatable not inited!");
                    TryDelayWrapLoader(L, type);
                    return common_delegate_meta;
                }

                is_first = true;
                Type alias_type = null;
                aliasCfg.TryGetValue(type, out alias_type);
                LuaAPI.luaL_getmetatable(L, alias_type == null ? type.FullName : alias_type.FullName);

                if (LuaAPI.lua_isnil(L, -1)) //no meta yet, try to use reflection meta
                {
                    LuaAPI.lua_pop(L, 1);

                    if (TryDelayWrapLoader(L, alias_type == null ? type : alias_type))
                    {
                        LuaAPI.luaL_getmetatable(L, alias_type == null ? type.FullName : alias_type.FullName);
                    }
                    else
                    {
                        throw new Exception("Fatal: can not load metatable of type:" + type);
                    }
                }

                //循环依赖，自身依赖自己的class，比如有个自身类型的静态readonly对象。
                if (typeIdMap.TryGetValue(type, out type_id))
                {
                    LuaAPI.lua_pop(L, 1);
                }
                else
                {
                    if (type.IsEnum())
                    {
                        LuaAPI.xlua_pushasciistring(L, "__band");
                        LuaAPI.lua_pushstdcallcfunction(L, metaFunctions.EnumAndMeta);
                        LuaAPI.lua_rawset(L, -3);
                        LuaAPI.xlua_pushasciistring(L, "__bor");
                        LuaAPI.lua_pushstdcallcfunction(L, metaFunctions.EnumOrMeta);
                        LuaAPI.lua_rawset(L, -3);
                    }
                    if (typeof(IDictionary).IsAssignableFrom(type))
                    {
                        LuaAPI.xlua_pushasciistring(L, "__pairs");
                        LuaAPI.lua_getref(L, m_IDictionaryEnumerable);
                        LuaAPI.lua_rawset(L, -3);
                    }
                    else if (typeof(IEnumerable).IsAssignableFrom(type))
                    {
                        LuaAPI.xlua_pushasciistring(L, "__pairs");
                        LuaAPI.lua_getref(L, m_IListEnumerable);
                        LuaAPI.lua_rawset(L, -3);
                    }
                    LuaAPI.lua_pushvalue(L, -1);
                    type_id = LuaAPI.luaL_ref(L, LuaIndexes.LUA_REGISTRYINDEX);
                    LuaAPI.lua_pushnumber(L, type_id);
                    LuaAPI.xlua_rawseti(L, -2, 1);
                    LuaAPI.lua_pop(L, 1);

                    if (type.IsValueType())
                    {
                        typeMap.Add(type_id, type);
                    }

                    typeIdMap.Add(type, type_id);
                }
            }
            return type_id;
        }

        void pushPrimitive(RealStatePtr L, object o)
        {
            LuaAPI.lua_checkstack(L, 1);
            if (o is sbyte || o is byte || o is short || o is ushort || o is int)
            {
                Converter.Specializer<int>.toScript(LuaEnv.Instance.ffi, L, Convert.ToInt32(o));
            }
            else if (o is uint)
            {
                Converter.Specializer<uint>.toScript(LuaEnv.Instance.ffi, L, (uint)o);
            }
            else if (o is float || o is double)
            {
                Converter.Specializer<double>.toScript(LuaEnv.Instance.ffi, L, Convert.ToDouble(o));
            }
            else if (o is IntPtr)
            {
                LuaAPI.lua_pushlightuserdata(L, (IntPtr)o);
            }
            else if (o is char)
            {
                Converter.Specializer<char>.toScript(LuaEnv.Instance.ffi, L, (char)o);
            }
            else if (o is long)
            {
                Converter.Specializer<Int64>.toScript(LuaEnv.Instance.ffi, L, Convert.ToInt64(o));
            }
            else if (o is ulong)
            {
                Converter.Specializer<UInt64>.toScript(LuaEnv.Instance.ffi, L, Convert.ToUInt64(o));
            }
            else if (o is bool)
            {
                Converter.Specializer<bool>.toScript(LuaEnv.Instance.ffi, L, (bool)o);
            }
            else
            {
                throw new Exception("No support type " + o.GetType());
            }
        }

        public void PushAny(RealStatePtr L, object o)
        {
            LuaAPI.lua_checkstack(L, 1);
            if (o == null)
            {
                LuaAPI.lua_pushnil(L);
                return;
            }

            Type type = o.GetType();
            if (type.IsPrimitive())
            {
                pushPrimitive(L, o);
            }
            else if (o is string)
            {
                Converter.Specializer<string>.toScript(LuaEnv.Instance.ffi, L, o as string);
            }
            else if (type == typeof(byte[]))
            {
                LuaAPI.lua_pushstring(L, o as byte[]);
            }
            else if (o is LuaBase)
            {
                ((LuaBase)o).push(L);
            }
            else if (o is LuaCSFunction)
            {
                Push(L, o as LuaCSFunction);
            }
            else if (o is ValueType)
            {
                PushCSObject push;
                if (custom_push_funcs.TryGetValue(o.GetType(), out push))
                {
                    push(L, o);
                }
                else if (type.IsEnum)
                {
                    pushPrimitive(L, System.Convert.ToInt64(o));
                }
                else
                {
                    Push(L, o);
                }
            }
            else
            {
                Push(L, o);
            }
        }

        Dictionary<object, int> enumMap = new();

        public int TranslateToEnumToTop(RealStatePtr L, Type type, int idx)
        {
            object res = null;
            LuaTypes lt = (LuaTypes)LuaAPI.lua_type(L, idx);
            if (lt == LuaTypes.LUA_TNUMBER)
            {
                int ival = (int)LuaAPI.lua_tonumber(L, idx);
                res = Enum.ToObject(type, ival);
            }
            else if (lt == LuaTypes.LUA_TSTRING)
            {
                string sflags = LuaAPI.lua_tostring(L, idx);
                res = Enum.Parse(type, sflags);
            }
            else
            {
                return LuaAPI.luaL_error(L, "#1 argument must be a integer or a string");
            }
            PushAny(L, res);
            return 1;
        }

        public void Push(RealStatePtr L, LuaCSFunction o)
        {
            if (Utils.IsStaticPInvokeCSFunction(o))
            {
                LuaAPI.lua_pushstdcallcfunction(L, o);
            }
            else
            {
                Push(L, (object)o);
                LuaAPI.lua_pushstdcallcfunction(L, metaFunctions.StaticCSFunctionWraper, 1);
            }
        }

        public void Push(RealStatePtr L, LuaBase o)
        {
            if (o == null)
            {
                LuaAPI.lua_pushnil(L);
            }
            else
            {
                o.push(L);
            }
        }

        public void Push(RealStatePtr L, object o)
        {
            LuaAPI.lua_checkstack(L, 1);
            if (o == null)
            {
                LuaAPI.lua_pushnil(L);
                return;
            }

            if (o is UnityEngine.Object && o as UnityEngine.Object == null)
            {
                LuaAPI.lua_pushnil(L);
                return;
            }

            int index = -1;
            Type type = o.GetType();
#if !UNITY_WSA || UNITY_EDITOR
            bool is_enum = type.IsEnum;
            bool is_valuetype = type.IsValueType;
#else
            bool is_enum = type.GetTypeInfo().IsEnum;
            bool is_valuetype = type.GetTypeInfo().IsValueType;
#endif
            bool needcache = !is_valuetype || is_enum;
            if (needcache && (is_enum ? enumMap.TryGetValue(o, out index) : reverseMap.TryGetValue(o, out index)))
            {
                if (LuaAPI.xlua_tryget_cachedud(L, index, cacheRef) == 1)
                {
                    return;
                }
                //这里实在太经典了，weaktable先删除，然后GC会延迟调用，当index会循环利用的时候，不注释这行将会导致重复释放
                //collectObject(index);
            }

            bool is_first;
            int type_id = getTypeId(L, type, out is_first);

            //如果一个type的定义含本身静态readonly实例时，getTypeId会push一个实例，这时候应该用这个实例
            if (is_first && needcache && (is_enum ? enumMap.TryGetValue(o, out index) : reverseMap.TryGetValue(o, out index)))
            {
                if (LuaAPI.xlua_tryget_cachedud(L, index, cacheRef) == 1)
                {
                    return;
                }
            }

            index = addObject(o, is_valuetype, is_enum);
            LuaAPI.xlua_pushcsobj(L, index, type_id, needcache, cacheRef);
        }

        public void Update(RealStatePtr L, int index, object obj)
        {
            int udata = LuaAPI.xlua_tocsobj_fast(L, index);

            if (udata != -1)
            {
                objects.Replace(udata, obj);
            }
            else
            {
                UpdateCSObject update;
                if (custom_update_funcs.TryGetValue(obj.GetType(), out update))
                {
                    update(L, index, obj);
                }
                else
                {
                    throw new Exception("can not update [" + obj + "]");
                }
            }
        }

        private object getCsObj(RealStatePtr L, int index, int udata)
        {
            object obj;
            if (udata == -1)
            {
                if (LuaAPI.lua_type(L, index) != LuaTypes.LUA_TUSERDATA) return null;

                Type type = GetTypeOf(L, index);
                GetCSObject get;
                if (type != null && custom_get_funcs.TryGetValue(type, out get))
                {
                    return get(L, index);
                }
                else
                {
                    return null;
                }
            }
            else if (objects.TryGetValue(udata, out obj))
            {
#if !UNITY_5 && !XLUA_GENERAL && !UNITY_2017 && !UNITY_2017_1_OR_NEWER && !UNITY_2018
                if (obj != null && obj is UnityEngine.Object && ((obj as UnityEngine.Object) == null))
                {
                    //throw new UnityEngine.MissingReferenceException("The object of type '"+ obj.GetType().Name +"' has been destroyed but you are still trying to access it.");
                    return null;
                }
#endif
                return obj;
            }
            return null;
        }

        internal object SafeGetCSObj(RealStatePtr L, int index)
        {
            return getCsObj(L, index, LuaAPI.xlua_tocsobj_safe(L, index));
        }

        public object FastGetCSObj(RealStatePtr L, int index)
        {
            return getCsObj(L, index, LuaAPI.xlua_tocsobj_fast(L, index));
        }

        internal void ReleaseCSObj(RealStatePtr L, int index)
        {
            int udata = LuaAPI.xlua_tocsobj_safe(L, index);
            if (udata != -1)
            {
                object o = objects.Replace(udata, null);
                if (o != null && reverseMap.ContainsKey(o))
                {
                    reverseMap.Remove(o);
                }
            }
        }

        List<LuaCSFunction> fix_cs_functions = new();

        internal LuaCSFunction GetFixCSFunction(int index)
        {
            return fix_cs_functions[index];
        }

        internal void PushFixCSFunction(RealStatePtr L, LuaCSFunction func)
        {
            if (func == null)
            {
                LuaAPI.lua_pushnil(L);
            }
            else
            {
                LuaAPI.xlua_pushinteger(L, fix_cs_functions.Count);
                fix_cs_functions.Add(func);
                LuaAPI.lua_pushstdcallcfunction(L, metaFunctions.FixCSFunctionWraper, 1);
            }
        }

#if GEN_CODE_MINIMIZE
        CSharpWrapper[] csharpWrapper = new CSharpWrapper[0];
        int csharpWrapperSize = 0;

        internal int CallCSharpWrapper(RealStatePtr L, int funcidx, int top)
        {
            return csharpWrapper[funcidx](L, top);
        }

        void ensureCSharpWrapperCapacity(int min)
        {
            if (csharpWrapper.Length < min)
            {
                int num = (csharpWrapper.Length == 0) ? 4 : (csharpWrapper.Length * 2);
                if (num > 2146435071)
                {
                    num = 2146435071;
                }
                if (num < min)
                {
                    num = min;
                }

                var array = new CSharpWrapper[num];
                Array.Copy(csharpWrapper, 0, array, 0, csharpWrapper.Length);
                csharpWrapper = array;
            }
        }

        internal void PushCSharpWrapper(RealStatePtr L, CSharpWrapper func)
        {
            if (func == null)
            {
                LuaAPI.lua_pushnil(L);
            }
            else
            {
                LuaAPI.xlua_push_csharp_wrapper(L, csharpWrapperSize);
                ensureCSharpWrapperCapacity(csharpWrapperSize + 1);
                csharpWrapper[csharpWrapperSize++] = func;
            }
        }
#endif

        internal object[] popValues(RealStatePtr L, int oldTop)
        {
            int newTop = LuaAPI.lua_gettop(L);
            if (oldTop == newTop)
            {
                return null;
            }
            else
            {
                ArrayList returnValues = new ArrayList();
                for (int i = oldTop + 1; i <= newTop; i++)
                {
                    returnValues.Add(GetObject(L, i));
                }
                LuaAPI.lua_settop(L, oldTop);
                return returnValues.ToArray();
            }
        }

        internal T popValue<T>(RealStatePtr L, int oldTop)
        {
            var newTop = LuaAPI.lua_gettop(L);
            if (oldTop == newTop)
            {
                return default;
            }
            var ret = GetObject<T>(L, oldTop + 1);
            LuaAPI.lua_settop(L, oldTop);
            return ret;
        }

        internal object popValue(RealStatePtr L, int oldTop, Type type)
        {
            var newTop = LuaAPI.lua_gettop(L);
            if (oldTop == newTop)
            {
                return default;
            }
            var ret = GetObject(L, oldTop + 1, type);
            LuaAPI.lua_settop(L, oldTop);
            return ret;
        }

        internal object[] popValues(RealStatePtr L, int oldTop, Type[] popTypes)
        {
            int newTop = LuaAPI.lua_gettop(L);
            if (oldTop == newTop)
            {
                return null;
            }
            else
            {
                int iTypes;
                ArrayList returnValues = new ArrayList();
                if (popTypes[0] == typeof(void))
                    iTypes = 1;
                else
                    iTypes = 0;
                for (int i = oldTop + 1; i <= newTop; i++)
                {
                    returnValues.Add(GetObject(L, i, popTypes[iTypes]));
                    iTypes++;
                }
                LuaAPI.lua_settop(L, oldTop);
                return returnValues.ToArray();
            }
        }

        public delegate void PushCSObject(RealStatePtr L, object obj);
        public delegate object GetCSObject(RealStatePtr L, int idx);
        public delegate void UpdateCSObject(RealStatePtr L, int idx, object obj);

        private Dictionary<Type, PushCSObject> custom_push_funcs = new();
        private Dictionary<Type, GetCSObject> custom_get_funcs = new();
        private Dictionary<Type, UpdateCSObject> custom_update_funcs = new();

        void registerCustomOp(Type type, PushCSObject push, GetCSObject get, UpdateCSObject update)
        {
            if (push != null) custom_push_funcs.Add(type, push);
            if (get != null) custom_get_funcs.Add(type, get);
            if (update != null) custom_update_funcs.Add(type, update);
        }

        public bool HasCustomOp(Type type)
        {
            return custom_push_funcs.ContainsKey(type);
        }

        private Dictionary<Type, Delegate> push_func_with_type = null;

        bool tryGetPushFuncByType<T>(Type type, out T func) where T : class
        {
            if (push_func_with_type == null)
            {
                push_func_with_type = new Dictionary<Type, Delegate>()
                {
                    {typeof(int),  new Action<RealStatePtr, int>(LuaAPI.xlua_pushinteger) },
                    {typeof(double), new Action<RealStatePtr, double>(LuaAPI.lua_pushnumber) },
                    {typeof(string), new Action<RealStatePtr, string>(LuaAPI.lua_pushstring) },
                    {typeof(byte[]), new Action<RealStatePtr, byte[]>(LuaAPI.lua_pushstring) },
                    {typeof(bool), new Action<RealStatePtr, bool>(LuaAPI.lua_pushboolean) },
                    {typeof(long), new Action<RealStatePtr, long>(LuaAPI.lua_pushint64) },
                    {typeof(ulong), new Action<RealStatePtr, ulong>(LuaAPI.lua_pushuint64) },
                    {typeof(IntPtr), new Action<RealStatePtr, IntPtr>(LuaAPI.lua_pushlightuserdata) },
                    {typeof(byte),  new Action<RealStatePtr, byte>((L, v) => LuaAPI.xlua_pushinteger(L, v)) },
                    {typeof(sbyte),  new Action<RealStatePtr, sbyte>((L, v) => LuaAPI.xlua_pushinteger(L, v)) },
                    {typeof(char),  new Action<RealStatePtr, char>((L, v) => LuaAPI.xlua_pushinteger(L, v)) },
                    {typeof(short),  new Action<RealStatePtr, short>((L, v) => LuaAPI.xlua_pushinteger(L, v)) },
                    {typeof(ushort),  new Action<RealStatePtr, ushort>((L, v) => LuaAPI.xlua_pushinteger(L, v)) },
                    {typeof(uint),  new Action<RealStatePtr, uint>(LuaAPI.xlua_pushuint) },
                    {typeof(float),  new Action<RealStatePtr, float>((L, v) => LuaAPI.lua_pushnumber(L, v)) },
                };
            }

            Delegate obj;
            if (push_func_with_type.TryGetValue(type, out obj))
            {
                func = obj as T;
                return true;
            }
            else
            {
                func = null;
                return false;
            }
        }

        private Dictionary<Type, Delegate> get_func_with_type = null;

        bool tryGetGetFuncByType<T>(Type type, out T func) where T : class
        {
            if (get_func_with_type == null)
            {
                get_func_with_type = new Dictionary<Type, Delegate>()
                {
                    {typeof(int), new Func<RealStatePtr, int, int>(LuaAPI.xlua_tointeger) },
                    {typeof(double), new Func<RealStatePtr, int, double>(LuaAPI.lua_tonumber) },
                    {typeof(string), new Func<RealStatePtr, int, string>(LuaAPI.lua_tostring) },
                    {typeof(byte[]), new Func<RealStatePtr, int, byte[]>(LuaAPI.lua_tobytes) },
                    {typeof(bool), new Func<RealStatePtr, int, bool>(LuaAPI.lua_toboolean) },
                    {typeof(long), new Func<RealStatePtr, int, long>(LuaAPI.lua_toint64) },
                    {typeof(ulong), new Func<RealStatePtr, int, ulong>(LuaAPI.lua_touint64) },
                    {typeof(IntPtr), new Func<RealStatePtr, int, IntPtr>(LuaAPI.lua_touserdata) },
                    {typeof(byte), new Func<RealStatePtr, int, byte>((L, idx) => (byte)LuaAPI.xlua_tointeger(L, idx) ) },
                    {typeof(sbyte), new Func<RealStatePtr, int, sbyte>((L, idx) => (sbyte)LuaAPI.xlua_tointeger(L, idx) ) },
                    {typeof(char), new Func<RealStatePtr, int, char>((L, idx) => (char)LuaAPI.xlua_tointeger(L, idx) ) },
                    {typeof(short), new Func<RealStatePtr, int, short>((L, idx) => (short)LuaAPI.xlua_tointeger(L, idx) ) },
                    {typeof(ushort), new Func<RealStatePtr, int, ushort>((L, idx) => (ushort)LuaAPI.xlua_tointeger(L, idx) ) },
                    {typeof(uint), new Func<RealStatePtr, int, uint>(LuaAPI.xlua_touint) },
                    {typeof(float), new Func<RealStatePtr, int, float>((L, idx) => (float)LuaAPI.lua_tonumber(L, idx) ) },
                };
            }

            Delegate obj;
            if (get_func_with_type.TryGetValue(type, out obj))
            {
                func = obj as T;
                return true;
            }
            else
            {
                func = null;
                return false;
            }
        }


        public delegate void GetFunc<T>(RealStatePtr L, int idx, out T val);

        public void RegisterPushAndGetAndUpdate<T>(Action<RealStatePtr, T> push, GetFunc<T> get, Action<RealStatePtr, int, T> update)
        {
            Type type = typeof(T);
            Action<RealStatePtr, T> org_push;
            Func<RealStatePtr, int, T> org_get;
            if (tryGetPushFuncByType(type, out org_push) || tryGetGetFuncByType(type, out org_get))
            {
                throw new InvalidOperationException("push or get of " + type + " has register!");
            }
            push_func_with_type.Add(type, push);
            get_func_with_type.Add(type, new Func<RealStatePtr, int, T>((L, idx) => {
                T ret;
                get(L, idx, out ret);
                return ret;
            }));

            registerCustomOp(type,
                (RealStatePtr L, object obj) => {
                    push(L, (T)obj);
                },
                (RealStatePtr L, int idx) => {
                    T val;
                    get(L, idx, out val);
                    return val;
                },
                (RealStatePtr L, int idx, object obj) => {
                    update(L, idx, (T)obj);
                }
            );
        }
    }
#endif
            }

