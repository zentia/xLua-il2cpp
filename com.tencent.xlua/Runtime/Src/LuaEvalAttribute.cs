using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace XLua
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class LuaEvalAttribute : Attribute
    {
        public string expression;

        public LuaEvalAttribute(string expression)
        {
            this.expression = expression;
        }

        public class Entry
        {
            public MemberInfo member;
            public string expression;
        }

        private static IEnumerable<Entry> BindableFields(object target)
        {
            foreach (var memberInfo in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                // UnityEngine.Debug.LogError($"Start LuaEvalAttribute.BindableFields - target: {target.GetType().Name} memberInfo: {memberInfo.Name}");

                var attribute = memberInfo.GetCustomAttribute<LuaEvalAttribute>(false);
                if (attribute != null)
                {
                    if (memberInfo.FieldType.IsSubclassOf(typeof(Delegate)))
                    {
                        yield return new Entry() { member = memberInfo, expression = attribute.expression };
                    }
                }
            }
        }

        public static void Bind(object target, LuaEnv L, LuaTable env = null)
        {
            env ??= L.Global;

            foreach (var entry in BindableFields(target))
            {
                try
                {
                    var fieldInfo = entry.member as FieldInfo;
                    if (fieldInfo != null)
                    {
#if OSG_PROFILE
                        string targetName = target != null ? target.GetType().Name : "null";
                        string funcName = "";
                        funcName = targetName;
                        if (fieldInfo != null)
                            funcName += $".{fieldInfo.Name}";
                        if (propertyInfo != null)
                            funcName += $".{propertyInfo.Name}";

                        string beginSample = "";//"$"local CSInteraction = CS.LuaCallCSharpInteraction\n"
                                                //+ $"local sampleIndex = CSInteraction.LuaBeginSample(CS.Assets.Plugins.Perf.StatsSampleId.CSharpCallCSharp, \"{funcName}\")";
                        string endSample = "";// "$"CSStats.EndSampleByIndex(sampleIndex)";
                        fieldInfo.SetValue(target, L.DoString($"return function(...)\n{beginSample}\nlocal ret = {entry.expression}(...)\n{endSample}\nreturn ret\nend\n", fieldInfo.FieldType,target.ToString(), env));
#else
                        fieldInfo.SetValue(target, L.DoString("return " + entry.expression, fieldInfo.FieldType, fieldInfo.Name, env));
#endif
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarningFormat("Evaluation failed:{0}\n{1}", entry.expression,e);
                }
            }
        }

        public static void Unbind(object target)
        {
            foreach (var entry in BindableFields(target))
            {
                var fieldInfo = entry.member as FieldInfo;
                if (fieldInfo != null)
                {
                    fieldInfo.SetValue(target, null);
                }

                var propertyInfo = entry.member as PropertyInfo;
                if (propertyInfo != null)
                {
                    propertyInfo.SetValue(target, null, null);
                }
            }
        }
    }
}
