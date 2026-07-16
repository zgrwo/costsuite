using System;
using System.Data;
using Dapper;

namespace BomAddIn.Infrastructure.Config
{
    /// <summary>
    /// Dapper 类型处理器 — 将 SQLite TEXT 列映射到 C# 枚举。
    /// 解决枚举在数据库存为字符串（如 "Admin", "Draft"）时 Dapper 默认按整数解析的问题。
    /// </summary>
    public class EnumStringTypeHandler<T> : SqlMapper.TypeHandler<T> where T : struct, Enum
    {
        public override void SetValue(IDbDataParameter parameter, T value)
        {
            parameter.Value = value.ToString();
        }

        public override T Parse(object value)
        {
            if (value is string str && Enum.TryParse<T>(str, ignoreCase: true, out var result))
                return result;

            // Fallback: 尝试整数转换（兼容旧数据可能存储为数字的情况）
            if (value is long l || (value is int i))
            {
                var intVal = value is long longVal ? (int)longVal : (int)value;
                if (Enum.IsDefined(typeof(T), intVal))
                    return (T)Enum.ToObject(typeof(T), intVal);
            }

            // H-29: 未知枚举值警告 — 静默 fallback 对 UserRole 等安全敏感枚举可导致权限提升
            Infrastructure.Logging.AppLogger.Warn(
                $"类型处理器 {typeof(T).Name}: 无法解析值 '{value}'，回退到 default({typeof(T).Name}) = {default(T)}。",
                typeof(EnumStringTypeHandler<T>));
            return default;
        }
    }
}
