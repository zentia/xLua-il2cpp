using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace XLua
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class LuaEvalAttribute : Attribute
    {
        public string expression;

        public LuaEvalAttribute(string expression) { this.expression = expression; }

        public class Entry
        {
            public MemberInfo member;
            public string expression;
        }

        private static IEnumerable<Entry> BindableFields(object target)
        {
            foreach(var memberInfo in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                // UnityEngine.Debug.LogError($"Start LuaEvalAttribute.BindableFields - target: {target.GetType().Name} memberInfo: {memberInfo.Name}");

                var attribute = memberInfo.GetCustomAttribute<LuaEvalAttribute>(false);
                if(attribute != null)
                {
                    if(memberInfo.FieldType.IsSubclassOf(typeof(Delegate)))
                    {
                        yield return new Entry() {member = memberInfo, expression = attribute.expression};
                    }
                }
            }
        }

        public static void Bind(object target, LuaEnv L, LuaTable env = null)
        {
            env ??= L.Global;

#if OSG_PROFILE
            string targetTypeName = target.GetType().Name;
#endif

            foreach(var entry in BindableFields(target))
            {
                try
                {
                    var fieldInfo = entry.member as FieldInfo;
                    if(fieldInfo != null)
                    {
#if OSG_PROFILE
                        string chunk = "return function (...)\n"
                                       + $"   local perfName = '[cs - {targetTypeName}.{fieldInfo.Name}][lua - {entry.expression}]'\n"
                                       + "    local sampleIndex = StatsService:BeginSampleInternal(StatsSampleId.Lua_CSCallLua, perfName)\n"
                                       + $"   local re = {entry.expression}(...)\n"
                                       + "    StatsService:EndSampleByIndex(sampleIndex)\n"
                                       + "    return re\n"
                                       + "end";

                        // 不存在多个返回值的支持了.
                        fieldInfo.SetValue(target, L.DoString( chunk, fieldInfo.FieldType, fieldInfo.Name, env));
#else
                        fieldInfo.SetValue(target, L.DoString("return " + entry.expression, fieldInfo.FieldType, fieldInfo.Name, env));
#endif
                    }
                }
                catch(Exception e)
                {
                    osgame_log.error(osgame_log.cat.Lua, "Evaluation failed:{}\n{}", entry.expression, e.Message);
                }
            }
        }

        public static void Unbind(object target)
        {
            foreach(var entry in BindableFields(target))
            {
                var fieldInfo = entry.member as FieldInfo;
                if(fieldInfo != null)
                {
                    fieldInfo.SetValue(target, null);
                }

                var propertyInfo = entry.member as PropertyInfo;
                if(propertyInfo != null)
                {
                    propertyInfo.SetValue(target, null, null);
                }
            }
        }
    }
}
