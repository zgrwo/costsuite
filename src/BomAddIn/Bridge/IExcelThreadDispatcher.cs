using System;
using System.Threading.Tasks;

namespace BomAddIn.Bridge
{
    /// <summary>
    /// Excel 主线程调度器。
    /// 所有 WPF 及其他非 Excel 主线程对 Excel COM 的调用必须通过此接口封送，
    /// 以避免 RPC_E_WRONG_THREAD 异常。
    /// </summary>
    public interface IExcelThreadDispatcher
    {
        /// <summary>
        /// 同步封送执行。若调用方已在 Excel 主线程上则直接执行，避免死锁。
        /// </summary>
        T RunOnExcelThread<T>(Func<T> action);

        /// <summary>
        /// 异步封送执行。不阻塞调用方线程。
        /// </summary>
        Task<T> RunOnExcelThreadAsync<T>(Func<T> action);

        /// <summary>
        /// 当前是否正在 Excel 主线程上执行。
        /// </summary>
        bool IsExcelMainThread { get; }
    }
}
