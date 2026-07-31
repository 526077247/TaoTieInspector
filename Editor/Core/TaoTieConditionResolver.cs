using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace TaoTie.Inspector.Editor
{
    public static class TaoTieConditionResolver
    {
        private static readonly Dictionary<(Type, string), MemberInfo> memberCache = new();

        public static MemberInfo GetMember(object target, string memberName)
        {
            if (target == null || string.IsNullOrEmpty(memberName)) return null;

            Type type = target.GetType();
            var key = (type, memberName);

            if (memberCache.TryGetValue(key, out MemberInfo cached))
            {
                if (cached != null) return cached;
                return null;
            }

            MemberInfo result = null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            result = (MemberInfo)type.GetField(memberName, flags)
                     ?? (MemberInfo)type.GetProperty(memberName, flags)
                     ?? (MemberInfo)type.GetMethod(memberName, flags);

            memberCache[key] = result;
            return result;
        }

        public static bool Evaluate(object target, string memberName)
        {
            if (TaoTieExpressionEvaluator.IsExpression(memberName))
                return TaoTieExpressionEvaluator.Evaluate(memberName, target);

            MemberInfo member = GetMember(target, memberName);
            if (member == null) return true;

            return member switch
            {
                FieldInfo fi => ToBool(fi.GetValue(target)),
                PropertyInfo pi => ToBool(pi.GetValue(target, null)),
                MethodInfo mi => mi.ReturnType == typeof(bool) && mi.GetParameters().Length == 0
                    ? (bool)mi.Invoke(target, null)
                    : false,
                _ => true
            };
        }

        public static bool EvaluateEquals(object target, string memberName, object expectedValue)
        {
            MemberInfo member = GetMember(target, memberName);
            if (member == null) return true;

            object actualValue = member switch
            {
                FieldInfo fi => fi.GetValue(target),
                PropertyInfo pi => pi.GetValue(target, null),
                _ => expectedValue
            };

            if (actualValue == null && expectedValue == null) return true;
            if (actualValue == null || expectedValue == null) return false;

            if (expectedValue is Enum && actualValue is Enum)
                return Convert.ToInt64(actualValue) == Convert.ToInt64(expectedValue);

            return actualValue.Equals(expectedValue);
        }

        public static bool EvaluateShowIf(ShowIfAttribute attr, object target)
        {
            if (attr.Value == null && TaoTieExpressionEvaluator.IsExpression(attr.Condition))
                return TaoTieExpressionEvaluator.Evaluate(attr.Condition, target);
            if (attr.Value != null)
                return EvaluateEquals(target, attr.Condition, attr.Value);
            return Evaluate(target, attr.Condition);
        }

        public static bool EvaluateHideIf(HideIfAttribute attr, object target)
        {
            if (attr.Value == null && TaoTieExpressionEvaluator.IsExpression(attr.Condition))
                return TaoTieExpressionEvaluator.Evaluate(attr.Condition, target);
            if (attr.Value != null)
                return EvaluateEquals(target, attr.Condition, attr.Value);
            return Evaluate(target, attr.Condition);
        }

        public static bool EvaluateEnableIf(EnableIfAttribute attr, object target)
        {
            if (attr.Value == null && TaoTieExpressionEvaluator.IsExpression(attr.Condition))
                return TaoTieExpressionEvaluator.Evaluate(attr.Condition, target);
            if (attr.Value != null)
                return EvaluateEquals(target, attr.Condition, attr.Value);
            return Evaluate(target, attr.Condition);
        }

        public static bool EvaluateDisableIf(DisableIfAttribute attr, object target)
        {
            if (attr.Value == null && TaoTieExpressionEvaluator.IsExpression(attr.Condition))
                return TaoTieExpressionEvaluator.Evaluate(attr.Condition, target);
            if (attr.Value != null)
                return EvaluateEquals(target, attr.Condition, attr.Value);
            return Evaluate(target, attr.Condition);
        }

        private static bool ToBool(object value)
        {
            if (value is bool b) return b;
            if (value == null) return false;
            return Convert.ToBoolean(value);
        }
    }
}
