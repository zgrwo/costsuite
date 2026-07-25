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

            // H-31: 未知枚举值 → 对安全敏感枚举抛异常，防止权限提升
            Infrastructure.Logging.AppLogger.Error(
                $"类型处理器 {typeof(T).Name}: 无法解析值 '{value}'。抛出异常以防止数据损坏传播。",
                null, typeof(EnumStringTypeHandler<T>));
            throw new ArgumentException(
                $"数据库中存在无法识别的 {typeof(T).Name} 值: '{value}'。" +
                "请检查数据迁移是否完整，或代码中的枚举定义是否与数据库同步。");
        }
    }
}
