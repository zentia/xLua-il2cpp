using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using Object = System.Object;
using LuaAPI = XLua.LuaDLL.Lua;
using RealStatePtr = System.IntPtr;
using LuaCSFunction = XLua.LuaDLL.lua_CSFunction;

namespace XLua
{
    internal static class Utils_Internal
    {
        internal static volatile Dictionary<Type, IEnumerable<MethodInfo>> extensionMethodMap = null;
    }
    public enum LazyMemberTypes
    {
        Method,
        FieldGet,
        FieldSet,
        PropertyGet,
        PropertySet,
        Event,
    }

    [UnityEngine.Scripting.Preserve]
    public static partial class Utils
    {
        public static long TwoIntToLong(int b, int a)
        {
            return (long)a << 32 | b & 0xFFFFFFFFL;
        }

        public static void LongToTwoInt(long c, out int b, out int a)
        {
            a = (int)(c >> 32);
            b = (int)c;
        }

        private static bool HasValidContraint(Type type, List<Type> validTypes)
        {
            if (type.IsGenericType)
            {
                Type[] genericArguments = type.GetGenericArguments();
                foreach (Type argument in genericArguments)
                {
                    if (!HasValidContraint(argument, validTypes))
                    {
                        return false;
                    }
                }

                // validTypes.Add(type);
                return true;
            }
            else if (type.IsGenericParameter)
            {
                if (
                    type.BaseType != null && type.BaseType.IsValueType
                ) return false;

                var parameterConstraints = type.GetGenericParameterConstraints();

                if (parameterConstraints.Length == 0) return false;
                foreach (var parameterConstraint in parameterConstraints)
                {
                    // the constraint could not be another genericType #533
                    if (
                        !parameterConstraint.IsClass ||
                        parameterConstraint == typeof(ValueType) ||
                        (
                            parameterConstraint.IsGenericType &&
                            !parameterConstraint.IsGenericTypeDefinition
                        )
                    )
                    {
                        return false;
                    }
                }

                validTypes.Add(type);
                return true;
            }
            else
            {
                return true;
            }
        }

        public static bool IsNotGenericOrValidGeneric(MethodInfo method, ParameterInfo[] pinfos = null)
        {
            // 不包含泛型参数，肯定支持
            if (!method.ContainsGenericParameters)
                return true;

            List<Type> validGenericParameter = new List<Type>();

            if (pinfos == null) pinfos = method.GetParameters();
            foreach (var parameters in pinfos)
            {
                Type parameterType = parameters.ParameterType;

                if (!HasValidContraint(parameterType, validGenericParameter)) { return false; }
            }

            return validGenericParameter.Count > 0 && (
                // 返回值也需要判断，必须是非泛型，或者是可用泛型参数里正好也包括返回类型
                !method.ReturnType.IsGenericParameter ||
                validGenericParameter.Contains(method.ReturnType)
            );
        }

        public static bool IsSupportedMethod(MethodInfo method)
        {
#if !UNITY_EDITOR && ENABLE_IL2CPP && !XLUA_REFLECT_ALL_EXTENSION
            if (method.IsGenericMethodDefinition) return false;
#endif
            if (!method.ContainsGenericParameters)
                return true;
            var methodParameters = method.GetParameters();
            var returnType = method.ReturnType;
            var hasValidGenericParameter = false;
            var returnTypeValid = !returnType.IsGenericParameter;
            // 在参数列表里找得到和泛型参数相同的参数
            for (var i = 0; i < methodParameters.Length; i++)
            {
                var parameterType = methodParameters[i].ParameterType;
                // 如果参数是泛型参数
                if (parameterType.IsGenericParameter)
                {
                    // 所有参数的基类都不是值类型，且不是另一个泛型
                    if (
                        parameterType.BaseType != null && (
                            parameterType.BaseType.IsValueType ||
                            (
                                parameterType.BaseType.IsGenericType &&
                                !parameterType.BaseType.IsGenericTypeDefinition
                            )
                        )
                    ) return false;
                    var parameterConstraints = parameterType.GetGenericParameterConstraints();
                    // 所有泛型参数都有值类型约束
                    if (parameterConstraints.Length == 0) return false;
                    foreach (var parameterConstraint in parameterConstraints)
                    {
                        // 所有泛型参数的类型约束都不是值类型
                        if (!parameterConstraint.IsClass || (parameterConstraint == typeof(ValueType)))
                            return false;
                    }
                    hasValidGenericParameter = true;
                    if (!returnTypeValid)
                    {
                        if (parameterType == returnType)
                        {
                            returnTypeValid = true;
                        }
                    }
                }
            }
            return hasValidGenericParameter && returnTypeValid;
        }
        public static MethodInfo[] GetMethodAndOverrideMethodByName(Type type, string name)
        {
            MethodInfo[] allMethods = type.GetMember(name).Select(m => (MethodInfo)m).ToArray();

            Dictionary<string, IEnumerable<Type[]>> errorMethods = type.GetMethods()
                .Where(m => m.DeclaringType != type && IsObsoleteError(m))
                .GroupBy(m => m.Name)
                .ToDictionary(i => i.Key, i => i.Cast<MethodInfo>().Select(m => m.GetParameters().Select(o => o.ParameterType).ToArray()));
            IEnumerable<Type[]> matchTypes;

            Type objType = typeof(Object);
            while (type.BaseType != null && type.BaseType != objType)
            {
                type = type.BaseType;
                MethodInfo[] methods = type.GetMember(name)
                    .Select(m => (MethodInfo)m)
                    .Where(m => !IsObsoleteError(m) && !IsVirtualMethod(m))
                    .Where(m => !errorMethods.TryGetValue(m.Name, out matchTypes) || !IsMatchParameters(matchTypes, m.GetParameters().Select(o => o.ParameterType).ToArray()))  //filter override method
                    .ToArray();
                if (methods.Length > 0)
                {
                    allMethods = allMethods.Concat(methods).ToArray();
                }
            }

            return allMethods;
        }

        public static MethodInfo[] GetMethodAndOverrideMethod(Type type, BindingFlags flag)
        {
            MethodInfo[] allMethods = type.GetMethods(flag);
            string[] methodNames = allMethods.Select(m => m.Name).ToArray();

            Dictionary<string, IEnumerable<Type[]>> errorMethods = type.GetMethods()
                .Where(m => m.DeclaringType != type && IsObsoleteError(m))
                .GroupBy(m => m.Name)
                .ToDictionary(i => i.Key, i => i.Cast<MethodInfo>().Select(m => m.GetParameters().Select(o => o.ParameterType).ToArray()));
            IEnumerable<Type[]> matchTypes;

            Type objType = typeof(Object);
            while (type.BaseType != null && type.BaseType != objType)
            {
                type = type.BaseType;
                MethodInfo[] methods = type.GetMethods(flag)
                    .Where(m => Array.IndexOf<string>(methodNames, m.Name) != -1)
                    .Where(m => !IsObsoleteError(m) && !IsVirtualMethod(m))
                    .Where(m => !m.IsSpecialName || !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_"))   //filter property
                    .Where(m => !errorMethods.TryGetValue(m.Name, out matchTypes) || !IsMatchParameters(matchTypes, m.GetParameters().Select(o => o.ParameterType).ToArray()))  //filter override method
                    .ToArray();
                if (methods.Length > 0)
                {
                    allMethods = allMethods.Concat(methods).ToArray();
                }
            }

            return allMethods;
        }
        private static bool IsVirtualMethod(MethodInfo memberInfo)
        {
            return memberInfo.IsAbstract || (memberInfo.Attributes & MethodAttributes.NewSlot) == MethodAttributes.NewSlot;
        }
        private static bool IsObsoleteError(MemberInfo memberInfo)
        {
            var obsolete = memberInfo.GetCustomAttributes(typeof(ObsoleteAttribute), true).FirstOrDefault() as ObsoleteAttribute;
            return obsolete != null && obsolete.IsError;
        }

        private static bool IsMatchParameters(IEnumerable<Type[]> typeList, Type[] pTypes)
        {
            foreach (var types in typeList)
            {
                if (types.Length != pTypes.Length)
                    continue;

                bool exclude = true;
                for (int i = 0; i < pTypes.Length && exclude; i++)
                {
                    if (pTypes[i] != types[i])
                        exclude = false;
                }
                if (exclude)
                    return true;
            }
            return false;
        }

        private static Type GetExtendedType(MethodInfo method)
        {
            var type = method.GetParameters()[0].ParameterType;
            if (!type.IsGenericParameter)
                return type;
            var parameterConstraints = type.GetGenericParameterConstraints();
            if (parameterConstraints.Length == 0)
                throw new InvalidOperationException();

            var firstParameterConstraint = parameterConstraints[0];
            if (!firstParameterConstraint.IsClass)
                throw new InvalidOperationException();
            return firstParameterConstraint;
        }

        public static List<Type> GetAllTypes(bool exclude_generic_definition = true)
        {
            List<Type> allTypes = new List<Type>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
#if UNITY_EDITOR && !NET_STANDARD_2_0
                    if (!(assemblies[i].ManifestModule is System.Reflection.Emit.ModuleBuilder))
                    {
#endif
                        allTypes.AddRange(assemblies[i].GetTypes()
                            .Where(type => exclude_generic_definition ? !type.IsGenericTypeDefinition : true));
#if UNITY_EDITOR && !NET_STANDARD_2_0
                    }
#endif
                }
                catch (Exception)
                {
                }
            }

            return allTypes;
        }

		public static bool LoadField(RealStatePtr L, int idx, string field_name)
		{
			idx = idx > 0 ? idx : LuaAPI.lua_gettop(L) + idx + 1;// abs of index
			LuaAPI.xlua_pushasciistring(L, field_name);
			LuaAPI.lua_rawget(L, idx);
			return !LuaAPI.lua_isnil(L, -1);
		}

#if !XLUA_IL2CPP || !ENABLE_IL2CPP
        static LuaCSFunction genFieldGetter(Type type, FieldInfo field)
        {
            if (field.IsStatic)
            {
                return (RealStatePtr L) =>
                {
                    ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
                    translator.PushAny(L, field.GetValue(null));
                    return 1;
                };
            }
            else
            {
                return (RealStatePtr L) =>
                {
                    ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
                    object obj = translator.FastGetCSObj(L, 1);
                    if (obj == null || !type.IsInstanceOfType(obj))
                    {
                        return LuaAPI.luaL_error(L, "Expected type " + type + ", but got " + (obj == null ? "null" : obj.GetType().ToString()) + ", while get field " + field);
                    }

                    translator.PushAny(L, field.GetValue(obj));
                    return 1;
                };
            }
        }
        static LuaCSFunction genFieldSetter(Type type, FieldInfo field)
        {
            if (field.IsStatic)
            {
                return (RealStatePtr L) =>
                {
                    ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
                    object val = translator.GetObject(L, 1, field.FieldType);
                    if (field.FieldType.IsValueType() && Nullable.GetUnderlyingType(field.FieldType) == null && val == null)
                    {
                        return LuaAPI.luaL_error(L, type.Name + "." + field.Name + " Expected type " + field.FieldType);
                    }
                    field.SetValue(null, val);
                    return 0;
                };
            }
            else
            {
                return (RealStatePtr L) =>
                {
                    ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);

                    object obj = translator.FastGetCSObj(L, 1);
                    if (obj == null || !type.IsInstanceOfType(obj))
                    {
                        return LuaAPI.luaL_error(L, "Expected type " + type + ", but got " + (obj == null ? "null" : obj.GetType().ToString()) + ", while set field " + field);
                    }

                    object val = translator.GetObject(L, 2, field.FieldType);
                    if (field.FieldType.IsValueType() && Nullable.GetUnderlyingType(field.FieldType) == null && val == null)
                    {
                        return LuaAPI.luaL_error(L, type.Name + "." + field.Name + " Expected type " + field.FieldType);
                    }
                    field.SetValue(obj, val);
                    if (type.IsValueType())
                    {
                        translator.Update(L, 1, obj);
                    }
                    return 0;
                };
            }
        }
        static LuaCSFunction genItemGetter(Type type, PropertyInfo[] props)
        {
            props = props.Where(prop => !prop.GetIndexParameters()[0].ParameterType.IsAssignableFrom(typeof(string))).ToArray();
            if (props.Length == 0)
            {
                return null;
            }
            Type[] params_type = new Type[props.Length];
            for (int i = 0; i < props.Length; i++)
            {
                params_type[i] = props[i].GetIndexParameters()[0].ParameterType;
            }
            object[] arg = new object[1] { null };
            return (RealStatePtr L) =>
            {
                ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
                object obj = translator.FastGetCSObj(L, 1);
                if (obj == null || !type.IsInstanceOfType(obj))
                {
                    return LuaAPI.luaL_error(L, "Expected type " + type + ", but got " + (obj == null ? "null" : obj.GetType().ToString()) + ", while get prop " + props[0].Name);
                }

                for (int i = 0; i < props.Length; i++)
                {
                    if (!translator.Assignable(L, 2, params_type[i]))
                    {
                        continue;
                    }
                    else
                    {
                        PropertyInfo prop = props[i];
                        try
                        {
                            object index = translator.GetObject(L, 2, params_type[i]);
                            arg[0] = index;
                            object ret = prop.GetValue(obj, arg);
                            LuaAPI.lua_pushboolean(L, true);
                            translator.PushAny(L, ret);
                            return 2;
                        }
                        catch (Exception e)
                        {
                            return LuaAPI.luaL_error(L, "try to get " + type + "." + prop.Name + " throw a exception:" + e + ",stack:" + e.StackTrace);
                        }
                    }
                }

                LuaAPI.lua_pushboolean(L, false);
                return 1;
            };
        }
        static LuaCSFunction genItemSetter(Type type, PropertyInfo[] props)
        {
            props = props.Where(prop => !prop.GetIndexParameters()[0].ParameterType.IsAssignableFrom(typeof(string))).ToArray();
            if (props.Length == 0)
            {
                return null;
            }
            Type[] params_type = new Type[props.Length];
            for (int i = 0; i < props.Length; i++)
            {
                params_type[i] = props[i].GetIndexParameters()[0].ParameterType;
            }
            object[] arg = new object[1] { null };
            return (RealStatePtr L) =>
            {
                ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
                object obj = translator.FastGetCSObj(L, 1);
                if (obj == null || !type.IsInstanceOfType(obj))
                {
                    return LuaAPI.luaL_error(L, "Expected type " + type + ", but got " + (obj == null ? "null" : obj.GetType().ToString()) + ", while set prop " + props[0].Name);
                }

                for (int i = 0; i < props.Length; i++)
                {
                    if (!translator.Assignable(L, 2, params_type[i]))
                    {
                        continue;
                    }
                    else
                    {
                        PropertyInfo prop = props[i];
                        try
                        {
                            arg[0] = translator.GetObject(L, 2, params_type[i]);
                            object val = translator.GetObject(L, 3, prop.PropertyType);
                            if (val == null)
                            {
                                return LuaAPI.luaL_error(L, type.Name + "." + prop.Name + " Expected type " + prop.PropertyType);
                            }
                            prop.SetValue(obj, val, arg);
                            LuaAPI.lua_pushboolean(L, true);

                            return 1;
                        }
                        catch (Exception e)
                        {
                            return LuaAPI.luaL_error(L, "try to set " + type + "." + prop.Name + " throw a exception:" + e + ",stack:" + e.StackTrace);
                        }
                    }
                }

                LuaAPI.lua_pushboolean(L, false);
                return 1;
            };
        }
        static LuaCSFunction genEnumCastFrom(Type type)
        {
            return (RealStatePtr L) =>
            {
                try
                {
                    ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
                    return translator.TranslateToEnumToTop(L, type, 1);
                }
                catch (Exception e)
                {
                    return LuaAPI.luaL_error(L, "cast to " + type + " exception:" + e);
                }
            };
        }

        internal static IEnumerable<MethodInfo> GetExtensionMethodsOf(Type type_to_be_extend)
		{
			if (InternalGlobals.extensionMethodMap == null)
			{
				List<Type> type_def_extention_method = new List<Type>();

				IEnumerator<Type> enumerator = GetAllTypes().GetEnumerator();

				while (enumerator.MoveNext())
				{
					Type type = enumerator.Current;
					if (type.IsDefined(typeof(ExtensionAttribute), false) && (type.IsDefined(typeof(ReflectionUseAttribute), false) || type.IsDefined(typeof(LuaCallCSharpAttribute), false)))
					{
						type_def_extention_method.Add(type);
					}

					if (!type.IsAbstract() || !type.IsSealed())
                        continue;

					var fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly);
					for (int i = 0; i < fields.Length; i++)
					{
						var field = fields[i];
						if ((field.IsDefined(typeof(ReflectionUseAttribute), false)
							|| field.IsDefined(typeof(LuaCallCSharpAttribute), false)
							) && (typeof(IEnumerable<Type>)).IsAssignableFrom(field.FieldType))
						{
							type_def_extention_method.AddRange((field.GetValue(null) as IEnumerable<Type>)
								.Where(t => t.IsDefined(typeof(ExtensionAttribute), false)));
						}
					}

					var props = type.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly);
					for (int i = 0; i < props.Length; i++)
					{
						var prop = props[i];
						if ((prop.IsDefined(typeof(ReflectionUseAttribute), false)
							|| prop.IsDefined(typeof(LuaCallCSharpAttribute), false)
							) && (typeof(IEnumerable<Type>)).IsAssignableFrom(prop.PropertyType))
						{
							type_def_extention_method.AddRange((prop.GetValue(null, null) as IEnumerable<Type>)
								.Where(t => t.IsDefined(typeof(ExtensionAttribute), false)));
						}
					}
				}
				enumerator.Dispose();

				InternalGlobals.extensionMethodMap = new Dictionary<Type, List<MethodInfo>>();
                foreach (var type in type_def_extention_method)
                {
                    foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public))
                    {
                        if (method.IsDefined(typeof(ExtensionAttribute), false) && IsSupportedMethod(method))
                        {
                            var extenedType = getExtendedType(method);
                            if (!InternalGlobals.extensionMethodMap.TryGetValue(extenedType, out var list))
                            {
                                list = new List<MethodInfo>();
                                InternalGlobals.extensionMethodMap.Add(extenedType, list);
                            }
                            list.Add(method);
                        }
                    }
                }
			}
			InternalGlobals.extensionMethodMap.TryGetValue(type_to_be_extend, out var ret);
			return ret;
		}

		struct MethodKey
		{
			public string Name;
			public bool IsStatic;
		}

		static void makeReflectionWrap(RealStatePtr L, Type type, int cls_field, int cls_getter, int cls_setter, int obj_field, int obj_getter, int obj_setter, int obj_meta, out LuaCSFunction item_getter, out LuaCSFunction item_setter)
		{
			ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
			BindingFlags flag = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;
			FieldInfo[] fields = type.GetFields(flag);
			EventInfo[] all_events = type.GetEvents(flag | BindingFlags.Public | BindingFlags.NonPublic);

            LuaAPI.lua_checkstack(L, 2);

            for (int i = 0; i < fields.Length; ++i)
			{
				FieldInfo field = fields[i];
				string fieldName = field.Name;

				if (all_events.Any(e => e.Name == fieldName))
				{
					fieldName = "&" + fieldName;
				}

				if (field.IsStatic && (field.IsInitOnly || field.IsLiteral))
				{
                    object constValue = type.IsEnum ? Convert.ToInt32(field.GetValue(null)) : field.GetValue(null);
                    LuaAPI.xlua_pushasciistring(L, fieldName);
                    translator.PushFixCSFunction(L, (RealStatePtr L) => {
                        translator.PushAny(L, constValue);
                        return 1;
                    });
                    LuaAPI.lua_rawset(L, cls_getter);

                    LuaAPI.xlua_pushasciistring(L, fieldName);
                    translator.PushFixCSFunction(L, (RealStatePtr L) => {
                        return LuaAPI.luaL_error(L, "Cannot modify a constant.");
                    });
                    LuaAPI.lua_rawset(L, cls_setter);
                }
				else
				{
					LuaAPI.xlua_pushasciistring(L, fieldName);
					translator.PushFixCSFunction(L, genFieldGetter(type, field));
					LuaAPI.lua_rawset(L, field.IsStatic ? cls_getter : obj_getter);

					LuaAPI.xlua_pushasciistring(L, fieldName);
					translator.PushFixCSFunction(L, genFieldSetter(type, field));
					LuaAPI.lua_rawset(L, field.IsStatic ? cls_setter : obj_setter);
				}
			}

			EventInfo[] events = type.GetEvents(flag);
			for (int i = 0; i < events.Length; ++i)
			{
				EventInfo eventInfo = events[i];
				LuaAPI.xlua_pushasciistring(L, $"add_{eventInfo.Name}");
				translator.PushFixCSFunction(L, translator.methodWrapsCache.GetAddEventWrap(type, eventInfo.Name));
				bool is_static = (eventInfo.GetAddMethod(true) != null) ? eventInfo.GetAddMethod(true).IsStatic : eventInfo.GetRemoveMethod(true).IsStatic;
				LuaAPI.lua_rawset(L, is_static ? cls_field : obj_field);

                LuaAPI.xlua_pushasciistring(L, $"remove_{eventInfo.Name}");
                translator.PushFixCSFunction(L, translator.methodWrapsCache.GetRemoveEventWrap(type, eventInfo.Name));
                is_static = (eventInfo.GetAddMethod(true) != null) ? eventInfo.GetAddMethod(true).IsStatic : eventInfo.GetRemoveMethod(true).IsStatic;
                LuaAPI.lua_rawset(L, is_static ? cls_field : obj_field);
            }

			List<PropertyInfo> items = new List<PropertyInfo>();
			PropertyInfo[] props = type.GetProperties(flag);
			for (int i = 0; i < props.Length; ++i)
			{
				PropertyInfo prop = props[i];
				if (prop.GetIndexParameters().Length > 0)
				{
					items.Add(prop);
				}
			}

			var item_array = items.ToArray();
			item_getter = item_array.Length > 0 ? genItemGetter(type, item_array) : null;
			item_setter = item_array.Length > 0 ? genItemSetter(type, item_array) : null;
			MethodInfo[] methods = type.GetMethods(flag);
			Dictionary<MethodKey, List<MemberInfo>> pending_methods = new Dictionary<MethodKey, List<MemberInfo>>();
			for (int i = 0; i < methods.Length; ++i)
			{
				MethodInfo method = methods[i];
				string method_name = method.Name;

				MethodKey method_key = new MethodKey { Name = method_name, IsStatic = method.IsStatic };
				List<MemberInfo> overloads;
				if (pending_methods.TryGetValue(method_key, out overloads))
				{
					overloads.Add(method);
					continue;
				}

				if (method_name.StartsWith("op_") && method.IsSpecialName) // 操作符
				{
					if (InternalGlobals.supportOp.ContainsKey(method_name))
					{
                        overloads = new List<MemberInfo>();
                        pending_methods.Add(method_key, overloads);
                        overloads.Add(method);
					}
					continue;
				}
				if (method_name.StartsWith("get_") && method.IsSpecialName && method.GetParameters().Length != 1) // getter of property
				{
					string prop_name = method.Name.Substring(4);
					LuaAPI.xlua_pushasciistring(L, prop_name);
					translator.PushFixCSFunction(L, translator.methodWrapsCache._GenMethodWrap(method.DeclaringType, prop_name, new MethodBase[] { method }, false, L).Call);
					LuaAPI.lua_rawset(L, method.IsStatic ? cls_getter : obj_getter);
                    continue;
				}
				if (method_name.StartsWith("set_") && method.IsSpecialName && method.GetParameters().Length != 2) // setter of property
				{
					string prop_name = method.Name.Substring(4);
					LuaAPI.xlua_pushasciistring(L, prop_name);
					translator.PushFixCSFunction(L, translator.methodWrapsCache._GenMethodWrap(method.DeclaringType, prop_name, new MethodBase[] { method }, false, L).Call);
					LuaAPI.lua_rawset(L, method.IsStatic ? cls_setter : obj_setter);
                    continue;
				}
				if (method_name == ".ctor" && method.IsConstructor)
				{
					continue;
				}
                overloads = new List<MemberInfo>();
                pending_methods.Add(method_key, overloads);
                overloads.Add(method);
            }

			IEnumerable<MethodInfo> extend_methods = GetExtensionMethodsOf(type);
			if (extend_methods != null)
			{
				foreach (var extend_method in extend_methods)
				{
					MethodKey method_key = new MethodKey { Name = extend_method.Name, IsStatic = false };
					List<MemberInfo> overloads;
					if (pending_methods.TryGetValue(method_key, out overloads))
					{
						overloads.Add(extend_method);
						continue;
					}
                    overloads = new List<MemberInfo>() { extend_method };
                    pending_methods.Add(method_key, overloads);
                }
			}

			foreach (var kv in pending_methods)
			{
				if (kv.Key.Name.StartsWith("op_")) // 操作符
				{
					LuaAPI.xlua_pushasciistring(L, InternalGlobals.supportOp[kv.Key.Name]);
					translator.PushFixCSFunction(L, new LuaCSFunction(translator.methodWrapsCache._GenMethodWrap(type, kv.Key.Name, kv.Value.ToArray(), false, L).Call));
					LuaAPI.lua_rawset(L, obj_meta);
				}
				else
				{
					LuaAPI.xlua_pushasciistring(L, kv.Key.Name);
					translator.PushFixCSFunction(L,
						new LuaCSFunction(translator.methodWrapsCache._GenMethodWrap(type, kv.Key.Name, kv.Value.ToArray(), false, L).Call));
					LuaAPI.lua_rawset(L, kv.Key.IsStatic ? cls_field : obj_field);
				}
			}
		}

		public static void loadUpvalue(RealStatePtr L, Type type, string metafunc, int index)
		{
			ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
			LuaAPI.xlua_pushasciistring(L, metafunc);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			translator.Push(L, type);
			LuaAPI.lua_rawget(L, -2);
			LuaAPI.lua_remove(L, -2);
			for (int i = 1; i <= index; i++)
			{
				LuaAPI.lua_getupvalue(L, -i, i);
				if (LuaAPI.lua_isnil(L, -1))
				{
					LuaAPI.lua_pop(L, 1);
					LuaAPI.lua_newtable(L);
					LuaAPI.lua_pushvalue(L, -1);
					LuaAPI.lua_setupvalue(L, -i - 2, i);
				}
			}
			for (int i = 0; i < index; i++)
			{
				LuaAPI.lua_remove(L, -2);
			}
		}

		public static void ReflectionWrap(RealStatePtr L, Type type)
		{
            LuaAPI.lua_checkstack(L, 20);

            int top_enter = LuaAPI.lua_gettop(L);
			ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
			//create obj meta table
			LuaAPI.luaL_getmetatable(L, type.FullName);
			if (LuaAPI.lua_isnil(L, -1))
			{
				LuaAPI.lua_pop(L, 1);
				LuaAPI.luaL_newmetatable(L, type.FullName);
			}
			LuaAPI.lua_pushlightuserdata(L, LuaAPI.xlua_tag());
			LuaAPI.lua_pushnumber(L, 1);
			LuaAPI.lua_rawset(L, -3);
			int obj_meta = LuaAPI.lua_gettop(L);

			LuaAPI.lua_newtable(L);
			int cls_meta = LuaAPI.lua_gettop(L);

			LuaAPI.lua_newtable(L);
			int obj_field = LuaAPI.lua_gettop(L);
			LuaAPI.lua_newtable(L);
			int obj_getter = LuaAPI.lua_gettop(L);
			LuaAPI.lua_newtable(L);
			int obj_setter = LuaAPI.lua_gettop(L);
			LuaAPI.lua_newtable(L);
			int cls_field = LuaAPI.lua_gettop(L);
            //set cls_field to namespace
            SetCSTable(L, type, cls_field);
            //finish set cls_field to namespace
            LuaAPI.lua_newtable(L);
			int cls_getter = LuaAPI.lua_gettop(L);
			LuaAPI.lua_newtable(L);
			int cls_setter = LuaAPI.lua_gettop(L);

            LuaCSFunction item_getter;
			LuaCSFunction item_setter;
			makeReflectionWrap(L, type, cls_field, cls_getter, cls_setter, obj_field, obj_getter, obj_setter, obj_meta, out item_getter, out item_setter);

			// init obj metatable
			LuaAPI.xlua_pushasciistring(L, "__gc");
			LuaAPI.lua_pushstdcallcfunction(L, translator.metaFunctions.GcMeta);
			LuaAPI.lua_rawset(L, obj_meta);

			LuaAPI.xlua_pushasciistring(L, "__tostring");
			LuaAPI.lua_pushstdcallcfunction(L, translator.metaFunctions.ToStringMeta);
			LuaAPI.lua_rawset(L, obj_meta);

			LuaAPI.xlua_pushasciistring(L, "__index");
			LuaAPI.lua_pushvalue(L, obj_field);
			LuaAPI.lua_pushvalue(L, obj_getter);
			translator.PushFixCSFunction(L, item_getter);
            if (type.BaseType() == typeof(Array))
            {
                LuaAPI.xlua_rawgeti(L, LuaIndexes.LUA_REGISTRYINDEX, translator.common_array_meta);
            }
            else
            {
                translator.PushAny(L, type.BaseType());
            }

			LuaAPI.xlua_pushasciistring(L, LuaIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			LuaAPI.lua_pushnil(L);
			LuaAPI.gen_obj_indexer(L);
			//store in lua indexs function tables
			LuaAPI.xlua_pushasciistring(L, LuaIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			translator.Push(L, type);
			LuaAPI.lua_pushvalue(L, -3);
			LuaAPI.lua_rawset(L, -3);
			LuaAPI.lua_pop(L, 1);
			LuaAPI.lua_rawset(L, obj_meta); // set __index

			LuaAPI.xlua_pushasciistring(L, "__newindex");
			LuaAPI.lua_pushvalue(L, obj_setter);
			translator.PushFixCSFunction(L, item_setter);
            if (type.BaseType() == typeof(Array))
            {
                LuaAPI.xlua_rawgeti(L, LuaIndexes.LUA_REGISTRYINDEX, translator.common_array_meta);
            }
            else
            {
                translator.Push(L, type.BaseType());
            }

			LuaAPI.xlua_pushasciistring(L, LuaNewIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			LuaAPI.lua_pushnil(L);
			LuaAPI.gen_obj_newindexer(L);
			//store in lua newindexs function tables
			LuaAPI.xlua_pushasciistring(L, LuaNewIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			translator.Push(L, type);
			LuaAPI.lua_pushvalue(L, -3);
			LuaAPI.lua_rawset(L, -3);
			LuaAPI.lua_pop(L, 1);
			LuaAPI.lua_rawset(L, obj_meta); // set __newindex
											//finish init obj metatable

			LuaAPI.xlua_pushasciistring(L, "UnderlyingSystemType");
			translator.PushAny(L, type);
			LuaAPI.lua_rawset(L, cls_field);

			//init class meta
			LuaAPI.xlua_pushasciistring(L, "__index");
			LuaAPI.lua_pushvalue(L, cls_getter);
			LuaAPI.lua_pushvalue(L, cls_field);
			translator.Push(L, type.BaseType());
			LuaAPI.xlua_pushasciistring(L, LuaClassIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			LuaAPI.gen_cls_indexer(L);
			//store in lua indexs function tables
			LuaAPI.xlua_pushasciistring(L, LuaClassIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			translator.Push(L, type);
			LuaAPI.lua_pushvalue(L, -3);
			LuaAPI.lua_rawset(L, -3);
			LuaAPI.lua_pop(L, 1);
			LuaAPI.lua_rawset(L, cls_meta); // set __index

			LuaAPI.xlua_pushasciistring(L, "__newindex");
			LuaAPI.lua_pushvalue(L, cls_setter);
			translator.Push(L, type.BaseType());
			LuaAPI.xlua_pushasciistring(L, LuaClassNewIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			LuaAPI.gen_cls_newindexer(L);
			//store in lua newindexs function tables
			LuaAPI.xlua_pushasciistring(L, LuaClassNewIndexsFieldName);
			LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
			translator.Push(L, type);
			LuaAPI.lua_pushvalue(L, -3);
			LuaAPI.lua_rawset(L, -3);
			LuaAPI.lua_pop(L, 1);
			LuaAPI.lua_rawset(L, cls_meta); // set __newindex

			LuaCSFunction constructor = typeof(Delegate).IsAssignableFrom(type) ? translator.metaFunctions.DelegateCtor : translator.methodWrapsCache.GetConstructorWrap(type, L);
			if (constructor == null)
			{
				constructor = (RealStatePtr LL) =>
				{
					return LuaAPI.luaL_error(LL, "No constructor for " + type);
				};
			}

			LuaAPI.xlua_pushasciistring(L, "__call");
			translator.PushFixCSFunction(L, constructor);
			LuaAPI.lua_rawset(L, cls_meta);

			LuaAPI.lua_pushvalue(L, cls_meta);
			LuaAPI.lua_setmetatable(L, cls_field);

			LuaAPI.lua_pop(L, 8);

			System.Diagnostics.Debug.Assert(top_enter == LuaAPI.lua_gettop(L));
		}

        //meta: -4, method:-3, getter: -2, setter: -1
        public static void BeginObjectRegister(Type type, RealStatePtr L, ObjectTranslator translator, int meta_count, int method_count, int getter_count,
            int setter_count, int type_id = -1)
        {
            if (type == null)
            {
                if (type_id == -1) throw new Exception("Fatal: must provide a type of type_id");
                LuaAPI.xlua_rawgeti(L, LuaIndexes.LUA_REGISTRYINDEX, type_id);
            }
            else
            {
                LuaAPI.luaL_getmetatable(L, type.FullName);
                if (LuaAPI.lua_isnil(L, -1))
                {
                    LuaAPI.lua_pop(L, 1);
                    LuaAPI.luaL_newmetatable(L, type.FullName);
                }
            }
            LuaAPI.lua_pushlightuserdata(L, LuaAPI.xlua_tag());
            LuaAPI.lua_pushnumber(L, 1);
            LuaAPI.lua_rawset(L, -3);

            if ((type == null || !translator.HasCustomOp(type)) && type != typeof(decimal))
            {
                LuaAPI.xlua_pushasciistring(L, "__gc");
                LuaAPI.lua_pushstdcallcfunction(L, translator.metaFunctions.GcMeta);
                LuaAPI.lua_rawset(L, -3);
            }

            LuaAPI.xlua_pushasciistring(L, "__tostring");
            LuaAPI.lua_pushstdcallcfunction(L, translator.metaFunctions.ToStringMeta);
            LuaAPI.lua_rawset(L, -3);

            if (method_count == 0)
            {
                LuaAPI.lua_pushnil(L);
            }
            else
            {
                LuaAPI.lua_createtable(L, 0, method_count);
            }

            if (getter_count == 0)
            {
                LuaAPI.lua_pushnil(L);
            }
            else
            {
                LuaAPI.lua_createtable(L, 0, getter_count);
            }

            if (setter_count == 0)
            {
                LuaAPI.lua_pushnil(L);
            }
            else
            {
                LuaAPI.lua_createtable(L, 0, setter_count);
            }
        }
#endif

        static int abs_idx(int top, int idx)
		{
			return idx > 0 ? idx : top + idx + 1;
		}

		public const int OBJ_META_IDX = -4;
		public const int METHOD_IDX = -3;
		public const int GETTER_IDX = -2;
		public const int SETTER_IDX = -1;
#if !XLUA_IL2CPP || !ENABLE_IL2CPP
        public static void EndObjectRegister(Type type, RealStatePtr L, ObjectTranslator translator, LuaCSFunction csIndexer,
            LuaCSFunction csNewIndexer, Type base_type, LuaCSFunction arrayIndexer, LuaCSFunction arrayNewIndexer)
        {
            int top = LuaAPI.lua_gettop(L);
            int meta_idx = abs_idx(top, OBJ_META_IDX);
            int method_idx = abs_idx(top, METHOD_IDX);
            int getter_idx = abs_idx(top, GETTER_IDX);
            int setter_idx = abs_idx(top, SETTER_IDX);

            //begin index gen
            LuaAPI.xlua_pushasciistring(L, "__index");
            LuaAPI.lua_pushvalue(L, method_idx);
            LuaAPI.lua_pushvalue(L, getter_idx);

            if (csIndexer == null)
            {
                LuaAPI.lua_pushnil(L);
            }
            else
            {
                LuaAPI.lua_pushstdcallcfunction(L, csIndexer);
            }

            translator.Push(L, type == null ? base_type : type.BaseType());

            LuaAPI.xlua_pushasciistring(L, LuaIndexsFieldName);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
            if (arrayIndexer == null)
            {
                LuaAPI.lua_pushnil(L);
            }
            else
            {
                LuaAPI.lua_pushstdcallcfunction(L, arrayIndexer);
            }

            LuaAPI.gen_obj_indexer(L);

            if (type != null)
            {
                LuaAPI.xlua_pushasciistring(L, LuaIndexsFieldName);
                LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);//store in lua indexs function tables
                translator.Push(L, type);
                LuaAPI.lua_pushvalue(L, -3);
                LuaAPI.lua_rawset(L, -3);
                LuaAPI.lua_pop(L, 1);
            }

            LuaAPI.lua_rawset(L, meta_idx);
            //end index gen

            //begin newindex gen
            LuaAPI.xlua_pushasciistring(L, "__newindex");
            LuaAPI.lua_pushvalue(L, setter_idx);

            if (csNewIndexer == null)
            {
                LuaAPI.lua_pushnil(L);
            }
            else
            {
                LuaAPI.lua_pushstdcallcfunction(L, csNewIndexer);
            }

            translator.Push(L, type == null ? base_type : type.BaseType());

            LuaAPI.xlua_pushasciistring(L, LuaNewIndexsFieldName);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);

            if (arrayNewIndexer == null)
            {
                LuaAPI.lua_pushnil(L);
            }
            else
            {
                LuaAPI.lua_pushstdcallcfunction(L, arrayNewIndexer);
            }

            LuaAPI.gen_obj_newindexer(L);

            if (type != null)
            {
                LuaAPI.xlua_pushasciistring(L, LuaNewIndexsFieldName);
                LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);//store in lua newindexs function tables
                translator.Push(L, type);
                LuaAPI.lua_pushvalue(L, -3);
                LuaAPI.lua_rawset(L, -3);
                LuaAPI.lua_pop(L, 1);
            }

            LuaAPI.lua_rawset(L, meta_idx);
            //end new index gen
            LuaAPI.lua_pop(L, 4);
        }
#endif

        public static void RegisterFunc(RealStatePtr L, int idx, string name, LuaCSFunction func)
		{
			idx = abs_idx(LuaAPI.lua_gettop(L), idx);
			LuaAPI.xlua_pushasciistring(L, name);
			LuaAPI.lua_pushstdcallcfunction(L, func);
			LuaAPI.lua_rawset(L, idx);
		}

        public static void RegisterRefFunc(RealStatePtr L, int idx, string name, int valueRef)
        {
            LuaAPI.lua_checkstack(L, 2);
            idx = abs_idx(LuaAPI.lua_gettop(L), idx);
            LuaAPI.xlua_pushasciistring(L, name);
            LuaAPI.lua_getref(L, valueRef);
            LuaAPI.lua_rawset(L, idx);
        }
#if !XLUA_IL2CPP || !ENABLE_IL2CPP
        public static void RegisterObject(RealStatePtr L, ObjectTranslator translator, int idx, string name, object obj)
        {
            idx = abs_idx(LuaAPI.lua_gettop(L), idx);
            LuaAPI.xlua_pushasciistring(L, name);
            translator.PushAny(L, obj);
            LuaAPI.lua_rawset(L, idx);
        }

        public static void BeginClassRegister(Type type, RealStatePtr L, LuaCSFunction creator, int class_field_count,
            int static_getter_count, int static_setter_count)
        {
            ObjectTranslator translator = ObjectTranslatorPool.Instance.Find(L);
            LuaAPI.lua_createtable(L, 0, class_field_count);

            LuaAPI.xlua_pushasciistring(L, "UnderlyingSystemType");
            translator.PushAny(L, type);
            LuaAPI.lua_rawset(L, -3);

            int cls_table = LuaAPI.lua_gettop(L);

            SetCSTable(L, type, cls_table);

            LuaAPI.lua_createtable(L, 0, 3);
            int meta_table = LuaAPI.lua_gettop(L);
            if (creator != null)
            {
                LuaAPI.xlua_pushasciistring(L, "__call");
                LuaAPI.lua_pushstdcallcfunction(L, creator);
                LuaAPI.lua_rawset(L, -3);
            }

            if (static_getter_count == 0)
            {
                LuaAPI.lua_pushnil(L);
            }
            else
            {
                LuaAPI.lua_createtable(L, 0, static_getter_count);
            }

            if (static_setter_count == 0)
            {
                LuaAPI.lua_pushnil(L);
            }
            else
            {
                LuaAPI.lua_createtable(L, 0, static_setter_count);
            }
            LuaAPI.lua_pushvalue(L, meta_table);
            LuaAPI.lua_setmetatable(L, cls_table);
        }

        public const int CLS_IDX = -4;
        public const int CLS_META_IDX = -3;
        public const int CLS_GETTER_IDX = -2;
        public const int CLS_SETTER_IDX = -1;

        public static void EndClassRegister(Type type, RealStatePtr L, ObjectTranslator translator)
        {
            int top = LuaAPI.lua_gettop(L);
            int cls_idx = abs_idx(top, CLS_IDX);
            int cls_getter_idx = abs_idx(top, CLS_GETTER_IDX);
            int cls_setter_idx = abs_idx(top, CLS_SETTER_IDX);
            int cls_meta_idx = abs_idx(top, CLS_META_IDX);

            //begin cls index
            LuaAPI.xlua_pushasciistring(L, "__index");
            LuaAPI.lua_pushvalue(L, cls_getter_idx);
            LuaAPI.lua_pushvalue(L, cls_idx);
            translator.Push(L, type.BaseType());
            LuaAPI.xlua_pushasciistring(L, LuaClassIndexsFieldName);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
            LuaAPI.gen_cls_indexer(L);

            LuaAPI.xlua_pushasciistring(L, LuaClassIndexsFieldName);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);//store in lua indexs function tables
            translator.Push(L, type);
            LuaAPI.lua_pushvalue(L, -3);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.lua_pop(L, 1);

            LuaAPI.lua_rawset(L, cls_meta_idx);
            //end cls index

            //begin cls newindex
            LuaAPI.xlua_pushasciistring(L, "__newindex");
            LuaAPI.lua_pushvalue(L, cls_setter_idx);
            translator.Push(L, type.BaseType());
            LuaAPI.xlua_pushasciistring(L, LuaClassNewIndexsFieldName);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
            LuaAPI.gen_cls_newindexer(L);

            LuaAPI.xlua_pushasciistring(L, LuaClassNewIndexsFieldName);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);//store in lua newindexs function tables
            translator.Push(L, type);
            LuaAPI.lua_pushvalue(L, -3);
            LuaAPI.lua_rawset(L, -3);
            LuaAPI.lua_pop(L, 1);

            LuaAPI.lua_rawset(L, cls_meta_idx);
            //end cls newindex

            LuaAPI.lua_pop(L, 4);
        }


        static List<string> getPathOfType(Type type)
		{
			List<string> path = new List<string>();

			if (type.Namespace != null)
			{
				path.AddRange(type.Namespace.Split(new char[] { '.' }));
			}

			string class_name = type.ToString().Substring(type.Namespace == null ? 0 : type.Namespace.Length + 1);

			if (type.IsNested)
			{
				path.AddRange(class_name.Split(new char[] { '+' }));
			}
			else
			{
				path.Add(class_name);
			}
			return path;
		}

		public static void LoadCSTable(RealStatePtr L, Type type)
		{
			int oldTop = LuaAPI.lua_gettop(L);
            LuaAPI.xlua_pushasciistring(L, LuaEnv.CSHARP_NAMESPACE);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);

            List<string> path = getPathOfType(type);

			for (int i = 0; i < path.Count; ++i)
			{
				LuaAPI.xlua_pushasciistring(L, path[i]);
				if (0 != LuaAPI.xlua_pgettable(L, -2))
				{
					LuaAPI.lua_settop(L, oldTop);
					LuaAPI.lua_pushnil(L);
					return;
				}
				if (!LuaAPI.lua_istable(L, -1) && i < path.Count - 1)
				{
					LuaAPI.lua_settop(L, oldTop);
					LuaAPI.lua_pushnil(L);
					return;
				}
				LuaAPI.lua_remove(L, -2);
			}
		}

		public static void SetCSTable(RealStatePtr L, Type type, int cls_table)
		{
			int oldTop = LuaAPI.lua_gettop(L);
			cls_table = abs_idx(oldTop, cls_table);
            LuaAPI.xlua_pushasciistring(L, LuaEnv.CSHARP_NAMESPACE);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);

            List<string> path = getPathOfType(type);

			for (int i = 0; i < path.Count - 1; ++i)
			{
				LuaAPI.xlua_pushasciistring(L, path[i]);
				if (0 != LuaAPI.xlua_pgettable(L, -2))
				{
					var err = LuaAPI.lua_tostring(L, -1);
					LuaAPI.lua_settop(L, oldTop);
					throw new Exception("SetCSTable for [" + type + "] error: " + err);
				}
				if (LuaAPI.lua_isnil(L, -1))
				{
					LuaAPI.lua_pop(L, 1);
					LuaAPI.lua_createtable(L, 0, 0);
					LuaAPI.xlua_pushasciistring(L, path[i]);
					LuaAPI.lua_pushvalue(L, -2);
					LuaAPI.lua_rawset(L, -4);
				}
				else if (!LuaAPI.lua_istable(L, -1))
				{
					LuaAPI.lua_settop(L, oldTop);
					throw new Exception("SetCSTable for [" + type + "] error: ancestors is not a table!");
				}
				LuaAPI.lua_remove(L, -2);
			}

			LuaAPI.xlua_pushasciistring(L, path[path.Count - 1]);
			LuaAPI.lua_pushvalue(L, cls_table);
			LuaAPI.lua_rawset(L, -3);
			LuaAPI.lua_pop(L, 1);

            LuaAPI.xlua_pushasciistring(L, LuaEnv.CSHARP_NAMESPACE);
            LuaAPI.lua_rawget(L, LuaIndexes.LUA_REGISTRYINDEX);
            ObjectTranslatorPool.Instance.Find(L).PushAny(L, type);
			LuaAPI.lua_pushvalue(L, cls_table);
			LuaAPI.lua_rawset(L, -3);
			LuaAPI.lua_pop(L, 1);
		}
#endif
        public const string LuaIndexsFieldName = "LuaIndexs";

		public const string LuaNewIndexsFieldName = "LuaNewIndexs";

		public const string LuaClassIndexsFieldName = "LuaClassIndexs";

		public const string LuaClassNewIndexsFieldName = "LuaClassNewIndexs";

		public static bool IsParamsMatch(MethodInfo delegateMethod, MethodInfo bridgeMethod)
		{
			if (delegateMethod == null || bridgeMethod == null)
			{
				return false;
			}
			if (delegateMethod.ReturnType != bridgeMethod.ReturnType)
			{
				return false;
			}
			ParameterInfo[] delegateParams = delegateMethod.GetParameters();
			ParameterInfo[] bridgeParams = bridgeMethod.GetParameters();
			if (delegateParams.Length != bridgeParams.Length)
			{
				return false;
			}

			for (int i = 0; i < delegateParams.Length; i++)
			{
				if (delegateParams[i].ParameterType != bridgeParams[i].ParameterType || delegateParams[i].IsOut != bridgeParams[i].IsOut)
				{
					return false;
				}
			}

            var lastPos = delegateParams.Length - 1;
            return lastPos < 0 || delegateParams[lastPos].IsDefined(typeof(ParamArrayAttribute), false) == bridgeParams[lastPos].IsDefined(typeof(ParamArrayAttribute), false);
		}

		public static MethodInfo MakeGenericMethodWithConstraints(MethodInfo method)
		{
			try
			{
				var genericArguments = method.GetGenericArguments();
				var constraintedArgumentTypes = new Type[genericArguments.Length];
				for (var i = 0; i < genericArguments.Length; i++)
				{
					var argumentType = genericArguments[i];
					var parameterConstraints = argumentType.GetGenericParameterConstraints();
					constraintedArgumentTypes[i] = parameterConstraints[0];
				}
				return method.MakeGenericMethod(constraintedArgumentTypes);
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static Type getExtendedType(MethodInfo method)
		{
			var type = method.GetParameters()[0].ParameterType;
			if (!type.IsGenericParameter)
				return type;
			var parameterConstraints = type.GetGenericParameterConstraints();
			if (parameterConstraints.Length == 0)
				throw new InvalidOperationException();

			var firstParameterConstraint = parameterConstraints[0];
			if (!firstParameterConstraint.IsClass())
				throw new InvalidOperationException();
			return firstParameterConstraint;
		}
        public static bool IsStaticPInvokeCSFunction(LuaCSFunction csFunction)
		{
#if UNITY_WSA && !UNITY_EDITOR
            return csFunction.GetMethodInfo().IsStatic && csFunction.GetMethodInfo().GetCustomAttribute<MonoPInvokeCallbackAttribute>() != null;
#else
			return csFunction.Method.IsStatic && Attribute.IsDefined(csFunction.Method, typeof(MonoPInvokeCallbackAttribute));
#endif
		}

		public static bool IsPublic(Type type)
		{
			if (type.IsNested)
			{
				if (!type.IsNestedPublic()) return false;
				return IsPublic(type.DeclaringType);
			}
			if (type.IsGenericType())
			{
				var gas = type.GetGenericArguments();
				for (int i = 0; i < gas.Length; i++)
				{
					if (!IsPublic(gas[i]))
					{
						return false;
					}
				}
			}
			return type.IsPublic();
		}

        public static object ConvertToEnum(Type enumType, long longValue)
        {
            if (!enumType.IsEnum)
            {
                throw new ArgumentException("Type must be an enum.");
            }

            // 获取枚举的基础类型
            Type underlyingType = Enum.GetUnderlyingType(enumType);

            // 将 long 值转换为枚举的基础类型
            object value = Convert.ChangeType(longValue, underlyingType);

            // 检查值是否在枚举定义的范围内
            if (Enum.IsDefined(enumType, value))
            {
                return Enum.ToObject(enumType, value);
            }
            else
            {
                return null;
            }
        }
    }
}

