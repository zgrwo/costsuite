using System;

// ReSharper disable once CheckNamespace — must match ExcelDna.Integration namespace for UDF source compatibility
namespace ExcelDna.Integration
{
    /// <summary>Stub of ExcelDna.Integration.ExcelFunctionAttribute for test compilation on net8.0.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ExcelFunctionAttribute : Attribute
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool IsThreadSafe { get; set; }
        public bool IsVolatile { get; set; }
    }

    /// <summary>Stub of ExcelDna.Integration.ExcelArgumentAttribute for test compilation on net8.0.</summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class ExcelArgumentAttribute : Attribute
    {
        public ExcelArgumentAttribute(string description) { }
    }

    /// <summary>Stub of ExcelDna.Integration.ExcelError for test compilation on net8.0.</summary>
    public class ExcelError
    {
        /// <summary>Represents Excel #N/A error.</summary>
        public static readonly object ExcelErrorNA = new ExcelError { _kind = "NA" };

        /// <summary>Represents Excel #VALUE! error.</summary>
        public static readonly object ExcelErrorValue = new ExcelError { _kind = "VALUE" };

        private string _kind = string.Empty;

        public override bool Equals(object? obj)
        {
            if (obj is ExcelError other)
                return _kind == other._kind;
            return false;
        }

        public override int GetHashCode()
        {
            return _kind.GetHashCode();
        }

        public override string ToString()
        {
            return $"ExcelError({_kind})";
        }
    }

    /// <summary>Stub of ExcelDna.Integration.ExcelMissing for test compilation on net8.0.</summary>
    public class ExcelMissing
    {
        public static readonly ExcelMissing Value = new();

        private ExcelMissing() { }
    }

    /// <summary>Stub of ExcelDna.Integration.ExcelEmpty for test compilation on net8.0.</summary>
    public class ExcelEmpty
    {
        public static readonly ExcelEmpty Value = new();

        private ExcelEmpty() { }
    }
}
